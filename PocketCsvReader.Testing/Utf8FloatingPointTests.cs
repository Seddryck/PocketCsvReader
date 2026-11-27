using System.Globalization;
using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing;

public class Utf8FloatingPointTests
{
    [TestCase("12.375")]
    [TestCase("+4.25E-3")]
    [TestCase("-2.5e+3")]
    [TestCase(" \t-1,234.5\t ")]
    [TestCase("1e-45")]
    [TestCase("5e-324")]
    [TestCase("16777217")]
    [TestCase("9007199254740993")]
    [TestCase("3.4028234663852886e38")]
    [TestCase("1.7976931348623157e308")]
    [TestCase("1e39")]
    [TestCase("-1e309")]
    [TestCase("-1e-400")]
    [TestCase("-0")]
    [TestCase(" NaN ")]
    [TestCase("\u00A0NaN\u00A0")]
    [TestCase("+Infinity")]
    [TestCase("-infinity")]
    public void NativeAndDecodedGettersReturnTheSameBits(string value)
    {
        var culture = CultureInfo.InvariantCulture;
        int singleBits = BitConverter.SingleToInt32Bits(float.Parse(value.AsSpan(), culture));
        long doubleBits = BitConverter.DoubleToInt64Bits(double.Parse(value.AsSpan(), culture));
        // Exercise bare and quoted fields. Grouped values require quotes with the CSV delimiter.
        string quoted = $"\"{value}\"";
        string csv = value.Contains(',') ? $"{quoted},{quoted}" : $"{value},{quoted}";

        foreach (var mode in new[] { CsvInputMode.DecodedChars, CsvInputMode.Utf8Bytes })
        {
            using var reader = Open(CreateBuilder(mode), csv);
            Assert.That(reader.FieldCount, Is.EqualTo(2));
            for (int i = 0; i < reader.FieldCount; i++)
            {
                Assert.That(BitConverter.SingleToInt32Bits(reader.GetFloat(i)), Is.EqualTo(singleBits), $"{mode}, column {i}");
                Assert.That(BitConverter.SingleToInt32Bits(reader.GetFieldValue<float>(i)), Is.EqualTo(singleBits), $"{mode}, column {i}");
                Assert.That(BitConverter.DoubleToInt64Bits(reader.GetDouble(i)), Is.EqualTo(doubleBits), $"{mode}, column {i}");
                Assert.That(BitConverter.DoubleToInt64Bits(reader.GetFieldValue<double>(i)), Is.EqualTo(doubleBits), $"{mode}, column {i}");
            }
        }
    }

    [TestCase(CsvInputMode.DecodedChars)]
    [TestCase(CsvInputMode.Utf8Bytes)]
    public void MalformedFieldsKeepTheirFormatExceptions(CsvInputMode mode)
    {
        using var reader = Open(CreateBuilder(mode), "1.2x,1e+,1.5-,\"1\"\"2\",");
        Assert.That(reader.FieldCount, Is.EqualTo(5));
        for (int i = 0; i < reader.FieldCount; i++)
        {
            Assert.That(() => reader.GetFloat(i), Throws.TypeOf<FormatException>());
            Assert.That(() => reader.GetDouble(i), Throws.TypeOf<FormatException>());
            Assert.That(() => reader.GetFieldValue<float>(i), Throws.TypeOf<FormatException>());
            Assert.That(() => reader.GetFieldValue<double>(i), Throws.TypeOf<FormatException>());
        }
    }

    [Test]
    public void ConfiguredFormatsAndCultureTakePrecedence()
    {
        var builder = CreateBuilder(CsvInputMode.Utf8Bytes)
            .WithSchema(s => s.Indexed()
                .WithNumberField<float>(f => f.WithFormat(format => format.WithDecimalChar(',').WithGroupChar('.')))
                .WithNumberField<double>(f => f.WithFormat(format => format.WithDecimalChar(',').WithGroupChar('.'))));

        using var reader = Open(builder, "\"1.234,5\",\"2.345,75\"");
        Assert.That(reader.GetFloat(0), Is.EqualTo(1234.5f));
        Assert.That(reader.GetDouble(1), Is.EqualTo(2345.75));
        Assert.That(reader.GetFieldValue<float>(0), Is.EqualTo(1234.5f));
        Assert.That(reader.GetFieldValue<double>(1), Is.EqualTo(2345.75));
        var culture = CultureInfo.GetCultureInfo("de-DE");
        Assert.That(reader.GetFieldValue<float>(0, culture), Is.EqualTo(1234.5f));
        Assert.That(reader.GetFieldValue<double>(1, culture), Is.EqualTo(2345.75));
    }

    [Test]
    public void ConfiguredCharacterParsersTakePrecedence()
    {
        var builder = CreateBuilder(CsvInputMode.Utf8Bytes)
            .WithParsers(p => p
                .WithParser<float>(static (ReadOnlySpan<char> _) => 42f)
                .WithParser<double>(static (ReadOnlySpan<char> _) => 43d));

        using var reader = Open(builder, "1.25,2.5");
        Assert.That(reader.GetFloat(0), Is.EqualTo(42f));
        Assert.That(reader.GetDouble(1), Is.EqualTo(43d));
        Assert.That(reader.GetFieldValue<float>(0), Is.EqualTo(42f));
        Assert.That(reader.GetFieldValue<double>(1), Is.EqualTo(43d));
    }

    [TestCase(CsvInputMode.DecodedChars)]
    [TestCase(CsvInputMode.Utf8Bytes)]
    public void SequencesPreserveNullsAndReplacements(CsvInputMode mode)
    {
        var builder = CreateBuilder(mode)
            .WithResource(r => r.WithSequence("1.25", null).WithSequence("2.5", "3.5"));

        using var reader = Open(builder, "1.25,2.5");
        Assert.That(reader.IsDBNull(0), Is.True);
        Assert.That(reader.GetFieldValue<float?>(0), Is.Null);
        Assert.That(reader.GetFieldValue<double?>(0), Is.Null);
        Assert.That(() => reader.GetFloat(0), Throws.TypeOf<InvalidCastException>());
        Assert.That(() => reader.GetDouble(0), Throws.TypeOf<InvalidCastException>());
        Assert.That(() => reader.GetFieldValue<float>(0), Throws.TypeOf<InvalidCastException>());
        Assert.That(() => reader.GetFieldValue<double>(0), Throws.TypeOf<InvalidCastException>());
        Assert.That(reader.GetFloat(1), Is.EqualTo(3.5f));
        Assert.That(reader.GetDouble(1), Is.EqualTo(3.5d));
        Assert.That(reader.GetFieldValue<float>(1), Is.EqualTo(3.5f));
        Assert.That(reader.GetFieldValue<double>(1), Is.EqualTo(3.5d));
    }

    private static CsvReaderBuilder CreateBuilder(CsvInputMode mode)
        => new CsvReaderBuilder()
            .WithDialect(d => d.WithoutHeader())
            .WithStreamInitialization(new StreamInitializationOptions(InputMode: mode));

    private static CsvDataReader Open(CsvReaderBuilder builder, string csv)
    {
        var reader = builder.Build().ToDataReader(new MemoryStream(Encoding.UTF8.GetBytes(csv)));
        Assert.That(reader.Read(), Is.True);
        return reader;
    }
}
