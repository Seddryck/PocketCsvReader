namespace PocketCsvReader.Indexing;

internal readonly record struct SourceStamp(long Length, DateTime LastWriteUtc)
{
    internal static SourceStamp Read(FileStream source)
        => new(source.Length, File.GetLastWriteTimeUtc(source.SafeFileHandle));

    internal static SourceStamp Read(string path)
    {
        var file = new FileInfo(path);
        return new(file.Length, file.LastWriteTimeUtc);
    }
}
