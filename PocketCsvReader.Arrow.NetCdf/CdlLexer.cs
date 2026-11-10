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

    public async ValueTask<CdlToken> ReadAsync()
    {
        token.ThrowIfCancellationRequested();
        while (await HasInputAsync().ConfigureAwait(false))
        {
            var current = _chunk.Span[_index];
            var position = _offset + _index;
            if (char.IsWhiteSpace(current))
            {
                _index++;
                continue;
            }

            switch (current)
            {
                case '/':
                    _index++;
                    await SkipCommentAsync(position).ConfigureAwait(false);
                    continue;
                case '{' or '}' or '(' or ')' or ':' or ';' or ',' or '=':
                    _index++;
                    return new(current, default, position);
                case '"':
                    _index++;
                    return await ReadTextAsync('"', position).ConfigureAwait(false);
                default:
                    return await ReadTextAsync('w', position).ConfigureAwait(false);
            }
        }

        return new('\0', default, _offset + _index);
    }

    private ValueTask<bool> HasInputAsync()
    {
        if (_index < _chunk.Length)
            return ValueTask.FromResult(true);
        if (input.IsEof)
            return ValueTask.FromResult(false);
        return RefillAsync();
    }

    private async ValueTask<bool> RefillAsync()
    {
        token.ThrowIfCancellationRequested();
        _offset += _chunk.Length;
        _index = 0;
        _chunk = asynchronous ? await input.ReadAsync(token).ConfigureAwait(false) : input.Read();
        return !_chunk.IsEmpty;
    }

    private async ValueTask SkipCommentAsync(long position)
    {
        if (!await HasInputAsync().ConfigureAwait(false))
            throw new CdlToken('/', default, position).Error("Expected a CDL comment");

        switch (_chunk.Span[_index++])
        {
            case '/':
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

                return;
            case '*':
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
            default:
                throw new CdlToken('/', default, position).Error("Expected // or /* comment");
        }
    }

    private async ValueTask<CdlToken> ReadTextAsync(char kind, long position)
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
                    throw new CdlToken(kind, default, _offset + _index).Error("Unexpected NUL in CDL text");
                if (kind == 'w')
                {
                    if (char.IsWhiteSpace(next) || next is '{' or '}' or '(' or ')' or ':' or ';' or ',' or '=' or '/' or '"')
                        return Complete(kind, position, start);
                }
                else
                {
                    if (!escaped && next == '"')
                    {
                        var literal = Complete(kind, position, start);
                        _index++;
                        return literal;
                    }

                    if (next is '\r' or '\n')
                        throw new CdlToken(kind, default, position).Error("Unterminated quoted string");
                    escaped = !escaped && next == '\\';
                }

                _index++;
            }

            if (kind == 'w' && input.IsEof)
                return Complete(kind, position, start);
            Append(position, span[start..]);
        }
        while (await HasInputAsync().ConfigureAwait(false));

        if (kind == '"')
            throw new CdlToken(kind, default, position).Error("Unterminated quoted string");
        return new(kind, _overflow.WrittenMemory, position);
    }

    private CdlToken Complete(char kind, long position, int start)
    {
        var segment = _chunk[start.._index];
        if (_overflow.WrittenCount == 0)
            return new(kind, segment, position);

        Append(position, segment.Span);
        return new(kind, _overflow.WrittenMemory, position);
    }

    private void Append(long position, ReadOnlySpan<char> segment)
    {
        if (segment.Length > MaxTokenLength - _overflow.WrittenCount)
            throw new CdlToken('w', default, position).Error($"Token exceeds {MaxTokenLength} characters");
        _overflow.Write(segment);
    }
}
