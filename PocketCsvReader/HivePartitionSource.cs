using System.Globalization;

namespace PocketCsvReader;

internal sealed class HivePartitionSource(
    string root, Func<string, IEnumerable<string>> enumerateDirectories, Func<string, IEnumerable<string>> enumerateFiles)
{
    internal static readonly NamedPartitions Empty = new([]);
    private static readonly EnumerationOptions FileOptions = new() { AttributesToSkip = 0, IgnoreInaccessible = false };
    private static readonly EnumerationOptions DirectoryOptions = new()
    {
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = false
    };

    internal HivePartitionSource(string root, string searchPattern)
        : this(root, path => Directory.EnumerateDirectories(path, "*", DirectoryOptions),
            path => Directory.EnumerateFiles(path, searchPattern, FileOptions))
    {
    }

    internal static void ValidateSearchPattern(string searchPattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(searchPattern);
        if (searchPattern.IndexOfAny(['/', '\\', '\0']) >= 0 || Path.IsPathRooted(searchPattern) || searchPattern == "..")
            throw new ArgumentException("The search pattern must be a filename glob.", nameof(searchPattern));
    }

    public IEnumerable<PartitionedFile<NamedPartitions>> Discover(
        IReadOnlyList<PartitionCondition> conditions, FileNamePartitionPattern? pattern, Func<string, Stream>? open)
        => Visit(root, Empty, conditions, pattern, open);

    private IEnumerable<PartitionedFile<NamedPartitions>> Visit(
        string directory, NamedPartitions partitions, IReadOnlyList<PartitionCondition> conditions,
        FileNamePartitionPattern? pattern, Func<string, Stream>? open)
    {
        if (ShouldPrune(partitions, conditions))
            yield break;

        foreach (var path in enumerateFiles(directory))
        {
            var metadata = pattern is null ? partitions : partitions.WithValues(pattern.Extract(path), path);
            yield return new PartitionedFile<NamedPartitions>(path, metadata, open is null ? null : () => open(path));
        }

        foreach (var child in enumerateDirectories(directory))
        {
            var metadata = AddDirectory(partitions, Path.GetFileName(child), child);
            foreach (var file in Visit(child, metadata, conditions, pattern, open))
                yield return file;
        }
    }

    internal static bool ShouldPrune(NamedPartitions partitions, IReadOnlyList<PartitionCondition> conditions)
    {
        foreach (var condition in conditions)
        {
            if (partitions.TryGetValue(condition.Key, out var value) && !condition.Matches(value))
                return true;
        }
        return false;
    }

    internal static NamedPartitions AddDirectory(NamedPartitions inherited, string name, string path)
    {
        var separator = name.IndexOf('=');
        if (separator < 0)
            return inherited;

        var key = DecodePartitionText(name[..separator]);
        var value = DecodePartitionText(name[(separator + 1)..]);
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            throw new FormatException($"Invalid Hive partition directory '{path}'. Expected a nonblank key and value.");

        return inherited.WithValues([new(key, value)], path);
    }

    private static string DecodePartitionText(string text)
    {
        var read = text.IndexOf('%');
        if (read < 0)
            return text;

        var decoded = text.ToCharArray();
        var written = read;
        while (read < text.Length)
        {
            var character = text[read++];
            if (character == '%' && read + 1 < text.Length
                && byte.TryParse(text.AsSpan(read, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code))
            {
                character = (char)code;
                read += 2;
            }
            decoded[written++] = character;
        }
        return new string(decoded, 0, written);
    }
}
