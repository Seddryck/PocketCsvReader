using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Compression;
internal class GZipDecompressor : IDecompressor
{
    private readonly Func<Stream, Stream> _decompressionStrategy;

    public GZipDecompressor(Func<Stream, Stream> decompressionStrategy)
    {
        _decompressionStrategy = decompressionStrategy ?? throw new ArgumentNullException(nameof(decompressionStrategy));
    }

    public Stream Decompress(Stream compressedStream)
    {
        return _decompressionStrategy(compressedStream);
    }

    public static GZipDecompressor Streaming() => new GZipDecompressor(StreamingStrategy);
    public static GZipDecompressor Buffered() => new GZipDecompressor(BufferedStrategy);

    private static Stream StreamingStrategy(Stream compressedStream)
    {
        return new GZipStream(compressedStream, CompressionMode.Decompress, leaveOpen: true);
    }

    private static Stream BufferedStrategy(Stream compressedStream)
    {
        if (compressedStream.CanSeek)
            compressedStream.Seek(0, SeekOrigin.Begin);

        var outputStream = new MemoryStream();
        using (var gzip = new GZipStream(compressedStream, CompressionMode.Decompress, leaveOpen: true))
        {
            gzip.CopyTo(outputStream);
        }

        outputStream.Seek(0, SeekOrigin.Begin);
        return outputStream;
    }
}
