using System.Buffers;
using System.Globalization;

namespace PocketCsvReader.SecurityLogs;

internal sealed class SecurityLogParser
{
    private static readonly string[] CefHeaders = ["CefVersion", "DeviceVendor", "DeviceProduct", "DeviceVersion", "DeviceEventClassId", "Name", "Severity"];
    private static readonly string[] LeefHeaders = ["LeefVersion", "Vendor", "Product", "ProductVersion", "EventId"];
    // Scratch metadata is retained, overwritten, and cleared for each record.
    public List<FieldSpan> Fields { get; } = [];
    public static IReadOnlyList<string> HeaderNames(SecurityLogFormat format) => format == SecurityLogFormat.Cef ? CefHeaders : LeefHeaders;

    public void Parse(ReadOnlySpan<char> line, SecurityLogFormat format)
    {
        Fields.Clear();
        int position = SyslogEnvelope.PayloadOffset(line);
        bool cef = format == SecurityLogFormat.Cef;
        var prefix = cef ? "CEF:" : "LEEF:";
        if (!line[position..].StartsWith(prefix)) throw new InvalidDataException($"Expected {prefix} payload.");
        position += prefix.Length;
        ParseHeaders(line, format, ref position);
        var version = line.Slice(Fields[0].Value.Start, Fields[0].Value.Length);
        if (cef)
        {
            if (!version.SequenceEqual("0") && !version.SequenceEqual("1")) throw new InvalidDataException("Supported CEF versions are 0 and 1.");
            ParseCefAttributes(line, position);
        }
        else
        {
            var delimiter = ReadLeefDelimiter(line, version, ref position);
            ParseLeefAttributes(line, position, delimiter);
        }
    }

    private void ParseHeaders(ReadOnlySpan<char> line, SecurityLogFormat format, ref int position)
    {
        bool cef = format == SecurityLogFormat.Cef;
        var names = HeaderNames(format);
        for (int i = 0; i < names.Count; i++)
        {
            int start = position;
            FindHeaderEnd(line, cef, ref position);
            if (position == line.Length) throw new InvalidDataException($"Missing header separator after {names[i]}.");
            var raw = line.Slice(start, position - start);
            Fields.Add(new FieldSpan(new SpanInfo(start, raw.Length), default,
                DecodedValue: cef ? Decode(raw, header: true) : null, DecodedLabel: names[i]));
            position++;
        }
    }

    private static void FindHeaderEnd(ReadOnlySpan<char> line, bool cef, ref int position)
    {
        while (position < line.Length && line[position] != '|')
        {
            if (cef && line[position] == '\\')
            {
                if (++position >= line.Length || line[position] is not ('\\' or '|'))
                    throw new InvalidDataException("Invalid CEF header escape.");
            }
            position++;
        }
    }

    private static char ReadLeefDelimiter(ReadOnlySpan<char> line, ReadOnlySpan<char> version, ref int position)
    {
        if (!version.SequenceEqual("1.0") && !version.SequenceEqual("2.0")) throw new InvalidDataException("Supported LEEF versions are 1.0 and 2.0.");
        if (version.SequenceEqual("1.0")) return '\t';
        int end = line[position..].IndexOf('|');
        if (end < 0) throw new InvalidDataException("LEEF 2.0 requires a delimiter declaration followed by '|'.");
        var delimiter = ParseDelimiter(line.Slice(position, end));
        position += end + 1;
        return delimiter;
    }

    private static void SkipSpaces(ReadOnlySpan<char> line, ref int position)
    {
        while (position < line.Length && line[position] == ' ') position++;
    }

    private static int ReadCefKey(ReadOnlySpan<char> line, ref int position)
    {
        int start = position;
        while (position < line.Length && IsKeyChar(line[position])) position++;
        if (position == start || position == line.Length || line[position] != '=')
            throw new InvalidDataException("Expected a CEF extension key=value pair.");
        return position - start;
    }

    private static bool TryFindNextCefKey(ReadOnlySpan<char> line, int position, out int key)
    {
        SkipSpaces(line, ref position);
        key = position;
        while (position < line.Length && IsKeyChar(line[position])) position++;
        return position > key && position < line.Length && line[position] == '=';
    }

