using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing.Configuration;
public class CsvReaderBuilderTest
{
    [Test]
    public void WithDialectFunc_ShouldSetDialect()
    {
        var builder = new CsvReaderBuilder().WithDialect
        (
            (dialect) => dialect
                        .WithDelimiter(Delimiter.Tab)
                        .WithLineTerminator(LineTerminator.LineFeed)
        );
        var reader = builder.Build();
        Assert.That(reader.Profile.Dialect.Delimiter, Is.EqualTo('\t'));
        Assert.That(reader.Profile.Dialect.LineTerminator, Is.EqualTo("\n"));
    }

    [Test]
    public void WithDialect_ShouldSetDialect()
    {
        var builder = new CsvReaderBuilder().WithDialect
        (
            new DialectDescriptorBuilder()
                        .WithDelimiter(Delimiter.Tab)
                        .WithLineTerminator(LineTerminator.LineFeed)
        );
        var reader = builder.Build();
        Assert.That(reader.Profile.Dialect.Delimiter, Is.EqualTo('\t'));
        Assert.That(reader.Profile.Dialect.LineTerminator, Is.EqualTo("\n"));
    }

    [Test]
    public void WithSchemaFunc_ShouldSetSchema()
    {
        var builder = new CsvReaderBuilder().WithSchema
        (
            (schema) => schema
                        .Indexed()
                        .WithField<int>((f) => f.WithName("foo"))
                        .WithField(typeof(bool), (f) => f.WithName("bar"))
        );
        var reader = builder.Build();
        Assert.That(reader.Profile.Schema, Is.Not.Null);
        Assert.That(reader.Profile.Schema!.Fields, Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public void WithSchema_ShouldSetSchema()
    {
        var builder = new CsvReaderBuilder().WithSchema
        (
            new SchemaDescriptorBuilder()
                        .Indexed()
                        .WithField<int>((f) => f.WithName("foo"))
                        .WithField(typeof(bool), (f) => f.WithName("bar"))
        );
        var reader = builder.Build();
        Assert.That(reader.Profile.Schema, Is.Not.Null);
        Assert.That(reader.Profile.Schema!.Fields, Is.Not.Null.And.Not.Empty);
    }
}
