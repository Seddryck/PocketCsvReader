using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PocketCsvReader.CharParsing;
using PocketCsvReader.Ndjson.CharParsing;
using PocketCsvReader.Ndjson.Configuration;

namespace PocketCsvReader.Ndjson;
public class RecordParser : BaseRecordParser<NdjsonProfile>
{
    private readonly StreamReader? _reader;

    public RecordParser(StreamReader reader, NdjsonProfile profile)
        : this(reader, profile, ArrayPool<char>.Shared)
    { }

    public RecordParser(StreamReader reader, NdjsonProfile profile, ArrayPool<char>? pool)
        : base(profile, new SingleBuffer(reader, 64*1024, pool), pool, (p) => new NdjsonParser(p.Dialect))
        => _reader = reader;

    protected RecordParser(NdjsonProfile profile, IBufferReader buffer, ArrayPool<char>? pool)
        : base(profile, buffer, pool, (p) => new NdjsonParser(p.Dialect))
        => _reader = null;

    public override bool IsEndOfFile(out RecordSpan record, out RecordState recordState)
    {
        if (_reader is null)
            return base.IsEndOfFile(out record, out recordState);

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

        var fields = new JsonRecordParser(line, Profile.Dialect.Whitespaces).ParseRoot();
        record = new RecordSpan(line.AsSpan(), fields);
        recordState = RecordState.Record;
        return _reader.Peek() < 0;
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
            var next = _reader!.Read();
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
            _ = new JsonRecordParser(content, whitespaces).ParseRoot();
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }
}
