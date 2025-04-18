using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Compression;
public class DeflateDecompressor : IDecompressor
{
    private readonly Func<Stream, Stream> _decompressionStrategy;

    public DeflateDecompressor(Func<Stream, Stream> decompressionStrategy)
    {
        _decompressionStrategy = decompressionStrategy ?? throw new ArgumentNullException(nameof(decompressionStrategy));
    }

    public Stream Decompress(Stream compressedStream)
    {
        return _decompressionStrategy(compressedStream);
    }

    // Static factory methods for convenience
    public static DeflateDecompressor Streaming() => new DeflateDecompressor(StreamingStrategy);
    public static DeflateDecompressor Buffered() => new DeflateDecompressor(BufferedStrategy);

    private static Stream StreamingStrategy(Stream compressedStream)
    {
        return new DeflateStream(compressedStream, CompressionMode.Decompress, leaveOpen: true);
    }

    private static Stream BufferedStrategy(Stream compressedStream)
    {
        if (compressedStream.CanSeek)
            compressedStream.Seek(0, SeekOrigin.Begin);

        var outputStream = new MemoryStream();
        using (var deflate = new DeflateStream(compressedStream, CompressionMode.Decompress, leaveOpen: true))
        {
            deflate.CopyTo(outputStream);
        }

        outputStream.Seek(0, SeekOrigin.Begin);
        return outputStream;
    }
}
