using System.Data;
using System.Globalization;
using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing;

public class CsvComputedFieldTests
{
    [TestCase(CsvInputMode.DecodedChars)]
    [TestCase(CsvInputMode.Utf8Bytes)]
    public async Task ComputesSanitizedSpansFromProjectedAndHiddenColumns(CsvInputMode mode)
    {
        var calls = 0;
        var csv = Builder()
            .WithStreamInitialization(new(ProbeEncoding: false, InputMode: mode))
            .WithProjection("Quantity", "FirstName")
            .WithRowNumber()
            .WithFilter("LastName", "Lovelace")
            .WithSchema(schema => schema.Named()
                .WithField<string>("FirstName", field => field.WithSequence("alias", "Augusta"))
                .WithComputedField<string>("FullName", ["FirstName", "LastName"],
                    static fields => string.Concat(fields.GetSpan(3).Value, " ".AsSpan(), fields.GetSpan(0).Value))
                .WithComputedField<decimal>("Net", ["Quantity", "Price", "Discount", "Fee"], fields =>
                {
                    calls++;
                    Assert.That(fields.Count, Is.EqualTo(4));
                    var culture = CultureInfo.InvariantCulture;
                    var gross = int.Parse(fields[0], culture) * decimal.Parse(fields[1], culture);
                    var discount = decimal.Parse(fields[2], culture);
                    var fee = decimal.Parse(fields[3], culture);
                    return (gross * (1 - discount)) + fee;
                }))
            .Build();

        const string text = "LastName,Quantity,Price,FirstName,Discount,Fee\n"
            + "Hopper,1,1,Grace,0,0\n"
            + "Lovelace,2,10,\"Ad\"\"á\",0.25,1\n"
            + "Lovelace,3,10,alias,0,2\n";
        await using var reader = csv.ToDataReader(new MemoryStream(Encoding.UTF8.GetBytes(text)));

        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.Fields, Is.EqualTo(new[] { "rowNumber", "Quantity", "FirstName", "FullName", "Net" }));
        Assert.That(reader.GetInt32(0), Is.EqualTo(2));
        Assert.That(reader.GetFieldType(4), Is.EqualTo(typeof(decimal)));
        Assert.That(calls, Is.Zero);
        var name = reader.GetString(3);
        Assert.That(name, Is.EqualTo("Ad\"á Lovelace"));
        Assert.That(reader.GetDecimal(4), Is.EqualTo(16m));
        Assert.That(reader.GetFieldValue<decimal>("Net"), Is.EqualTo(16m));
        Assert.That(reader.GetValue(4), Is.EqualTo(16m));
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(reader.TryGetOrdinal("Price", out _), Is.False);

        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetString(3), Is.EqualTo("Augusta Lovelace"));
        Assert.That(reader.GetDecimal(4), Is.EqualTo(32m));
        Assert.That(calls, Is.EqualTo(2));
        Assert.That(name, Is.EqualTo("Ad\"á Lovelace"));
        Assert.That(await reader.ReadAsync(), Is.False);
    }

    [Test]
    public void DistinguishesNullFromEmptyAndKeepsOutputOrdinalForIncompleteRows()
    {
        var calls = 0;
        var csv = Builder()
            .WithResource(resource => resource.WithSequence("NULL", null))
            .WithSchema(schema => schema.Named()
                .WithComputedField<int?>("Length", ["Name", "Suffix"],
                    fields =>
                    {
                        calls++;
                        Assert.That(fields.GetSpan(0).HasValue, Is.True);
                        return fields[0].Length + fields[1].Length;
                    }))
            .Build();
        using var reader = csv.ToDataReader(new StringReader("Name,Suffix\nNULL,x\n\"\",x\nAda\n"));

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetOrdinal("Length"), Is.EqualTo(2));
        Assert.That(reader.IsSourceNull("Length"), Is.True);
        Assert.That(reader.IsDBNull(2), Is.True);
        Assert.That(reader.GetValue(2), Is.SameAs(DBNull.Value));
        Assert.That(reader.GetFieldValue<int?>(2), Is.Null);
        Assert.Throws<InvalidCastException>(() => reader.GetInt32(2));

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.IsSourceNull("Length"), Is.False);
        Assert.That(reader.IsDBNull(2), Is.False);
        Assert.That(reader.GetInt32(2), Is.EqualTo(1));

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.FieldCount, Is.EqualTo(3));
        Assert.That(reader.GetOrdinal("Length"), Is.EqualTo(2));
        Assert.That(reader.GetValue(2), Is.SameAs(DBNull.Value));
        Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    public void RejectsAmbiguousSourceNamesEvenOnHeaderOnlyInput()
    {
        var csv = Builder()
            .WithSchema(schema => schema.Named()
                .WithComputedField<int>("Output", ["A"], static fields => fields[0].Length))
            .Build();
        using var reader = csv.ToDataReader(new StringReader("A,A"));
        Assert.That(Assert.Throws<InvalidDataException>(() => reader.Read())!.Message, Does.Contain("is ambiguous"));
    }

    [TestCase("Output")]
    [TestCase("A")]
    public void RejectsDuplicateOutputNames(string duplicate)
    {
        var csv = Builder()
            .WithSchema(schema => schema.Named()
                .WithComputedField<int>("Output", ["A"], static fields => fields[0].Length)
                .WithComputedField<int>(duplicate, ["A"], static fields => fields[0].Length))
            .Build();
        using var reader = csv.ToDataReader(new StringReader("A\nvalue"));
        Assert.That(Assert.Throws<InvalidDataException>(() => reader.Read())!.Message, Does.Contain("conflicts"));
    }

    [Test]
    public void Utf8ReadsResetComputedValuesAndRejectExtraSourceFields()
    {
        var csv = Builder()
            .WithStreamInitialization(new(ProbeEncoding: false, InputMode: CsvInputMode.Utf8Bytes))
            .WithSchema(schema => schema.Named()
                .WithComputedField<int>("Length", ["Name"], static fields => fields[0].Length))
            .Build();
        var text = $"Name\nAda\nGrace\nextra,unexpected\n{new string('p', 64)}\n";
        using var reader = csv.ToDataReader(new MemoryStream(Encoding.UTF8.GetBytes(text)));

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(1), Is.EqualTo(3));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(1), Is.EqualTo(5));
        Assert.That(() => reader.Read(), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void PreservesSourceHeadersBetweenBatchStreams()
    {
        var csv = Builder()
            .WithDialect(dialect => dialect.WithoutHeaderRepeat())
            .WithSchema(schema => schema.Named()
                .WithComputedField<int>("Length", ["Name"], static fields => fields[0].Length))
            .Build();

        using var first = new MemoryStream("Name\nAda\n"u8.ToArray());
        using var second = new MemoryStream("Grace\n"u8.ToArray());
        using var reader = csv.ToDataReader(new Stream[] { first, second });
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(reader.GetOrdinal("Length")), Is.EqualTo(3));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(reader.GetOrdinal("Length")), Is.EqualTo(5));
        Assert.That(reader.Read(), Is.False);
    }

    [Test]
    public void IndexedSchemaSupportsTypedOverloadsAndSourceOrdinals()
    {
        var csv = Builder()
            .WithRowNumber()
            .WithSchema(schema => schema.Indexed()
                .WithField<DateOnly>(f => f.WithFormat("dd-MM-yyyy"))
                .WithField<int>(f => f.WithParser((ReadOnlySpan<char> text) => int.Parse(text) * 2))
                .WithComputedField<int, DateOnly, int>("Sum", 0, 1, (date, units) => date.Day + units)
                .WithComputedField<string, DateOnly, int, string>("Label", 0, 1, 2,
                    (date, units, label) => $"{date.Day}-{units}-{label}")
                .WithComputedField<int>("Length", [2, 1], fields =>
                {
                    var raw = fields.GetSpan(2);
                    Assert.That(raw.HasValue, Is.True);
                    Assert.That(raw.Value.ToString(), Is.EqualTo("item"));
                    Assert.That(fields[1].ToString(), Is.EqualTo("4"));
                    return raw.Value.Length + fields.GetFieldValue<int>(1);
                }))
            .Build();
        using var reader = csv.ToDataReader(new StringReader("Day,Units,Name\n07-01-2026,4,item"));

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.Fields, Is.EqualTo(new[] { "rowNumber", "Day", "Units", "Name", "Sum", "Label", "Length" }));
        Assert.That(reader.GetFieldValue<int>("Sum"), Is.EqualTo(15));
        Assert.That(reader.GetFieldValue<string>("Label"), Is.EqualTo("7-8-item"));
        Assert.That(reader.GetFieldValue<int>("Length"), Is.EqualTo(12));
    }

    [Test]
    public void RejectsAccessToUndeclaredOrdinals()
    {
        var csv = Builder()
            .WithSchema(schema => schema.Indexed()
                .WithComputedField<int>("Output", [1], static fields => fields.GetSpan(0).Value.Length))
            .Build();
        using var reader = csv.ToDataReader(new StringReader("A,B\na,b"));
        Assert.That(reader.Read(), Is.True);
        Assert.That(() => reader.GetValue("Output"), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void MissingSourceSkipsCallbackAndMalformedInputUsesNormalConversionErrors()
    {
        var csv = Builder()
            .WithSchema(schema => schema.Named()
                .WithComputedField<int>("Missing", ["Unknown"], _ => throw new AssertionException("Callback ran."))
                .WithComputedField<int, int, int>("Sum", "A", "B", (a, b) => a + b))
            .Build();
        using var reader = csv.ToDataReader(new StringReader("A,B\n1,2\n1,bad"));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetValue("Missing"), Is.SameAs(DBNull.Value));
        Assert.That(reader.GetFieldValue<int>("Sum"), Is.EqualTo(3));
        Assert.That(reader.Read(), Is.True);
        Assert.That(() => reader.GetFieldValue<int>("Sum"), Throws.TypeOf<FormatException>());
    }

    private static CsvReaderBuilder Builder() => new CsvReaderBuilder()
        .WithDialect(dialect => dialect.WithHeader().WithDelimiter(',').WithLineTerminator("\n").WithDoubleQuote(true));
}
