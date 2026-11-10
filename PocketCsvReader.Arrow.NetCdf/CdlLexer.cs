using System.Buffers;

namespace PocketCsvReader.Arrow.NetCdf;

// Token text borrows the input chunk or reusable overflow storage until the next read.
internal readonly record struct CdlToken(char Kind, ReadOnlyMemory<char> Text, long Position)
{
    public bool IsWord(string word) => Kind == 'w' && Text.Span.SequenceEqual(word);

    public InvalidDataException Error(string message) => new($"{message} (CDL character offset {Position}).");
}

internal sealed class CdlLexer(IBufferReader input, bool asynchronous, CancellationToken token)
{
    private const int MaxTokenLength = 1_048_576;
    private readonly ArrayBufferWriter<char> _overflow = new();
    private ReadOnlyMemory<char> _chunk;
    private int _index;
    private long _offset;

    public ValueTask<CdlToken> ReadAsync()
    {
        token.ThrowIfCancellationRequested();
        return TryReadBuffered(out var current) ? ValueTask.FromResult(current) : ReadSlowAsync();
    }

    private bool TryReadBuffered(out CdlToken token)
    {
        token = default;
        var span = _chunk.Span;
        while (_index < span.Length && char.IsWhiteSpace(span[_index]))
            _index++;
        if (_index == span.Length)
            return false;

        var start = _index;
        var position = _offset + start;
        switch (span[start])
        {
            case '/' or '"':
                return false;
            case '{' or '}' or '(' or ')' or ':' or ';' or ',' or '=':
                _index++;
                token = new(span[start], default, position);
                return true;
        }

        var end = FindWordEnd(span, start);
        if (end == span.Length && !input.IsEof)
            return false;
        _index = end;
        token = new('w', _chunk[start..end], position);
        return true;
    }

    private async ValueTask<CdlToken> ReadSlowAsync()
    {
        while (true)
        {
            if (_index == _chunk.Length)
            {
                if (!await HasInputAsync().ConfigureAwait(false))
                    return new('\0', default, _offset + _index);
            }
            else
            {
                var position = _offset + _index;
                switch (_chunk.Span[_index])
                {
                    case '/':
                        _index++;
                        await SkipCommentAsync(position).ConfigureAwait(false);
                        break;
                    case '"':
                        _index++;
                        return await ReadQuotedAsync(position).ConfigureAwait(false);
                    default:
                        return await ReadCrossBufferWordAsync(position).ConfigureAwait(false);
                }
            }

            if (TryReadBuffered(out var current))
                return current;
        }
    }

    private ValueTask<bool> HasInputAsync()
    {
        if (_index < _chunk.Length)
            return ValueTask.FromResult(true);
        if (input.IsEof)
            return ValueTask.FromResult(false);
        return RefillAsync();
    }

    private ValueTask<bool> RefillAsync()
    {
        token.ThrowIfCancellationRequested();
        _offset += _chunk.Length;
        _index = 0;
        return asynchronous ? RefillChunkAsync() : ValueTask.FromResult(SetChunk(input.Read()));
    }

    private async ValueTask<bool> RefillChunkAsync()
        => SetChunk(await input.ReadAsync(token).ConfigureAwait(false));

    private bool SetChunk(ReadOnlyMemory<char> chunk)
    {
        _chunk = chunk;
        return !_chunk.IsEmpty;
    }

    private async ValueTask SkipCommentAsync(long position)
    {
        if (!await HasInputAsync().ConfigureAwait(false))
            throw new CdlToken('/', default, position).Error("Expected a CDL comment");

        switch (_chunk.Span[_index++])
        {
            case '/':
                await SkipLineCommentAsync().ConfigureAwait(false);
                return;
            case '*':
                await SkipBlockCommentAsync(position).ConfigureAwait(false);
                return;
            default:
                throw new CdlToken('/', default, position).Error("Expected // or /* comment");
        }
    }

    private async ValueTask SkipLineCommentAsync()
    {
        while (await HasInputAsync().ConfigureAwait(false))
        {
            var end = _chunk.Span[_index..].IndexOfAny('\r', '\n');
            if (end >= 0)
            {
                _index += end + 1;
                return;
            }

            _index = _chunk.Length;
        }
    }

    private async ValueTask SkipBlockCommentAsync(long position)
    {
        var star = false;
        while (await HasInputAsync().ConfigureAwait(false))
        {
            while (_index < _chunk.Length)
            {
                var next = _chunk.Span[_index++];
                if (star && next == '/')
                    return;
                star = next == '*';
            }
        }

        throw new CdlToken('/', default, position).Error("Unterminated block comment");
    }

    private async ValueTask<CdlToken> ReadCrossBufferWordAsync(long position)
    {
        _overflow.Clear();
        do
        {
            var start = _index;
            var span = _chunk.Span;
            _index = FindWordEnd(span, start);
            Append(position, span[start.._index]);
            if (_index < span.Length)
                break;
        }
        while (await HasInputAsync().ConfigureAwait(false));

        return new('w', _overflow.WrittenMemory, position);
    }

    private async ValueTask<CdlToken> ReadQuotedAsync(long position)
    {
        _overflow.Clear();
        var escaped = false;
        do
        {
            var start = _index;
            var span = _chunk.Span;
            while (_index < span.Length)
            {
                var next = span[_index];
                if (next == '\0')
                    throw NulError(_index);
                if (!escaped && next == '"')
                {
                    var literal = CompleteQuoted(position, start);
                    _index++;
                    return literal;
                }

                if (next is '\r' or '\n')
                    throw new CdlToken('"', default, position).Error("Unterminated quoted string");
                escaped = !escaped && next == '\\';
                _index++;
            }

            Append(position, span[start..]);
        }
        while (await HasInputAsync().ConfigureAwait(false));

        throw new CdlToken('"', default, position).Error("Unterminated quoted string");
    }

    private int FindWordEnd(ReadOnlySpan<char> span, int index)
    {
        while (index < span.Length)
        {
            var next = span[index];
            if (next == '\0')
                throw NulError(index);
            if (char.IsWhiteSpace(next) || next is '{' or '}' or '(' or ')' or ':' or ';' or ',' or '=' or '/' or '"')
                break;
            index++;
        }

        return index;
    }

    private InvalidDataException NulError(int index)
        => new CdlToken('\0', default, _offset + index).Error("Unexpected NUL in CDL text");

    private CdlToken CompleteQuoted(long position, int start)
    {
        var segment = _chunk[start.._index];
        if (_overflow.WrittenCount == 0)
            return new('"', segment, position);

        Append(position, segment.Span);
        return new('"', _overflow.WrittenMemory, position);
    }

    private void Append(long position, ReadOnlySpan<char> segment)
    {
        if (segment.Length > MaxTokenLength - _overflow.WrittenCount)
            throw new CdlToken('w', default, position).Error($"Token exceeds {MaxTokenLength} characters");
        _overflow.Write(segment);
    }
}
