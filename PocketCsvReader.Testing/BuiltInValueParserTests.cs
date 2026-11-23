using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing;

public class BuiltInValueParserTests
{
    [Test]
    [SetCulture("fr-FR")]
    public void ReadsAndValidatesTimeSpanUsingInvariantCulture()
    {
        using var reader = ReadRow<TimeSpan>("ts", "01:30:00.5");

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.IsValid("ts"), Is.True);
        Assert.That(reader.GetFieldValue<TimeSpan>("ts"), Is.EqualTo(TimeSpan.FromSeconds(5400.5)));
        Assert.That(reader.GetValue("ts"), Is.EqualTo(TimeSpan.FromSeconds(5400.5)));
    }

    [Test]
    public void RejectsMalformedTimeSpan()
    {
        using var reader = ReadRow<TimeSpan>("ts", "invalid");

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.IsValid("ts"), Is.False);
        Assert.That(() => reader.GetFieldValue<TimeSpan>("ts"), Throws.TypeOf<FormatException>());
        Assert.That(() => reader.GetValue("ts"), Throws.TypeOf<FormatException>());
    }

    private static CsvDataReader ReadRow<T>(string name, string input)
    {
        var csv = new CsvReaderBuilder()
            .WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
            .WithSchema(s => s.Named().WithField<T>(name))
            .Build();
        return csv.ToDataReader(new MemoryStream(Encoding.UTF8.GetBytes($"{name}\n{input}\n")));
    }
}
