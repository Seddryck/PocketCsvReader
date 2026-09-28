using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing;

public class UnquotedFieldParserTest
{
    [Test]
    public void QuoteCharacters_AreTreatedAsData()
    {
        var rows = Read("a,\"b\"\r\nc,d", new DialectDescriptorBuilder().WithoutQuoteChar());

        Assert.That(rows, Is.EqualTo(new[] { new[] { "a", "\"b\"" }, new[] { "c", "d" } }));
    }

    [Test]
    public void EscapedDelimiter_IsPreservedInTheValue()
    {
        var rows = Read("a,b\\,c", new DialectDescriptorBuilder()
            .WithoutQuoteChar()
            .WithEscapeChar('\\'));

        Assert.That(rows.Single(), Is.EqualTo(new[] { "a", "b,c" }));
    }

    [Test]
    public void SingleCharacterLineTerminator_IsSupported()
    {
        var rows = Read("a,b\nc,d", new DialectDescriptorBuilder()
            .WithoutQuoteChar()
            .WithLineTerminator("\n"));

        Assert.That(rows, Is.EqualTo(new[] { new[] { "a", "b" }, new[] { "c", "d" } }));
    }

    [TestCase(3)]
    [TestCase(4096)]
    public void TerminatorMismatch_IsReprocessed(int bufferSize)
    {
        var rows = Read("ab\r,cd\r\nef", new DialectDescriptorBuilder().WithoutQuoteChar(), bufferSize);

        Assert.That(rows, Is.EqualTo(new[] { new[] { "ab\r", "cd" }, new[] { "ef" } }));
    }

    [Test]
    public void CommentMarker_IsSpecialOnlyAtTheStartOfARecord()
    {
        var rows = Read("#ignored\r\na,#data\r\nb,c", new DialectDescriptorBuilder()
            .WithoutQuoteChar()
            .WithCommentChar('#'));

        Assert.That(rows, Is.EqualTo(new[] { new[] { "a", "#data" }, new[] { "b", "c" } }));
    }

    private static string?[][] Read(string csv, DialectDescriptorBuilder builder, int bufferSize = 4096)
    {
        var dialect = builder.WithoutHeader().Build();
        var profile = new CsvProfile(dialect)
        {
            ParserOptimizations = new ParserOptimizationOptions { BufferSize = bufferSize }
        };
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        return new CsvReader(profile).ToArrayString(stream).ToArray();
    }
}
