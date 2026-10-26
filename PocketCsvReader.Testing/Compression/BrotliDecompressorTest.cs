using System.IO.Compression;
using System.Text;
using NUnit.Framework;
using PocketCsvReader.Compression;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing.Compression;

public class BrotliDecompressorTest
{
    private const string Csv = "id,name\n1,中国人\n2,\"Grace, Hopper\"\n";

    private static MemoryStream Compress(string text = Csv)
    {
        var stream = new MemoryStream();
        using (var compressor = new BrotliStream(stream, CompressionLevel.Optimal, leaveOpen: true))
            compressor.Write(Encoding.UTF8.GetBytes(text));
        stream.Position = 0;
        return stream;
    }

    private static DecompressorFactory CreateFactory(bool buffered)
        => buffered ? DecompressorFactory.Buffered() : DecompressorFactory.Streaming();

    [Test]
    public void Factory_BrotliIdentifiers_DecompressAndDiscover(
        [Values(false, true)] bool buffered,
        [Values("brotli", "br", ".brotli", ".br", "BROTLI", "BR", ".BrOtLi", ".Br")] string identifier)
    {
        var factory = CreateFactory(buffered);
        Assert.That(factory.GetSupportedKeys(), Does.Contain("brotli").And.Contain("br"));
        Assert.That(factory.GetSupportedCompressions(), Does.Contain("brotli").And.Not.Contain("br"));
        Assert.That(factory.GetDecompressor(identifier), Is.SameAs(factory.GetDecompressor("brotli")));
        Assert.That(factory.GetCompression("br"), Is.EqualTo("brotli"));
        using var compressed = Compress();
        using var output = factory.GetDecompressor(identifier).Decompress(compressed);
        using var reader = new StreamReader(output);
        Assert.That(reader.ReadToEnd(), Is.EqualTo(Csv));
    }

    [Test]
    public void Decompress_DisposeOutput_LeavesInputOpen([Values(false, true)] bool buffered)
    {
        using var compressed = Compress();
        var output = CreateFactory(buffered).GetDecompressor("brotli").Decompress(compressed);
        output.CopyTo(Stream.Null);
        output.Dispose();
        Assert.That(output.CanRead, Is.False);
        Assert.That(compressed.CanRead, Is.True);
        compressed.Position = 0;
        Assert.That(compressed.ReadByte(), Is.GreaterThanOrEqualTo(0));
    }

    [Test]
    public void Streaming_IsLazyForwardOnlyAndStartsAtCurrentPosition()
    {
        using var payload = Compress();
        using var compressed = new MemoryStream();
        compressed.WriteByte(0);
        payload.CopyTo(compressed);
        compressed.Position = 1;
        using var output = BrotliDecompressor.Streaming().Decompress(compressed);
        Assert.That(compressed.Position, Is.EqualTo(1));
        Assert.That(output.CanSeek, Is.False);
        using var reader = new StreamReader(output);
        Assert.That(reader.ReadToEnd(), Is.EqualTo(Csv));
    }

    [Test]
    public void Buffered_RewindsSeekableInputAndReturnsIndependentRewoundStream()
    {
        using var compressed = Compress();
        compressed.Position = compressed.Length;
        using var output = BrotliDecompressor.Buffered().Decompress(compressed);
        Assert.That(output.CanSeek, Is.True);
        Assert.That(output.Position, Is.Zero);
        compressed.Dispose();
        using var reader = new StreamReader(output);
        Assert.That(reader.ReadToEnd(), Is.EqualTo(Csv));
        reader.Rewind();
        Assert.That(reader.ReadToEnd(), Is.EqualTo(Csv));
    }

    [Test]
    public void Decompress_NonSeekableInput_Succeeds([Values(false, true)] bool buffered)
    {
        using var payload = Compress();
        using var compressed = new NonSeekableStream(payload.ToArray());
        using var output = CreateFactory(buffered).GetDecompressor("br").Decompress(compressed);
        using var reader = new StreamReader(output);
        Assert.That(reader.ReadToEnd(), Is.EqualTo(Csv));
    }

    [Test]
    public void Decompress_EmptyContent_ReturnsEmptyStream([Values(false, true)] bool buffered)
    {
        using var compressed = Compress("");
        using var output = CreateFactory(buffered).GetDecompressor("br").Decompress(compressed);
        Assert.That(output.ReadByte(), Is.EqualTo(-1));
    }

    [Test]
    public void CsvReader_Brotli_MatchesUncompressedHeadersAndValues(
        [Values(false, true)] bool detectEncoding,
        [Values("brotli", "br", "typed")] string configuration)
    {
        var resource = new ResourceDescriptorBuilder().WithEncoding(detectEncoding ? null : "utf-8");
        if (configuration == "typed")
            resource.WithCompression(CompressionFormat.Brotli);
        else
            resource.WithCompression(configuration);
        Assert.That(resource.Build().Compression, Is.EqualTo(configuration == "typed" ? "brotli" : configuration));
        var profile = new CsvProfile(new DialectDescriptorBuilder()
            .WithDelimiter(',').WithLineTerminator("\n").WithHeader().Build(), null, resource.Build());
        using var compressed = Compress();
        using var plain = new MemoryStream(Encoding.UTF8.GetBytes(Csv));
        var plainProfile = new CsvProfile(profile.Dialect, null, resource.WithoutCompression().Build());
        using var expected = new CsvReader(plainProfile).ToDataReader(plain);
        using var actual = new CsvReader(profile).ToDataReader(compressed);

        var count = 0;
        while (expected.Read())
        {
            Assert.That(actual.Read(), Is.True);
            Assert.That(actual.FieldCount, Is.EqualTo(2));
            Assert.That(actual.FieldCount, Is.EqualTo(expected.FieldCount));
            for (var i = 0; i < expected.FieldCount; i++)
            {
                Assert.That(actual.GetName(i), Is.EqualTo(expected.GetName(i)));
                Assert.That(actual.GetString(i), Is.EqualTo(expected.GetString(i)));
            }
            count++;
        }
        Assert.That(count, Is.EqualTo(2));
        Assert.That(actual.Read(), Is.False);
    }

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override long Seek(long offset, SeekOrigin loc) => throw new NotSupportedException();
    }
}
