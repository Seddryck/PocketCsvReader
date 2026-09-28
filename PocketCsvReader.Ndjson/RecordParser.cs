using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PocketCsvReader.CharParsing;
using PocketCsvReader.Ndjson.CharParsing;
using PocketCsvReader.Ndjson.Configuration;
using System.Text.Json;

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
            var candidate = _reader.ReadLine();
            if (candidate is null)
                break;
            if (string.IsNullOrWhiteSpace(candidate))
                continue;

            line = RemoveComment(candidate, Profile.Dialect.CommentChar);
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

    private static string? RemoveComment(string line, char? commentChar)
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
            if (content.Length == 0 || IsCompleteJson(content))
                return content;
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

    private static bool IsCompleteJson(string content)
    {
        try
        {
            JsonDocument.Parse(content).Dispose();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
