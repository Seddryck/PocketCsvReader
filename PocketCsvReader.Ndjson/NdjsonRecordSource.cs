using System.Text;
using PocketCsvReader.Json;
using PocketCsvReader.Ndjson.Configuration;

namespace PocketCsvReader.Ndjson;

internal sealed class NdjsonRecordSource : IRecordSource<NdjsonProfile>
{
    private readonly StreamReader _reader;

    public NdjsonProfile Profile { get; }

    public NdjsonRecordSource(StreamReader reader, NdjsonProfile profile)
    {
        _reader = reader;
        Profile = profile;
    }

    public bool IsEndOfFile(out RecordSpan record, out RecordState recordState)
    {
        string? line = null;
        while (line is null)
        {
            var candidate = ReadRecord();
            if (candidate is null)
                break;
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            line = RemoveComment(candidate, Profile.Dialect.CommentChar, Profile.Dialect.Whitespaces);
            if (string.IsNullOrWhiteSpace(line))
                line = null;
        }

        if (line is null)
        {
            record = new RecordSpan([], []);
            recordState = RecordState.Eof;
            return true;
        }

        var fields = new JsonRecordParser(line.AsMemory(), Profile.Dialect.Whitespaces).ParseRoot();
        record = new RecordSpan(line.AsSpan(), fields);
        recordState = RecordState.Record;
        return _reader.Peek() < 0;
    }

    public async ValueTask<RecordReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        string? line = null;
        while (line is null)
        {
            var candidate = await ReadRecordAsync(cancellationToken).ConfigureAwait(false);
            if (candidate is null)
                break;
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            line = RemoveComment(candidate, Profile.Dialect.CommentChar, Profile.Dialect.Whitespaces);
            if (string.IsNullOrWhiteSpace(line))
                line = null;
        }

        if (line is null)
            return new(true, RecordMemory.Empty, RecordState.Eof);

        var fields = new JsonRecordParser(line.AsMemory(), Profile.Dialect.Whitespaces).ParseRoot();
        return new(false, new RecordMemory(line.AsSpan(), fields), RecordState.Record);
    }

    private string? ReadRecord()
    {
        var terminator = Profile.Dialect.LineTerminator;
        if (string.IsNullOrEmpty(terminator))
            throw new InvalidOperationException("The line terminator cannot be empty.");

        var builder = new StringBuilder();
        var inString = false;
        var escaping = false;
        var inComment = false;
        while (true)
        {
            var next = _reader.Read();
            if (next < 0)
                return builder.Length == 0 ? null : builder.ToString();

            var current = (char)next;
            var wasInString = inString;
            if (!inComment)
            {
                UpdateStringState(current, ref inString, ref escaping);
                inComment = !wasInString
                    && !inString
                    && current == Profile.Dialect.CommentChar;
            }
            builder.Append(current);

            if ((inComment || (!wasInString && !inString)) && EndsWith(builder, terminator))
            {
                builder.Length -= terminator.Length;
                return builder.ToString();
            }
        }
    }

    private async ValueTask<string?> ReadRecordAsync(CancellationToken cancellationToken)
    {
        var terminator = Profile.Dialect.LineTerminator;
        if (string.IsNullOrEmpty(terminator))
            throw new InvalidOperationException("The line terminator cannot be empty.");

        var builder = new StringBuilder();
        var buffer = new char[1];
        var inString = false;
        var escaping = false;
        var inComment = false;
        while (true)
        {
            var count = await _reader.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (count == 0)
                return builder.Length == 0 ? null : builder.ToString();

            var current = buffer[0];
            var wasInString = inString;
            if (!inComment)
            {
                UpdateStringState(current, ref inString, ref escaping);
                inComment = !wasInString && !inString && current == Profile.Dialect.CommentChar;
            }
            builder.Append(current);

            if ((inComment || (!wasInString && !inString)) && EndsWith(builder, terminator))
            {
                builder.Length -= terminator.Length;
                return builder.ToString();
            }
        }
    }

    private static bool EndsWith(StringBuilder builder, string value)
    {
        if (builder.Length < value.Length)
            return false;

        var offset = builder.Length - value.Length;
        for (var index = 0; index < value.Length; index++)
        {
            if (builder[offset + index] != value[index])
                return false;
        }
        return true;
    }

    private static string? RemoveComment(string line, char? commentChar, char[] whitespaces)
    {
        if (!commentChar.HasValue)
            return line;

        var inString = false;
        var escaping = false;
        for (var i = 0; i < line.Length; i++)
        {
            var current = line[i];
            if (UpdateStringState(current, ref inString, ref escaping))
                continue;

            if (current != commentChar.Value)
                continue;

            var content = line[..i].TrimEnd();
            if (content.Length == 0 || IsCompleteJson(content, whitespaces))
                return content;
            return line;
        }

        return line;
    }

    private static bool UpdateStringState(char current, ref bool inString, ref bool escaping)
    {
        if (!inString)
        {
            if (current != '"')
                return false;
            inString = true;
            return true;
        }

        if (escaping)
            escaping = false;
        else if (current == '\\')
            escaping = true;
        else if (current == '"')
            inString = false;
        return true;
    }

    private static bool IsCompleteJson(string content, char[] whitespaces)
    {
        try
        {
            _ = new JsonRecordParser(content.AsMemory(), whitespaces).ParseRoot();
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    public void Dispose() { }
}
