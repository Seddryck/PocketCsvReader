using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Compression;
public class ZipDecompressor : IDecompressor
{
    private readonly Func<Stream, Stream> _decompressionStrategy;

    public ZipDecompressor(Func<Stream, Stream> decompressionStrategy)
    {
        _decompressionStrategy = decompressionStrategy ?? throw new ArgumentNullException(nameof(decompressionStrategy));
    }

    public Stream Decompress(Stream compressedStream)
    {
        return _decompressionStrategy(compressedStream);
    }

    public static ZipDecompressor Streaming() =>
        new ZipDecompressor(StreamingStrategy);

    public static ZipDecompressor Buffered() =>
        new ZipDecompressor(BufferedStrategy);

    private static Stream StreamingStrategy(Stream zipStream)
    {
        var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count == 0)
            throw new InvalidOperationException("ZIP archive is empty.");

        // Returns the stream for the first entry — caller is responsible for disposal.
        return archive.Entries[0].Open();
    }

    private static Stream BufferedStrategy(Stream zipStream)
    {
        var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, leaveOpen: true);
        if (archive.Entries.Count == 0)
            throw new InvalidOperationException("ZIP archive is empty.");

        var entryStream = archive.Entries[0].Open();
        var output = new MemoryStream();
        using (entryStream)
        {
            entryStream.CopyTo(output);
        }

        output.Seek(0, SeekOrigin.Begin);
        return output;
    }
}
