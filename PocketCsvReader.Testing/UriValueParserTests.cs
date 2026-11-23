using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing;

public class UriValueParserTests
{
    [TestCase("https://example.com/a,b", true)]
    [TestCase("docs/start", false)]
    public void DefaultsToAbsoluteUris(string input, bool valid)
    {
        using var reader = ReadRow(input, p => p.UseUriValues());

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.IsValid("url"), Is.EqualTo(valid));
        if (valid)
        {
            Assert.That(reader.GetFieldValue<Uri>("url").OriginalString, Is.EqualTo(input));
            Assert.That(reader.GetValue("url"), Is.EqualTo(new Uri(input)));
        }
        else
        {
            Assert.That(() => reader.GetFieldValue<Uri>("url"), Throws.TypeOf<UriFormatException>());
        }
    }

    [TestCase(UriKind.Relative, "docs/start", false)]
    [TestCase(UriKind.RelativeOrAbsolute, "docs/start", false)]
    [TestCase(UriKind.RelativeOrAbsolute, "https://example.com/", true)]
    public void HonorsConfiguredUriKind(UriKind kind, string input, bool absolute)
    {
        using var reader = ReadRow(input, p => p.UseUriValues(kind));

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.IsValid("url"), Is.True);
        Assert.That(reader.GetFieldValue<Uri>("url").IsAbsoluteUri, Is.EqualTo(absolute));
    }

    [Test]
    public void RejectsAnAbsoluteUriInRelativeMode()
    {
        using var reader = ReadRow("https://example.com/", p => p.UseUriValues(UriKind.Relative));

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.IsValid("url"), Is.False);
        Assert.That(() => reader.GetFieldValue<Uri>("url"), Throws.TypeOf<UriFormatException>());
    }

    [TestCase(true)]
    [TestCase(false)]
    public void ExplicitParsersOverrideUriDefaults(bool defaultsFirst)
    {
        var field = new Uri("https://field.example/");
        var runtime = new Uri("https://runtime.example/");
        var csv = new CsvReaderBuilder()
            .WithDialect(d => d.WithoutHeader().WithLineTerminator("\n"))
            .WithSchema(s => s.Indexed()
                .WithField<Uri>(f => f.WithParser((ReadOnlySpan<char> _) => field))
                .WithField<Uri>())
            .WithParsers(p => defaultsFirst
                ? p.UseUriValues().WithParser((ReadOnlySpan<char> _) => runtime)
                : p.WithParser((ReadOnlySpan<char> _) => runtime).UseUriValues())
            .Build();
        using var stream = new MemoryStream("field,runtime\n"u8.ToArray());
        using var reader = csv.ToDataReader(stream);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetFieldValue<Uri>(0), Is.SameAs(field));
        Assert.That(reader.GetFieldValue<Uri>(1), Is.SameAs(runtime));
    }

    private static CsvDataReader ReadRow(string input,
        Func<RuntimeParsersDescriptorBuilder, RuntimeParsersDescriptorBuilder> configure)
    {
        var csv = new CsvReaderBuilder()
            .WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
            .WithSchema(s => s.Named().WithField<Uri>("url"))
            .WithParsers(configure)
            .Build();
        return csv.ToDataReader(new MemoryStream(Encoding.UTF8.GetBytes($"url\n\"{input}\"\n")));
    }
}
