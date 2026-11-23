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

    [TestCase("1.2", 1, 2, -1, -1)]
    [TestCase("1.2.3", 1, 2, 3, -1)]
    [TestCase("1.2.3.4", 1, 2, 3, 4)]
    public void ReadsAndValidatesVersion(string input, int major, int minor, int build, int revision)
    {
        using var reader = ReadRow<Version>("version", input);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.IsValid("version"), Is.True);
        var version = reader.GetFieldValue<Version>("version");
        Assert.That((version.Major, version.Minor, version.Build, version.Revision),
            Is.EqualTo((major, minor, build, revision)));
        Assert.That(reader.GetValue("version"), Is.EqualTo(version));
    }

    [TestCase("1", typeof(ArgumentException))]
    [TestCase("1.2.3-beta", typeof(FormatException))]
    public void RejectsMalformedVersion(string input, Type exception)
    {
        using var reader = ReadRow<Version>("version", input);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.IsValid("version"), Is.False);
        Assert.That(() => reader.GetFieldValue<Version>("version"), Throws.TypeOf(exception));
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
