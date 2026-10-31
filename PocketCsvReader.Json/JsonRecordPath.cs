using System.Globalization;
using System.Text;

namespace PocketCsvReader.Json;

// A null segment is an array wildcard; other segments are literal object member names.
internal sealed class JsonRecordPath(string?[] segments)
{
    public static JsonRecordPath Parse(string path)
        => new PathParser(path).Parse();

    private sealed class PathParser(string path)
    {
        private int _position;

        public JsonRecordPath Parse()
        {
            Expect('$');
            var segments = new List<string?>();
            while (_position < path.Length)
                segments.Add(ParseSegment());
            return new(segments.ToArray());
        }

        private string? ParseSegment()
            => path[_position++] switch
            {
                '.' => ParseDotName(),
                '[' => ParseBracketSegment(),
                _ => throw Invalid()
            };

        private string ParseDotName()
        {
            var start = _position;
            if (_position == path.Length || !IsNameFirst(path[_position]))
                throw Invalid();
            _position++;
            while (_position < path.Length && IsNameCharacter(path[_position]))
                _position++;
            var name = path[start.._position];
            return ValidUnicode(name) ? name : throw Invalid();
        }

        private static bool IsNameCharacter(char value)
            => IsNameFirst(value) || char.IsAsciiDigit(value);

        private string? ParseBracketSegment()
        {
            if (_position == path.Length)
                throw Invalid();
            string? name = null;
            if (path[_position] == '*')
                _position++;
            else
                name = ParseQuotedName();
            Expect(']');
            return name;
        }

        private string ParseQuotedName()
        {
            var quote = path[_position++];
            if (quote is not ('\'' or '"'))
                throw Invalid();
            var name = new StringBuilder();
            while (_position < path.Length && path[_position] != quote)
                name.Append(ParseNameCharacter(quote));
            Expect(quote);
            var value = name.ToString();
            return ValidUnicode(value) ? value : throw Invalid();
        }

        private char ParseNameCharacter(char quote)
        {
            var current = path[_position++];
            if (current < ' ')
                throw Invalid();
            return current == '\\' ? ParseEscape(quote) : current;
        }

        private char ParseEscape(char quote)
        {
            if (_position == path.Length)
                throw Invalid();
            var escape = path[_position++];
            if (escape == quote || escape is '\\' or '/')
                return escape;
            return escape switch
            {
                'u' => ParseUnicodeEscape(),
                'b' => '\b', 'f' => '\f', 'n' => '\n', 'r' => '\r', 't' => '\t',
                _ => throw Invalid()
            };
        }

        private char ParseUnicodeEscape()
        {
            if (_position + 4 > path.Length
                || !ushort.TryParse(path.AsSpan(_position, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code))
            {
                throw Invalid();
            }
            _position += 4;
            return (char)code;
        }

        private void Expect(char expected)
        {
            if (_position == path.Length || path[_position++] != expected)
                throw Invalid();
        }

        private ArgumentException Invalid()
            => new($"Invalid record path '{path}'.", nameof(path));
    }

    private static bool IsNameFirst(char value)
        => value is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or '_' or >= '\u0080';

    private static bool ValidUnicode(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (!char.IsSurrogate(value[index]))
                continue;
            if (!char.IsHighSurrogate(value[index]) || ++index == value.Length || !char.IsLowSurrogate(value[index]))
                return false;
        }
        return true;
    }

    public IEnumerable<ReadOnlyMemory<char>> Select(ReadOnlyMemory<char> json, char[] whitespaces, FieldSpan[]? parsedFields = null, bool jsonc = false)
    {
        // Parse once, then enumerate matches lazily. The caller retains the captured frame
        // until enumeration ends; matches are slices rather than a materialized result list.
        var fields = parsedFields ?? JsonRecordParser.Parse(json.Span, whitespaces, jsonc);
        var start = 0;
        while (start < json.Length && Array.IndexOf(whitespaces, json.Span[start]) >= 0)
            start++;
        var root = json.Span[start] == '{'
            ? new FieldSpan(new SpanInfo(start, json.Length - start), default, fields)
            : fields[0];
        return SelectNode(json, root, 0);
    }

    private IEnumerable<ReadOnlyMemory<char>> SelectNode(ReadOnlyMemory<char> json, FieldSpan node, int depth)
    {
        if (depth == segments.Length)
        {
            var quoted = node.Value.WasQuoted;
            yield return json.Slice(node.Value.Start - (quoted ? 1 : 0), node.Value.Length + (quoted ? 2 : 0));
            yield break;
        }
        foreach (var child in SelectChildren(json, node, segments[depth]))
            foreach (var match in SelectNode(json, child, depth + 1))
                yield return match;
    }

    private static IEnumerable<FieldSpan> SelectChildren(ReadOnlyMemory<char> json, FieldSpan node, string? name)
    {
        if (node.Children is null || node.Value.WasQuoted)
            yield break;
        var kind = json.Span[node.Value.Start];
        if (name is null)
        {
            if (kind == '[')
                foreach (var child in node.Children)
                    yield return child;
            yield break;
        }
        if (kind == '{' && FindMember(json, node.Children, name) is { } member)
            yield return member;
    }

    private static FieldSpan? FindMember(ReadOnlyMemory<char> json, FieldSpan[] children, string name)
    {
        FieldSpan? selected = null;
        foreach (var child in children)
        {
            var matches = child.DecodedLabel is { } label
                ? string.Equals(label, name, StringComparison.Ordinal)
                : json.Span.Slice(child.Label.Start, child.Label.Length).SequenceEqual(name);
            if (!matches)
                continue;
            if (selected.HasValue)
                throw new InvalidDataException($"Duplicate matching member '{name}' at position {child.Label.Start}.");
            selected = child;
        }
        return selected;
    }
}
