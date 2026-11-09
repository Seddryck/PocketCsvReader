namespace PocketCsvReader;

internal sealed class FileNamePartitionPattern
{
    private readonly string[] _literals;
    private readonly string[] _keys;

    public FileNamePartitionPattern(string pattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        if (pattern.Any(c => c is '/' or '\\' || char.IsControl(c)))
            throw new ArgumentException("The pattern must describe a filename only.", nameof(pattern));

        var literals = new List<string>();
        var keys = new List<string>();
        var literalStart = 0;
        var start = FindPlaceholder(pattern, literalStart);
        while (start >= 0)
        {
            var end = pattern.IndexOf('}', start + 1);
            var key = ReadKey(pattern, start, end);
            if (keys.Contains(key, StringComparer.Ordinal))
                throw new ArgumentException($"Duplicate filename partition key '{key}'.", nameof(pattern));
            if (keys.Count != 0 && start == literalStart)
                throw new ArgumentException("Filename partition placeholders must be separated by literal text.", nameof(pattern));

            keys.Add(key);
            literals.Add(UnescapeLiteral(pattern[literalStart..start]));
            literalStart = end + 1;
            start = FindPlaceholder(pattern, literalStart);
        }

        if (keys.Count == 0)
            throw new ArgumentException("The filename pattern must contain a partition placeholder.", nameof(pattern));
        literals.Add(UnescapeLiteral(pattern[literalStart..]));
        _literals = [.. literals];
        _keys = [.. keys];
    }

    public NamedPartitions Extract(string path)
    {
        var name = Path.GetFileName(path).AsSpan();
        var prefix = _literals[0];
        var suffix = _literals[^1];
        if (name.Length <= prefix.Length + suffix.Length
            || !name.StartsWith(prefix, StringComparison.Ordinal)
            || !name.EndsWith(suffix, StringComparison.Ordinal))
            throw NoMatch(path);

        var remaining = name[prefix.Length..^suffix.Length];
        var captures = new KeyValuePair<string, string>[_keys.Length];
        for (var i = 0; i < captures.Length - 1; i++)
        {
            var separator = _literals[i + 1];
            var end = remaining.IndexOf(separator, StringComparison.Ordinal);
            if (end <= 0)
                throw NoMatch(path);
            captures[i] = new(_keys[i], remaining[..end].ToString());
            remaining = remaining[(end + separator.Length)..];
        }

        if (remaining.IsEmpty)
            throw NoMatch(path);
        captures[^1] = new(_keys[^1], remaining.ToString());
        return new(captures);
    }

    private static int FindPlaceholder(string pattern, int offset)
    {
        while (offset < pattern.Length)
        {
            var relative = pattern.AsSpan(offset).IndexOfAny('{', '}');
            if (relative < 0)
                return -1;

            var brace = offset + relative;
            if (brace + 1 == pattern.Length || pattern[brace] != pattern[brace + 1])
                return brace;

            offset = brace + 2;
        }
        return -1;
    }

    private static string ReadKey(string pattern, int start, int end)
    {
        if (pattern[start] == '}' || end < 0)
            throw new ArgumentException("The filename pattern has an unmatched brace.", nameof(pattern));

        var key = pattern[(start + 1)..end];
        if (key.Length == 0 || key.Any(c => c == '{' || char.IsWhiteSpace(c)))
            throw new ArgumentException($"Invalid filename partition key '{key}'.", nameof(pattern));

        return key;
    }

    private static string UnescapeLiteral(string literal)
        => literal.Replace("{{", "{", StringComparison.Ordinal).Replace("}}", "}", StringComparison.Ordinal);

    private static FormatException NoMatch(string path)
        => new($"Filename '{Path.GetFileName(path)}' does not match the partition pattern.");
}
