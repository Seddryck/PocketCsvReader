using System.Text;
using NUnit.Framework;

namespace PocketCsvReader.Testing;

public class CsvReaderBufferSizeTest
{
    [TestCase(0)]
    [TestCase(-1)]
    public void Constructor_NonPositiveBuffer_Throws(int bufferSize)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new CsvReader(bufferSize));

    [Test]
    public void BufferConstructor_KeepsCommaDialectAcrossBoundaries()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("a,b\r\nc,d"));
        var rows = new CsvReader(1).ToArrayString(stream).ToArray();
        Assert.That(rows, Is.EqualTo(new[] { new[] { "a", "b" }, new[] { "c", "d" } }));
    }

    [Test]
    public void ProfileBufferConstructor_HandlesQuotesAndCrlfAcrossBoundaries()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("a,b\r\n\"x,y\",z"));
        var rows = new CsvReader(new CsvProfile(',', '"', "\r\n", false), 1).ToArrayString(stream).ToArray();
        Assert.That(rows, Is.EqualTo(new[] { new[] { "a", "b" }, new[] { "x,y", "z" } }));
    }
}
