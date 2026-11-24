using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing;

public class Utf8ParserRegistrationTests
{
    [Test]
    public void CustomParserUsesBytesAndFallsBackForSkippedOrRejectedInput()
    {
        int utf8Calls = 0;
        int fallbackCalls = 0;
        var csv = new CsvReaderBuilder()
            .WithDialect(d => d.WithoutHeader())
            .WithStreamInitialization(StreamInitializationOptions.Utf8)
            .WithParsers(p => p.WithUtf8Parser<string>(
                (ReadOnlySpan<byte> text, out string value) =>
                {
                    utf8Calls++;
                    value = "native";
                    return text.SequenceEqual("native"u8);
                },
                text =>
                {
                    fallbackCalls++;
                    return text.ToString();
                }))
            .Build();

        using var stream = new MemoryStream("native,fallback,\"escaped\"\"quote\",\r\n"u8.ToArray());
        using var reader = csv.ToDataReader(stream);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetFieldValue<string>(0), Is.EqualTo("native"));
        Assert.That(reader.GetString(1), Is.EqualTo("fallback"));
        Assert.That(reader.GetString(2), Is.EqualTo("escaped\"quote"));
        Assert.That(reader.GetFieldValue<string>(3), Is.Empty);
        Assert.That(utf8Calls, Is.EqualTo(3));
        Assert.That(fallbackCalls, Is.EqualTo(3));

        using var textReader = csv.ToDataReader(new StringReader("native"));
        Assert.That(textReader.Read(), Is.True);
        Assert.That(textReader.GetString(0), Is.EqualTo("native"));
        Assert.That(utf8Calls, Is.EqualTo(3));
        Assert.That(fallbackCalls, Is.EqualTo(4));
    }

    [Test]
    public void AFieldParserAndAnExplicitGetterTakePrecedence()
    {
        var csv = new CsvReaderBuilder()
            .WithDialect(d => d.WithoutHeader())
            .WithStreamInitialization(StreamInitializationOptions.Utf8)
            .WithSchema(s => s.Indexed()
                .WithField<TimeOnly>(f => f.WithParser<TimeOnly>((ReadOnlySpan<char> _) => new TimeOnly(1, 0)))
                .WithField<Guid>())
            .WithParsers(p => p
                .WithUtf8Parser<TimeOnly>(
                    (ReadOnlySpan<byte> _, out TimeOnly value) => throw new InvalidOperationException("Field parser should win."),
                    text => TimeOnly.Parse(text, System.Globalization.CultureInfo.InvariantCulture))
                .WithUtf8Parser<Guid>(
                    (ReadOnlySpan<byte> _, out Guid value) =>
                    {
                        value = Guid.Empty;
                        return true;
                    },
                    _ => throw new InvalidOperationException("The unformatted field should use bytes.")))
            .Build();

        using var reader = csv.ToDataReader(new MemoryStream("12:30:00,id"u8.ToArray()));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetFieldValue(0, (ReadOnlySpan<char> _) => new TimeOnly(2, 0)), Is.EqualTo(new TimeOnly(2, 0)));
        Assert.That(reader.GetTime(0), Is.EqualTo(new TimeOnly(1, 0)));
        Assert.That(reader.GetGuid(1), Is.EqualTo(Guid.Empty));
    }

    [Test]
    public void CustomParserKeepsNullAndInvalidUtf8OnTheCharacterPath()
    {
        var csv = new CsvReaderBuilder()
            .WithDialect(d => d.WithoutHeader())
            .WithStreamInitialization(StreamInitializationOptions.Utf8)
            .WithParsers(p => p.WithUtf8Parser<string>(
                (ReadOnlySpan<byte> _, out string value) => throw new InvalidOperationException("Invalid UTF-8 should use the fallback."),
                text => text.ToString()))
            .Build();

        using var reader = csv.ToDataReader(new MemoryStream(new byte[] { 0xff }));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo(Encoding.UTF8.GetString(new byte[] { 0xff })));

        csv = new CsvReaderBuilder()
            .WithDialect(d => d.WithoutHeader())
            .WithResource(r => r.WithSequence("N/A", null))
            .WithStreamInitialization(StreamInitializationOptions.Utf8)
            .WithParsers(p => p.WithUtf8Parser<string>(
                (ReadOnlySpan<byte> _, out string value) => throw new InvalidOperationException("Null fields should skip parsing."),
                _ => throw new InvalidOperationException("Null fields should skip parsing.")))
            .Build();

        using var nullReader = csv.ToDataReader(new MemoryStream("N/A"u8.ToArray()));
        Assert.That(nullReader.Read(), Is.True);
        Assert.That(nullReader.GetFieldValue<string>(0), Is.Null);
    }
}
