using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing;

public class StringParserGetterTests
{
    [Test]
    public void GetString_UsesFieldAndRuntimeStringParsersInGetFieldValuePrecedence()
    {
        var reader = new CsvReaderBuilder()
            .WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
            .WithSchema(s => s.Named()
                .WithField<string>("Code", f => f.WithParser((ReadOnlySpan<char> text) => $"field:{text}"))
                .WithField<string>("Name"))
            .WithParsers(p => p.WithParser<string>((string text) => $"runtime:{text}"))
            .Build();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Code,Name\nabc,Ada\n"));
        using var data = reader.ToDataReader(stream);

        Assert.That(data.Read(), Is.True);
        Assert.That(data.GetString(0), Is.EqualTo("field:abc"));
        Assert.That(data.GetString(1), Is.EqualTo("runtime:Ada"));
        Assert.That(data.GetFieldValue<string>(0), Is.EqualTo("field:abc"));
        Assert.That(data.GetFieldValue<string>(1), Is.EqualTo("runtime:Ada"));
    }

    [Test]
    public void GetString_WithoutStringParserReturnsDecodedTextAndMaterializersUseConfiguredParser()
    {
        var plainReader = new CsvReaderBuilder()
            .WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
            .Build();
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes("Name\nAda\n")))
        using (var data = plainReader.ToDataReader(stream))
        {
            Assert.That(data.Read(), Is.True);
            Assert.That(data.GetString(0), Is.EqualTo("Ada"));
        }

        var parsedReader = new CsvReaderBuilder()
            .WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
            .WithParsers(p => p.WithParser<string>((string text) => text.ToUpperInvariant()))
            .Build();
        using var arrayStream = new MemoryStream(Encoding.UTF8.GetBytes("Name\nAda\n"));
        Assert.That(parsedReader.ToArrayString(arrayStream).Single(), Is.EqualTo(new[] { "ADA" }));
        using var tableStream = new MemoryStream(Encoding.UTF8.GetBytes("Name\nAda\n"));
        Assert.That(parsedReader.ToDataTable(tableStream).Rows[0]["Name"], Is.EqualTo("ADA"));
    }
}
