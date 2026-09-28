using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing;

public class LineTerminatorMismatchTest
{
    [TestCase(3)]
    [TestCase(4096)]
    public void Mismatch_ReprocessesDelimiter(int bufferSize)
    {
        var dialect = new DialectDescriptorBuilder()
            .WithDelimiter(',')
            .WithLineTerminator("\r\n")
            .WithoutHeader()
            .Build();
        var profile = new CsvProfile(dialect)
        {
            ParserOptimizations = new ParserOptimizationOptions { BufferSize = bufferSize }
        };
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("ab\r,cd\r\nef"));

        var rows = new CsvReader(profile).ToArrayString(stream).ToArray();

        Assert.That(rows, Is.EqualTo(new[] { new[] { "ab\r", "cd" }, new[] { "ef" } }));
    }

    [Test]
    public void PartialTerminatorAtEof_IsPreservedAsData()
    {
        var dialect = new DialectDescriptorBuilder().WithLineTerminator("\r\n").WithoutHeader().Build();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("ab\r"));
        var rows = new CsvReader(new CsvProfile(dialect)).ToArrayString(stream).ToArray();
        Assert.That(rows.Single(), Is.EqualTo(new[] { "ab\r" }));
    }
}
