using System.IO.Compression;

namespace PocketCsvReader.Compression;

public class BrotliDecompressor : IDecompressor
{
    private readonly Func<Stream, Stream> _decompressionStrategy;

    public BrotliDecompressor(Func<Stream, Stream> decompressionStrategy)
    {
        _decompressionStrategy = decompressionStrategy ?? throw new ArgumentNullException(nameof(decompressionStrategy));
    }

    public Stream Decompress(Stream compressedStream)
        => _decompressionStrategy(compressedStream);

    public static BrotliDecompressor Streaming() => new(StreamingStrategy);
    public static BrotliDecompressor Buffered() => new(BufferedStrategy);

    private static Stream StreamingStrategy(Stream compressedStream)
        => new BrotliStream(compressedStream, CompressionMode.Decompress, leaveOpen: true);

    private static Stream BufferedStrategy(Stream compressedStream)
    {
        if (compressedStream.CanSeek)
            compressedStream.Seek(0, SeekOrigin.Begin);

        var outputStream = new MemoryStream();
        try
        {
            using var brotli = new BrotliStream(compressedStream, CompressionMode.Decompress, leaveOpen: true);
            brotli.CopyTo(outputStream);
            outputStream.Seek(0, SeekOrigin.Begin);
            return outputStream;
        }
        catch
        {
            outputStream.Dispose();
            throw;
        }
    }
}
