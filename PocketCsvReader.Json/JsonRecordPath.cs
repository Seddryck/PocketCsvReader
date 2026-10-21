using System.Globalization;
using System.Text;

namespace PocketCsvReader.Json;

// A null segment is an array wildcard; other segments are literal object member names.
internal sealed class JsonRecordPath(string?[] segments)
{
    public static JsonRecordPath Parse(string path)
    {
        ArgumentException Invalid() => new($"Invalid record path '{path}'.", nameof(path));
        if (path.Length == 0 || path[0] != '$')
            throw Invalid();
        var segments = new List<string?>();
        var index = 1;
        while (index < path.Length)
        {
            if (path[index] == '.')
            {
                var start = ++index;
                if (index == path.Length || !IsNameFirst(path[index]))
                    throw Invalid();
                index++;
                while (index < path.Length && (IsNameFirst(path[index]) || char.IsAsciiDigit(path[index])))
                    index++;
                var name = path[start..index];
                if (!ValidUnicode(name))
                    throw Invalid();
                segments.Add(name);
                continue;
            }
            if (path[index++] != '[' || index == path.Length)
                throw Invalid();
            if (path[index] == '*')
            {
                index++;
                segments.Add(null);
            }
            else
            {
                var quote = path[index++];
                if (quote is not ('\'' or '"'))
                    throw Invalid();
                var name = new StringBuilder();
                while (index < path.Length && path[index] != quote)
                {
                    var current = path[index++];
                    if (current < ' ')
                        throw Invalid();
                    if (current != '\\')
                    {
                        name.Append(current);
                        continue;
                    }
                    if (index == path.Length)
                        throw Invalid();
                    var escape = path[index++];
                    if (escape == 'u')
                    {
                        if (index + 4 > path.Length || !ushort.TryParse(path.AsSpan(index, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code))
                            throw Invalid();
                        name.Append((char)code);
                        index += 4;
                    }
                    else if (escape == quote || escape is '\\' or '/')
                        name.Append(escape);
                    else
                        name.Append(escape switch
                        {
                            'b' => '\b', 'f' => '\f', 'n' => '\n', 'r' => '\r', 't' => '\t',
                            _ => throw Invalid()
                        });
                }
                if (index == path.Length || !ValidUnicode(name.ToString()))
                    throw Invalid();
                index++;
                segments.Add(name.ToString());
            }
            if (index == path.Length || path[index++] != ']')
                throw Invalid();
        }
        return new(segments.ToArray());
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

    public IEnumerable<ReadOnlyMemory<char>> Select(ReadOnlyMemory<char> json, char[] whitespaces, FieldSpan[]? parsedFields = null)
    {
        // Parse once, then enumerate matches lazily. The caller retains the captured frame
        // until enumeration ends; matches are slices rather than a materialized result list.
        var fields = parsedFields ?? JsonRecordParser.Parse(json.Span, whitespaces);
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
        if (node.Children is null || node.Value.WasQuoted)
            yield break;
        var kind = json.Span[node.Value.Start];
        var name = segments[depth];
        if (name is null)
        {
            if (kind != '[')
                yield break;
            foreach (var child in node.Children)
                foreach (var match in SelectNode(json, child, depth + 1))
                    yield return match;
        }
        else if (kind == '{')
        {
            FieldSpan? selected = null;
            foreach (var child in node.Children)
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
            if (selected is { } value)
                foreach (var match in SelectNode(json, value, depth + 1))
                    yield return match;
        }
    }
}
