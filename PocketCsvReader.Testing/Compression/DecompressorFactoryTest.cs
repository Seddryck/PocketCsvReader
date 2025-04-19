using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using PocketCsvReader.Compression;

namespace PocketCsvReader.Testing.Compression;
public class DecompressorFactoryTest
{
    [Test]
    public void SupportedKeys_Contains_Success()
    {
        var factory = DecompressorFactory.Buffered();
        var keys = factory.GetSupportedKeys();
        Assert.That(keys, Does.Contain("gz"));
        Assert.That(keys, Does.Contain("gzip"));
        Assert.That(keys, Does.Contain("zz"));
        Assert.That(keys, Does.Contain("deflate"));
        Assert.That(keys, Does.Contain("zip"));
    }

    [Test]
    [TestCase("xyz")]
    [TestCase(".xyz")]
    [TestCase("XYZ")]
    public void AddAlias_Contains_Success(string alias)
    {
        var factory = DecompressorFactory.Buffered();
        Assert.That(factory.GetSupportedKeys(), Does.Not.Contain("xyz"));
        factory.AddAlias("zip", alias);
        Assert.That(factory.GetSupportedKeys(), Does.Contain("xyz"));
    }

    [Test]
    public void AddAlias_Unknown_ThrowsArgumentException()
    {
        var factory = DecompressorFactory.Buffered();
        Assert.That(factory.GetSupportedKeys(), Does.Not.Contain("xyz"));
        Assert.Throws<ArgumentException>(() => factory.AddAlias("xyz", "zip"));
    }

    [Test]
    public void Clear_Buffered_NoSupportedKeys()
    {
        var factory = DecompressorFactory.Buffered();
        factory.Clear();
        Assert.That(factory.GetSupportedKeys(), Is.Empty);
    }

    [Test]
    public void Clear_Streaming_NoSupportedKeys()
    {
        var factory = DecompressorFactory.Streaming();
        factory.Clear();
        Assert.That(factory.GetSupportedKeys(), Is.Empty);
    }

    [Test]
    public void GetDecompressor_Existing_Exists()
    {
        var factory = DecompressorFactory.Streaming();
        Assert.That(factory.GetDecompressor("gz"), Is.Not.Null);
    }

    [Test]
    public void GetDecompressor_Unknown_ThrowsArgumentOutOfRangeException()
    {
        var factory = DecompressorFactory.Streaming();
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => factory.GetDecompressor("xyz"));
        Assert.That(ex.Message, Does.Contain("xyz"));
        Assert.That(ex.ParamName, Is.EqualTo("key"));
    }
}
