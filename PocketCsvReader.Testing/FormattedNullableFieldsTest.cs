using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing;

public class FormattedNullableFieldsTest
{
    private const string Content = "Count;Amount;Date\n1_234;12,5;22/10/2026\nNULL;NULL;NULL\n42;3,25;01/11/2026\n";

    private static CsvReader CreateReader() => new CsvReaderBuilder()
        .WithDialect(d => d.WithHeader().WithDelimiter(';').WithLineTerminator("\n").WithNullSequence("NULL"))
        .WithSchema(s => s.Named()
            .WithIntegerField<int?>("Count", f => f.WithFormat(fmt => fmt.WithGroupChar('_')))
            .WithNumberField<decimal?>("Amount", f => f.WithFormat(fmt => fmt.WithDecimalChar(',')))
            .WithTemporalField<DateTime?>("Date", f => f.WithFormat("dd/MM/yyyy")))
        .Build();

    private static MemoryStream Source(string content = Content) => new(Encoding.UTF8.GetBytes(content));

    [TestCase(false)]
    [TestCase(true)]
    public void ParsedRows_PreserveFormattedNullableValuesAndNulls(bool dictionaries)
    {
        using var stream = Source();
        var reader = CreateReader();
        var rows = dictionaries
            ? reader.ToDictionaries(stream).Select(r => new[] { r["Count"], r["Amount"], r["Date"] }).ToArray()
            : reader.ToObjectArrays(stream).ToArray();
        Assert.That(rows, Is.EqualTo(new[]
        {
            new object?[] { 1234, 12.5m, new DateTime(2026, 10, 22) },
            new object?[] { null, null, null },
            new object?[] { 42, 3.25m, new DateTime(2026, 11, 1) }
        }));
        Assert.That(rows[0][0], Is.TypeOf<int>());
        Assert.That(rows[0][1], Is.TypeOf<decimal>());
        Assert.That(rows[0][2], Is.TypeOf<DateTime>());
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DataReader_TypedAndBoxedAccessPreservesNullableParserType(bool boxedFirst)
    {
        using var stream = Source();
        using var reader = CreateReader().ToDataReader(stream);
        Assert.That(reader.Read(), Is.True);
        if (boxedFirst) Assert.That(reader.GetValue(0), Is.EqualTo(1234));
        Assert.That(reader.GetFieldValue<int?>(0), Is.EqualTo(1234));
        Assert.That(reader.GetFieldValue<decimal?>(1), Is.EqualTo(12.5m));
        Assert.That(reader.GetFieldValue<DateTime?>(2), Is.EqualTo(new DateTime(2026, 10, 22)));
        Assert.That(reader.GetValue(0), Is.EqualTo(1234));
        // Switching to a non-nullable typed accessor must not corrupt subsequent nullable reads.
        Assert.That(reader.GetInt32(0), Is.EqualTo(1234));
        Assert.That(reader.GetFieldValue<int?>(0), Is.EqualTo(1234));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetFieldValue<int?>(0), Is.Null);
        Assert.That(reader.GetFieldValue<decimal?>(1), Is.Null);
        Assert.That(reader.GetFieldValue<DateTime?>(2), Is.Null);
    }

    [Test]
    public void InvalidFormattedNullableValue_PropagatesFormatException()
    {
        using var stream = Source("Count;Amount;Date\ninvalid;1,5;22/10/2026\n");
        Assert.Throws<FormatException>(() => CreateReader().ToObjectArrays(stream).ToArray());
    }
}