    private static int ReadCefValueEnd(ReadOnlySpan<char> line, ref int position)
    {
        while (position < line.Length)
        {
            char c = line[position];
            if (c == '\\')
            {
                if (++position >= line.Length || line[position] is not ('\\' or '=' or 'n' or 'r'))
                    throw new InvalidDataException("Invalid CEF extension escape.");
            }
            else if (c == '=')
            {
                throw new InvalidDataException("An equals sign inside a CEF extension value must be escaped.");
            }
            else if (c == ' ' && TryFindNextCefKey(line, position, out int key))
            {
                int end = position;
                position = key;
                return end;
            }
            position++;
        }
        return line.Length;
    }

    private void ParseCefAttributes(ReadOnlySpan<char> line, int position)
    {
        while (position < line.Length)
        {
            SkipSpaces(line, ref position);
            if (position == line.Length) return;
            int keyStart = position;
            int keyLength = ReadCefKey(line, ref position);
            int valueStart = ++position;
            int valueEnd = ReadCefValueEnd(line, ref position);
            AddAttribute(line, keyStart, keyLength, valueStart, valueEnd - valueStart, cef: true);
        }
    }

    private void ParseLeefAttributes(ReadOnlySpan<char> line, int position, char delimiter)
    {
        while (position < line.Length)
        {
            int end = line[position..].IndexOf(delimiter);
            end = end < 0 ? line.Length : position + end;
            var pair = line[position..end];
            int equals = pair.IndexOf('=');
            if (equals <= 0) throw new InvalidDataException("Expected a LEEF attribute key=value pair.");
            foreach (char c in pair[..equals])
                if (!IsKeyChar(c)) throw new InvalidDataException("Invalid LEEF attribute key.");
            AddAttribute(line, position, equals, position + equals + 1, pair.Length - equals - 1, cef: false);
            position = end + 1;
        }
    }

    private void AddAttribute(ReadOnlySpan<char> line, int keyStart, int keyLength, int valueStart, int valueLength, bool cef)
    {
        var key = line.Slice(keyStart, keyLength);
        foreach (var field in Fields)
        {
            var label = field.DecodedLabel is { } name ? name.AsSpan() : line.Slice(field.Label.Start, field.Label.Length);
            if (key.SequenceEqual(label)) throw new InvalidDataException("Duplicate attribute or attribute colliding with a header name.");
        }
        Fields.Add(new FieldSpan(new SpanInfo(valueStart, valueLength), new SpanInfo(keyStart, keyLength),
            DecodedValue: cef ? Decode(line.Slice(valueStart, valueLength), header: false) : null));
    }

    private static bool IsKeyChar(char c) => !char.IsWhiteSpace(c) && c is not ('=' or '\\' or '|');

    private static char ParseDelimiter(ReadOnlySpan<char> text)
    {
        if (text.Length == 1) return ValidateDelimiter(text[0]);
        var digits = text.StartsWith("0x") ? text[2..] : text.StartsWith("x") ? text[1..] : default;
        if (digits.Length is < 1 or > 4 || !ushort.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var value))
            throw new InvalidDataException("Invalid LEEF 2.0 delimiter declaration.");
        return ValidateDelimiter((char)value);
    }

    private static char ValidateDelimiter(char value)
        => value is '\r' or '\n' or '\0' or '=' || char.IsSurrogate(value)
            ? throw new InvalidDataException("LEEF delimiter must be a non-surrogate character other than NUL, CR, LF, or '='.") : value;

    private static string? Decode(ReadOnlySpan<char> value, bool header)
    {
        if (!value.Contains('\\')) return null;
        var buffer = ArrayPool<char>.Shared.Rent(value.Length);
        try
        {
            int count = 0;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c == '\\')
                {
                    if (++i == value.Length) throw new InvalidDataException("Truncated CEF escape.");
                    c = DecodeEscape(value[i], header);
                }
                buffer[count++] = c;
            }
            // Only escaped values need an independently owned decoded string.
            return new string(buffer, 0, count);
        }
        finally { ArrayPool<char>.Shared.Return(buffer); }
    }

    private static char DecodeEscape(char c, bool header)
    {
        if (header)
            return c is '|' or '\\' ? c : throw new InvalidDataException("Invalid CEF escape.");
        return c switch
        {
            '=' or '\\' => c,
            'n' => '\n',
            'r' => '\r',
            _ => throw new InvalidDataException("Invalid CEF escape.")
        };
    }
}
