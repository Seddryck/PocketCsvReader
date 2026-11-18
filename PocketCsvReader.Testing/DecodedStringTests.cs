using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;
using PocketCsvReader.FieldParsing;

namespace PocketCsvReader.Testing;

public class DecodedStringTests
{
    [TestCase("\"abcde\"", "abcde")]
    [TestCase("\"a\"\"b\"", "a\"b")]
    public void GetDecodedString_UnescapesQuotedFields(string raw, string decoded)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(raw));
        using var reader = new CsvDataReader(stream, new CsvProfile(new(Header: false, DoubleQuote: true)));

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetRawString(0), Is.EqualTo(raw));
        Assert.That(reader.GetDecodedString(0), Is.EqualTo(decoded));
    }

    [TestCase("\"\"", "")]
    [TestCase("alias", "canonical")]
    public void GetDecodedString_AppliesSanitization(string raw, string decoded)
    {
        var resource = new ResourceDescriptorBuilder().WithSequence("alias", "canonical").Build();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(raw));
        using var reader = new CsvDataReader(stream, new CsvProfile(new(Header: false), resource: resource));

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetDecodedString(0), Is.EqualTo(decoded));
    }

    [Test]
    public void GetDecodedString_ThrowsForSanitizedNull()
    {
        var resource = new ResourceDescriptorBuilder().WithSequence("NULL", null).Build();
        using var stream = new MemoryStream("NULL"u8.ToArray());
        using var reader = new CsvDataReader(stream, new CsvProfile(new(Header: false), resource: resource));

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.IsDBNull(0), Is.True);
        Assert.Throws<InvalidCastException>(() => reader.GetDecodedString(0));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void GetDecodedString_SkipsConfiguredStringParsers(bool schemaParser)
    {
        var schema = new SchemaDescriptorBuilder().Indexed();
        var parsers = new RuntimeParsersDescriptorBuilder();
        int calls = 0;
        ParseSpan<string> parse = text =>
        {
            calls++;
            return text.ToString().ToUpperInvariant();
        };
        if (schemaParser)
        {
            schema.WithField<string>(f => f.WithParser(parse));
        }
        else
        {
            schema.WithField<string>();
            parsers.WithParser(parse);
        }

        var profile = new CsvProfile(new(Header: false), schema.Build(), parsers: parsers.Build());
        using var stream = new MemoryStream("\"abcde\""u8.ToArray());
        using var reader = new CsvDataReader(stream, profile);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetDecodedString(0), Is.EqualTo("abcde"));
        Assert.That(calls, Is.Zero);
        Assert.That(reader.GetFieldValue<string>(0), Is.EqualTo("ABCDE"));
        Assert.That(reader.GetDecodedString(0), Is.EqualTo("abcde"));
        Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    public void GetDecodedString_CanReadFieldAfterFailedConversion()
    {
        var schema = new SchemaDescriptorBuilder().Indexed().WithField<int>().Build();
        using var stream = new MemoryStream("\"12x\""u8.ToArray());
        using var reader = new CsvDataReader(stream, new CsvProfile(new(Header: false), schema));

        Assert.That(reader.Read(), Is.True);
        Assert.Throws<FormatException>(() => reader.GetInt32(0));
        Assert.That(reader.GetDecodedString(0), Is.EqualTo("12x"));
    }
}
