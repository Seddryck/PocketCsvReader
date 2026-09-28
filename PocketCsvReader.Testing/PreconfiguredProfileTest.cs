using System.Text;
using NUnit.Framework;

namespace PocketCsvReader.Testing;

public class PreconfiguredProfileTest
{
    [TestCaseSource(nameof(DoubleQuoteProfiles))]
    public void DoubleQuoteProfiles_ParseEmbeddedDelimiter(CsvProfile profile, char delimiter)
    {
        var content = $"a{delimiter}b\r\n\"x{delimiter}y\"{delimiter}z";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var rows = new CsvReader(profile).ToArrayString(stream).ToArray();
        Assert.That(rows[1], Is.EqualTo(new[] { $"x{delimiter}y", "z" }));
    }

    [Test]
    public void DefaultReader_ParsesConventionalDoubleQuotedCsv()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("a,b\r\n\"x,y\",z"));
        var rows = new CsvReader().ToArrayString(stream).ToArray();
        Assert.That(rows[1], Is.EqualTo(new[] { "x,y", "z" }));
    }

    [Test]
    public void PipeSingleQuote_RemainsSingleQuoted()
        => Assert.That(CsvProfile.PipeSingleQuote.Dialect.QuoteChar, Is.EqualTo('\''));

    private static IEnumerable<TestCaseData> DoubleQuoteProfiles()
    {
        yield return new TestCaseData(CsvProfile.CommaDoubleQuote, ',');
        yield return new TestCaseData(CsvProfile.SemiColumnDoubleQuote, ';');
        yield return new TestCaseData(CsvProfile.TabDoubleQuote, '\t');
    }
}
