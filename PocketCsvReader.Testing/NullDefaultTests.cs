using System.Globalization;
using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing;

public class NullDefaultTests
{
    private const string TextOutputInput = "Amount,Note\nN/A,N/A\n12.5,\n";

    [Test]
    public void DecimalDefault_IsUsedOnlyForRecognizedNulls()
    {
        var csv = new CsvReaderBuilder()
            .WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
            .WithSchema(s => s.Named()
                .WithField<decimal>("Amount", f => f.WithSequence("N/A", null).WithNullDefault(0m)))
            .Build();

        using var stream = Source("Amount\nN/A\n12.5\nabc\n");
        using var reader = csv.ToDataReader(stream);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.IsSourceNull(0), Is.True);
        Assert.That(reader.IsSourceNull("Amount"), Is.True);
        Assert.That(reader.IsDBNull("Amount"), Is.False);
        Assert.That(reader.IsValid("Amount"), Is.True);
        Assert.That(reader.GetFieldValue<decimal>("Amount"), Is.EqualTo(0m));
        Assert.That(reader.GetDecimal(0), Is.EqualTo(0m));
        Assert.That(reader.GetFieldValue<decimal>(0, CultureInfo.InvariantCulture), Is.EqualTo(0m));
        Assert.That(reader.GetValue(0), Is.TypeOf<decimal>().And.EqualTo(0m));
        Assert.That(reader.GetRawString(0), Is.EqualTo("N/A"));
        Assert.Throws<InvalidCastException>(() => reader.GetDecodedString(0));

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.IsSourceNull(0), Is.False);
        Assert.That(reader.GetFieldValue<decimal>(0), Is.EqualTo(12.5m));
        Assert.That(reader.GetValue(0), Is.EqualTo(12.5m));

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.IsSourceNull(0), Is.False);
        Assert.That(reader.IsValid(0), Is.False);
        Assert.Throws<FormatException>(() => reader.GetFieldValue<decimal>(0));
        Assert.Throws<FormatException>(() => reader.GetValue(0));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NullableNumberDefault_IsPreservedInTypedReadsAndParsedRows(bool runtimeType)
    {
        var csv = new CsvReaderBuilder()
            .WithDialect(d => d.WithHeader().WithDelimiter(';').WithLineTerminator("\n").WithNullSequence("NULL"))
            .WithSchema(s => runtimeType
                ? s.Named().WithNumberField(typeof(decimal?), "Amount", f => f.WithNullDefault(1.25m).WithFormat(fmt => fmt.WithDecimalChar(',')))
                : s.Named().WithNumberField<decimal?>("Amount", f => f.WithNullDefault(1.25m).WithFormat(fmt => fmt.WithDecimalChar(','))))
            .Build();

        using var typedStream = Source("Amount\nNULL\n");
        using var reader = csv.ToDataReader(typedStream);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetFieldValue<decimal?>(0), Is.EqualTo(1.25m));

        using var parsedStream = Source("Amount\nNULL\n12,5\n");
        Assert.That(csv.ToDictionaries(parsedStream).Select(row => row["Amount"]).ToArray(),
            Is.EqualTo(new object[] { 1.25m, 12.5m }));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void EmptyCell_UsesDefaultOnlyWhenConfiguredAsNull(bool recognizeEmpty)
    {
        var csv = new CsvReaderBuilder()
            .WithDialect(d => d.WithHeader().WithDelimiter(';').WithLineTerminator("\n"))
            .WithSchema(s => s.Named().WithField<int>("Id").WithField<string>("Name", f =>
                (recognizeEmpty ? f.WithSequence(string.Empty, null) : f).WithNullDefault("unknown")))
            .Build();

        using var stream = Source("Id;Name\n1;\n2\n");
        using var reader = csv.ToDataReader(stream);
        Assert.That(reader.Read(), Is.True);
        var expected = recognizeEmpty ? "unknown" : string.Empty;
        Assert.That(reader.IsSourceNull("Name"), Is.EqualTo(recognizeEmpty));
        Assert.That(reader.GetFieldValue<string>(1), Is.EqualTo(expected));
        Assert.That(reader.GetString(1), Is.EqualTo(expected));
        Assert.That(reader.GetValue(1), Is.EqualTo(expected));

        Assert.That(reader.Read(), Is.True);
        Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetValue(1));
    }

    [Test]
    public void IncompatibleDefault_IsRejectedWhenBuildingSchema()
    {
        Assert.Throws<ArgumentException>(() => new CsvReaderBuilder()
            .WithSchema(s => s.Indexed().WithNumberField(typeof(decimal?), f => f.WithNullDefault("0")))
            .Build());
    }

    [Test]
    public void ToArrayString_PreservesSourceNullsAndEmptyText()
    {
        using var stream = Source(TextOutputInput);
        var rows = CreateTextOutputReader().ToArrayString(stream).ToArray();

        Assert.That(rows, Is.EqualTo(new[]
        {
            new string?[] { null, null },
            new string?[] { "12.5", string.Empty }
        }));
    }

    [Test]
    public void BatchDataReader_ReportsSourceNullBeforeApplyingDefault()
    {
        using var stream = Source(TextOutputInput);
        using var reader = CreateTextOutputReader().ToDataReader([stream]);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.IsSourceNull("Amount"), Is.True);
        Assert.That(reader.IsDBNull(0), Is.False);
        Assert.That(reader.GetDecimal(0), Is.EqualTo(0m));

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.IsSourceNull(0), Is.False);
    }

    [Test]
    public void ToDataTable_TextModePreservesSourceNullsAndSchemaModeUsesDefaults()
    {
        var csv = CreateTextOutputReader();
        using var textStream = Source(TextOutputInput);
        using var textTable = csv.ToDataTable(textStream, DataTableColumnTypes.Text);
        Assert.That(textTable.Rows[0].ItemArray, Is.EqualTo(new object[] { DBNull.Value, DBNull.Value }));
        Assert.That(textTable.Rows[1].ItemArray, Is.EqualTo(new object[] { "12.5", string.Empty }));

        using var schemaStream = Source(TextOutputInput);
        using var schemaTable = csv.ToDataTable(schemaStream, DataTableColumnTypes.Schema);
        Assert.That(schemaTable.Rows[0].ItemArray, Is.EqualTo(new object[] { 0m, "unknown" }));
        Assert.That(schemaTable.Rows[1].ItemArray, Is.EqualTo(new object[] { 12.5m, string.Empty }));
    }

    private static CsvReader CreateTextOutputReader()
        => new CsvReaderBuilder()
            .WithDialect(d => d.WithHeader().WithLineTerminator("\n").WithNullSequence("N/A"))
            .WithSchema(s => s.Named()
                .WithField<decimal>("Amount", f => f.WithNullDefault(0m))
                .WithField<string>("Note", f => f.WithNullDefault("unknown")))
            .Build();

    private static MemoryStream Source(string content) => new(Encoding.UTF8.GetBytes(content));
}
