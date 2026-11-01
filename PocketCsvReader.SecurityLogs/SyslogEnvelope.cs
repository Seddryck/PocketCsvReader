using System.Globalization;

namespace PocketCsvReader.SecurityLogs;

// Envelope recognition is independent of payload grammar. No arbitrary prefix search:
// a CEF:/LEEF: token inside structured data must not become the event header.
internal static class SyslogEnvelope
{
    public static int PayloadOffset(ReadOnlySpan<char> line)
    {
        if (line.StartsWith("CEF:") || line.StartsWith("LEEF:")) return 0;
        int position = 0;
        bool priority = line.StartsWith("<");
        if (priority) position = ReadPriority(line);

        if (priority && line[position..].StartsWith("1 "))
            return ReadRfc5424(line, position + 2);
        return ReadLegacyEnvelope(line, position);
    }

    private static int ReadPriority(ReadOnlySpan<char> line)
    {
        int end = line.IndexOf('>');
        if (end is < 2 or > 4 || !int.TryParse(line[1..end], NumberStyles.None, CultureInfo.InvariantCulture, out int pri) || pri > 191)
            throw new InvalidDataException("Invalid syslog priority.");
        return end + 1;
    }

    private static int ReadRfc5424(ReadOnlySpan<char> line, int position)
    {
        // RFC 5424: timestamp hostname app-name procid msgid structured-data MSG.
        var timestamp = Token(line, ref position);
        if (!timestamp.SequenceEqual("-") && !DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            throw new InvalidDataException("Invalid RFC 5424 timestamp.");
        for (int i = 0; i < 4; i++) Token(line, ref position);
        SkipStructuredData(line, ref position);
        if (position >= line.Length || line[position++] != ' ') throw new InvalidDataException("Missing syslog message separator.");
        if (position < line.Length && line[position] == '\uFEFF') position++;
        return position;
    }

    private static void SkipStructuredData(ReadOnlySpan<char> line, ref int position)
    {
        if (position < line.Length && line[position] == '-') { position++; return; }
        if (position >= line.Length || line[position] != '[') throw new InvalidDataException("Missing syslog structured data.");
        do { SkipStructuredDataElement(line, ref position); }
        while (position < line.Length && line[position] == '[');
    }

    private static void SkipStructuredDataElement(ReadOnlySpan<char> line, ref int position)
    {
        bool quoted = false;
        for (position++; position < line.Length; position++)
        {
            char c = line[position];
            if (c == '\\' && quoted)
            {
                if (++position >= line.Length) break;
            }
            else if (c == '"') quoted = !quoted;
            else if (c == ']' && !quoted) { position++; return; }
        }
        throw new InvalidDataException("Unterminated syslog structured data.");
    }

    private static int ReadLegacyEnvelope(ReadOnlySpan<char> line, int position)
    {
        // RFC 3164-style timestamp and host, optionally followed by an application tag.
        if (line.Length - position < 16 || !IsLegacyTimestamp(line.Slice(position, 15)))
            throw new InvalidDataException("Expected a bare event, RFC 3164 timestamp/host, or RFC 5424 envelope.");
        position += 15;
        if (line[position++] != ' ') throw new InvalidDataException("Missing syslog hostname separator.");
        Token(line, ref position);
        if (!line[position..].StartsWith("CEF:") && !line[position..].StartsWith("LEEF:"))
        {
            var tag = Token(line, ref position);
            if (!tag.EndsWith(":")) throw new InvalidDataException("Invalid syslog application tag.");
        }
        return position;
    }

    private static bool IsLegacyTimestamp(ReadOnlySpan<char> timestamp)
    {
        int monthIndex = "JanFebMarAprMayJunJulAugSepOctNovDec".AsSpan().IndexOf(timestamp[..3]);
        return monthIndex >= 0 && monthIndex % 3 == 0 && timestamp[3] == ' ' && timestamp[6] == ' '
            && int.TryParse(timestamp.Slice(4, 2).TrimStart(), NumberStyles.None, CultureInfo.InvariantCulture, out int day)
            // RFC 3164 has no year: accept leap day without assuming the current year.
            && day >= 1 && day <= DateTime.DaysInMonth(2000, (monthIndex / 3) + 1)
            && TimeOnly.TryParseExact(timestamp[7..], "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
    }

    private static ReadOnlySpan<char> Token(ReadOnlySpan<char> line, ref int position)
    {
        int start = position;
        int end = line[start..].IndexOf(' ');
        if (end <= 0) throw new InvalidDataException("Incomplete syslog header.");
        position += end + 1;
        return line.Slice(start, end);
    }
}
