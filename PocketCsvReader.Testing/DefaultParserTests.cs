using System.Globalization;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing;

public class DefaultParserTests
{
    [TestCase(true)]
    [TestCase(false)]
    public void ExplicitParser_OverridesDefaultRegardlessOfOrder(bool defaultsFirst)
    {
        var builder = new RuntimeParsersDescriptorBuilder();
        if (defaultsFirst) builder.WithDefaultParser<int>(_ => -1);
        builder.WithParser((ReadOnlySpan<char> _) => 42);
        if (!defaultsFirst) builder.WithDefaultParser<int>(_ => -1);
        var descriptor = builder.Build()!;
        Assert.That(descriptor.Count, Is.EqualTo(1));
        Assert.That(descriptor.Single().Value("ignored"), Is.EqualTo(42));
    }

    [Test]
    public void Build_SnapshotsDefaultsAndExplicitRegistrations()
    {
        var builder = new RuntimeParsersDescriptorBuilder().WithDefaultParser<int>(_ => 1);
        var first = builder.Build()!;
        builder.WithParser((ReadOnlySpan<char> _) => 2);
        var second = builder.Build()!;
        Assert.That(first.Single().Value(""), Is.EqualTo(1));
        Assert.That(second.Single().Value(""), Is.EqualTo(2));
    }

    [Test]
    public void FormatFactory_ReceivesSchemaPatternAndProvider()
    {
        string? actualPattern = null;
        IFormatProvider? actualCulture = null;
        var culture = CultureInfo.GetCultureInfo("fr-FR");
        var parsers = new RuntimeParsersDescriptorBuilder().WithDefaultParser<int>(_ => -1,
            (pattern, provider) =>
            {
                actualPattern = pattern;
                actualCulture = provider;
                return _ => 42;
            });
        var schema = new SchemaDescriptorBuilder().Indexed()
            .WithCustomField(typeof(int), f => f.WithFormat("custom", culture)).Build();
        var profile = new CsvProfile(new DialectDescriptorBuilder().WithoutHeader().Build(), schema, null, parsers.Build());
        using var reader = new CsvDataReader(new MemoryStream("ignored"u8.ToArray()), profile);
        reader.Read();
        Assert.That(reader.GetFieldValue<int>(0), Is.EqualTo(42));
        Assert.That(reader.GetValue(0), Is.EqualTo(42));
        Assert.That(actualPattern, Is.EqualTo("custom"));
        Assert.That(actualCulture, Is.SameAs(culture));
    }

    [Test]
    public void NullDefault_IsRejected()
        => Assert.That(() => new RuntimeParsersDescriptorBuilder().WithDefaultParser<int>(null!),
            Throws.ArgumentNullException);
}
