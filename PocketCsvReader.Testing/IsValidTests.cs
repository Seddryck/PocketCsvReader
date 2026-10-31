using System.Collections;
using System.Data;
using System.Globalization;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;
using PocketCsvReader.FieldParsing;

namespace PocketCsvReader.Testing;

public class IsValidTests
{
    private static CsvProfile Profile(FieldDescriptor? field = null, RuntimeParsersDescriptor? parsers = null,
        ResourceDescriptor? resource = null)
    {
        SchemaDescriptor? schema = null;
        if (field is not null)
        {
            schema = new SchemaDescriptor.IndexedSchemaDescriptor();
            schema.Fields.Add(field);
        }
        return new(new DialectDescriptorBuilder().WithDelimiter(';').WithHeader().WithLineTerminator("\n")
            .WithArray(a => a.WithDelimiter('|').WithPrefix('[').WithSuffix(']')).Build(), schema, resource, parsers);
    }

    private static MemoryStream Input(string text) => new(Encoding.UTF8.GetBytes(text));

    private static CsvDataReader Reader(string text, FieldDescriptor? field = null,
        RuntimeParsersDescriptor? parsers = null, ResourceDescriptor? resource = null, bool read = true)
    {
        var reader = new CsvDataReader(Input($"value;other\n{text};tail\n"), Profile(field, parsers, resource));
        if (read) Assert.That(reader.Read(), Is.True);
        return reader;
    }

    private static bool Validate(CsvDataReader reader, bool named) => named ? reader.IsValid("value") : reader.IsValid(0);

    [TestCase("2026-02-28", true)]
    [TestCase("2026-02-31", false)]
    [TestCase("02/28/2026", false)]
    public void DateColumn_BothOverloadsUseDeclaredFormat(string text, bool expected)
    {
        using var reader = Reader(text, new(typeof(DateTime), Format: new TemporalFormatDescriptor("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        Assert.That(reader.GetFieldType(0), Is.EqualTo(typeof(DateTime)));
        Assert.That(reader.IsValid(0), Is.EqualTo(expected));
        Assert.That(reader.IsValid("value"), Is.EqualTo(expected));
        Assert.That(reader.GetString(0), Is.EqualTo(text));
        if (expected) Assert.That(reader.GetValue(0), Is.EqualTo(new DateTime(2026, 2, 28)));
        else Assert.That(() => reader.GetValue(0), Throws.TypeOf<FormatException>());
    }

    [TestCase(typeof(int), "123", true)]
    [TestCase(typeof(int), "abc", false)]
    [TestCase(typeof(int), "999999999999999999999", false)]
    [TestCase(typeof(string), "2026-02-31", true)]
    [TestCase(typeof(bool), "true", true)]
    [TestCase(typeof(bool), "abc", false)]
    [TestCase(typeof(Guid), "a46cc6b7-8ff4-4c18-aac5-63b185c2c68e", true)]
    [TestCase(typeof(Guid), "abc", false)]
    [TestCase(typeof(int?), "123", true)]
    [TestCase(typeof(int?), "abc", false)]
    [TestCase(typeof(DateOnly), "2026-02-28", true)]
    [TestCase(typeof(DateOnly), "2026-02-31", false)]
    public void RuntimeType_DrivesBothValidationOverloads(Type type, string text, bool expected)
    {
        using var reader = Reader(text, new(type));
        Assert.That(reader.GetFieldType(0), Is.EqualTo(type));
        Assert.That(reader.IsValid(0), Is.EqualTo(expected));
        Assert.That(reader.IsValid("value"), Is.EqualTo(expected));
    }

    [TestCase("1,25", true)]
    [TestCase("1.25", false)]
    public void NumericFormat_PreservesCultureAndStyle(string text, bool expected)
    {
        using var reader = Reader(text, new(typeof(decimal), Format:
            new NumericFormatDescriptor(NumberStyles.Number, CultureInfo.GetCultureInfo("fr-FR"))));
        Assert.That(reader.IsValid(0), Is.EqualTo(expected));
        Assert.That(reader.IsValid("value"), Is.EqualTo(expected));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void NativeSchemaParser_NeverCallsParseDuringValidation(bool runtimeRegistration)
    {
        var parsers = new RuntimeParsersDescriptor();
        var field = new FieldDescriptor(typeof(ValidationNumber));
        if (runtimeRegistration) parsers.AddParser((ParseSpan<ValidationNumber>)ValidationNumber.Parse);
        else field = field with { SpanParse = (ParseSpan<ValidationNumber>)ValidationNumber.Parse };
        using var reader = Reader("42", field, parsers);
        Assert.That(reader.IsValid(0), Is.True);
        Assert.That(reader.IsValid("value"), Is.True);
        Assert.That(() => reader.GetValue(0), Throws.TypeOf<AssertionException>());
    }

    [TestCase("bad", false)]
    [TestCase("42", true)]
    public void FormattedCustomType_UsesTryParseWithoutInvokingParse(string text, bool expected)
    {
        using var reader = Reader(text, new(typeof(FormattedNumber), Format:
            new TemporalFormatDescriptor("number", CultureInfo.InvariantCulture)));
        Assert.That(reader.IsValid(0), Is.EqualTo(expected));
        Assert.That(reader.IsValid("value"), Is.EqualTo(expected));
    }

    public readonly record struct ValidationNumber(int Value)
    {
        public static ValidationNumber Parse(ReadOnlySpan<char> text) => throw new AssertionException("Parse must not be used by IsValid.");
        public static bool TryParse(ReadOnlySpan<char> text, out ValidationNumber value)
        {
            var success = int.TryParse(text, out var number);
            value = new(number);
            return success;
        }
    }

    public readonly record struct FormattedNumber(int Value)
    {
        public static FormattedNumber ParseExact(string text, string pattern, IFormatProvider culture, DateTimeStyles styles)
            => throw new AssertionException("ParseExact must not be used by IsValid.");
        public static bool TryParseExact(ReadOnlySpan<char> text, ReadOnlySpan<char> pattern, IFormatProvider culture,
            DateTimeStyles styles, out FormattedNumber value)
        {
            Assert.That(pattern.SequenceEqual("number"), Is.True);
            var success = int.TryParse(text, NumberStyles.Integer, culture, out var number);
            value = new(number);
            return success;
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ParseOnlyFieldParser_TakesPrecedenceAndKeepsThrowingGetter(bool named)
    {
        var calls = 0;
        using var reader = Reader("42", new(typeof(int), Parse: text => { calls++; throw new FormatException("Rejected by custom parser"); }));
        Assert.That(Validate(reader, named), Is.False);
        Assert.That(Validate(reader, named), Is.False);
        Assert.That(calls, Is.EqualTo(2));
        Assert.That(() => reader.GetInt32(0), Throws.TypeOf<FormatException>());
        Assert.That(calls, Is.EqualTo(3));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void UnexpectedParserFailures_Propagate(bool named)
    {
        var error = new InvalidOperationException("Broken parser");
        using var reader = Reader("42", new(typeof(int), Parse: _ => throw error));
        Assert.That(() => Validate(reader, named), Throws.Exception.SameAs(error));
    }

    [Test]
    public void InvalidFormatConfiguration_PropagatesInsteadOfReturningFalse()
    {
        using var reader = Reader("42", new(typeof(int), Format: new NumericFormatDescriptor((NumberStyles)(-1), CultureInfo.InvariantCulture)));
        Assert.That(() => reader.IsValid(0), Throws.InstanceOf<ArgumentException>());
    }

    [TestCase(typeof(string), false)]
    [TestCase(typeof(string), true)]
    [TestCase(typeof(int?), true)]
    public void CsvNull_FollowsGetValueRatherThanInventingNullability(Type type, bool nullable)
    {
        var resource = new ResourceDescriptorBuilder().WithSequence("NULL", null).Build();
        using var reader = Reader("NULL", new(type, IsNullable: nullable), resource: resource);
        Assert.That(reader.IsDBNull(0), Is.True);
        Assert.That(reader.IsValid(0), Is.False);
        Assert.That(reader.IsValid("value"), Is.False);
        Assert.That(() => reader.GetValue(0), Throws.TypeOf<InvalidCastException>());
    }

    [Test]
    public void SchemaFreeAndIncompleteSchema_FollowExistingReadBehavior()
    {
        using var reader = Reader("2026-02-31");
        Assert.That(reader.GetFieldType(0), Is.EqualTo(typeof(object)));
        Assert.That(reader.IsValid(0), Is.True);
        Assert.That(reader.IsValid("value"), Is.True);
        Assert.That(reader.GetValue(0), Is.EqualTo("2026-02-31"));
        using var partial = Reader("abc", new(typeof(int)));
        Assert.That(() => partial.IsValid("other"), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => partial.GetValue(1), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Validation_DoesNotAdvanceOrBindAGetterParser()
    {
        var profile = Profile(new(typeof(DateTime), Format: new TemporalFormatDescriptor("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        using var reader = new CsvDataReader(Input("value;other\n2026-02-28;x\n2026-02-31;y\n"), profile);
        Assert.That(reader.Read(), Is.True);
        var row = reader.RowCount;
        for (var count = 0; count < 5; count++) Assert.That(reader.IsValid("value"), Is.True);
        Assert.That(reader.RowCount, Is.EqualTo(row));
        var entries = (IDictionary)typeof(BaseDataRecord<CsvProfile>).GetField("_schemaParsers", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(reader)!;
        var entry = entries.Values.Cast<SpanParser.ParserEntry>().Single();
        var safe = entry.GetTryParser<DateTime>();
        var validator = typeof(SpanParser.ParserEntry).GetField("_validate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(entry);
        Assert.That(validator, Is.Not.Null);
        Assert.That(typeof(SpanParser.ParserEntry).GetField("_boxed", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(entry), Is.Null);
        var parser = (SpanParser)typeof(BaseDataRecord<CsvProfile>).GetProperty("Parser", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(reader)!;
        Assert.That(parser.TryGetEntry(0, out _), Is.False);
        Assert.That(reader.GetDateTime(0), Is.EqualTo(new DateTime(2026, 2, 28)));
        Assert.That(parser.TryGetEntry(0, out var bound), Is.True);
        Assert.That(bound, Is.SameAs(entry));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.IsValid(0), Is.False);
        Assert.That(entry.GetTryParser<DateTime>(), Is.SameAs(safe));
        Assert.That(typeof(SpanParser.ParserEntry).GetField("_validate", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(entry), Is.SameAs(validator));
        Assert.That(reader.GetRawString(1), Is.EqualTo("y"));
        Assert.That(() => reader.GetDateTime(0), Throws.TypeOf<FormatException>());
    }

    [Test]
    public void DifferentTypedGetter_DoesNotChangeDeclaredValidationTypeOrGetValueBinding()
    {
        using var reader = new CsvDataReader(Input("value;other\n42;x\n3000000000;y\n"),
            Profile(new(typeof(long), Format: new NumericFormatDescriptor(NumberStyles.Integer, CultureInfo.InvariantCulture))));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(42));
        Assert.That(reader.GetValue(0), Is.TypeOf<int>());
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.IsValid(0), Is.True);
        Assert.That(reader.IsValid("value"), Is.True);
        Assert.That(reader.GetFieldType(0), Is.EqualTo(typeof(long)));
        Assert.That(() => reader.GetValue(0), Throws.TypeOf<OverflowException>(),
            "Validation must not overwrite the existing int getter binding when validating the declared long type.");
    }

    [Test]
    public void FieldFormat_TakesPrecedenceOverRuntimeParser()
    {
        var parsers = new RuntimeParsersDescriptor();
        parsers.AddParser<int>((ReadOnlySpan<char> _) => throw new FormatException("Rejected runtime value"));
        using var formatted = Reader("42", new(typeof(int), Format: new NumericFormatDescriptor(NumberStyles.Integer, CultureInfo.InvariantCulture)), parsers);
        Assert.That(formatted.IsValid(0), Is.True);
        Assert.That(formatted.GetValue(0), Is.EqualTo(42));
        using var unformatted = Reader("42", new(typeof(int)), parsers);
        Assert.That(unformatted.IsValid(0), Is.False);
    }

    [TestCase(-1)]
    [TestCase(2)]
    [TestCase(int.MaxValue)]
    public void InvalidOrdinals_Throw(int ordinal)
    {
        using var reader = Reader("42", new(typeof(int)));
        Assert.That(() => reader.IsValid(ordinal), Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void InvalidNames_Throw()
    {
        using var reader = Reader("42");
        Assert.That(() => reader.IsValid("missing"), Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(() => reader.IsValid(null!), Throws.TypeOf<ArgumentNullException>());
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void NoCurrentRecord_ThrowsBeforeReadAfterEofCloseAndDispose(int stage)
    {
        using var reader = Reader("42", read: false);
        if (stage > 0) Assert.That(reader.Read(), Is.True);
        if (stage == 1) Assert.That(reader.Read(), Is.False);
        if (stage == 2) reader.Close();
        if (stage == 3) reader.Dispose();
        Assert.That(() => reader.IsValid(0), Throws.TypeOf<InvalidOperationException>());
        Assert.That(() => reader.IsValid("value"), Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public async Task AsyncRead_ExposesTheSameValidationApi()
    {
        await using var reader = Reader("abc", new(typeof(int)), read: false);
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.IsValid(0), Is.False);
        Assert.That(reader.IsValid("value"), Is.False);
        Assert.That(await reader.ReadAsync(), Is.False);
        Assert.That(() => reader.IsValid(0), Throws.TypeOf<InvalidOperationException>());
    }

    [TestCase("[1|2]", true)]
    [TestCase("[1|bad]", false)]
    public void SchemaArrays_ValidateElementsWithoutBuildingAnArray(string text, bool expected)
    {
        using var reader = Reader(text, new(typeof(int[])));
        Assert.That(reader.IsValid(0), Is.EqualTo(expected));
        Assert.That(reader.IsValid("value"), Is.EqualTo(expected));
        if (expected) Assert.That(reader.GetValue(0), Is.EqualTo(new[] { 1, 2 }));
        else Assert.That(() => reader.GetValue(0), Throws.TypeOf<FormatException>());
    }

    [Test]
    public void ArrayElements_UseCachedTryParseRatherThanParse()
    {
        var parsers = new RuntimeParsersDescriptor();
        parsers.AddParser((ParseSpan<ValidationNumber>)ValidationNumber.Parse);
        using var reader = Reader("[1|2]", new(typeof(ValidationNumber[])), parsers);
        Assert.That(reader.IsValid(0), Is.True);
        Assert.That(reader.IsValid("value"), Is.True);
    }

    [Test]
    public void WholeArrayParser_TakesPrecedenceOverElementParsing()
    {
        using var reader = Reader("[1|2]", new(typeof(int[]), Parse: _ => throw new FormatException()));
        Assert.That(reader.IsValid(0), Is.False);
    }

    private sealed class MissingRecord(Type type) : BaseDataRecord<CsvProfile>(Profile(new(type)), new StringMapper())
    {
        public void Initialize()
        {
            Fields = ["present", "missing"];
            Record = new RecordMemory("42", [new FieldSpan(0, 2)]);
        }
        public override int FieldCount => 2;
        public override string GetRawString(int i) => throw new NotSupportedException();
        protected override NullableSpan GetValueOrThrow(int i) => Record!.Slice(i).Span;
    }

    [Test]
    public void MissingCell_UsesExistingUnparsedDefault()
    {
        var record = new MissingRecord(typeof(int));
        record.Initialize();
        Assert.That(record.GetValue(1), Is.EqualTo(string.Empty));
        Assert.That(record.IsValid(1), Is.True);
        Assert.That(record.IsValid("missing"), Is.True);
    }

    [Test]
    public void BatchReader_ValidatesAcrossStreamsAndRejectsAbsentRows()
    {
        using var batch = new CsvBatchDataReader(new Stream[] { Input("value;other\n42;x\n"), Input("value;other\nbad;y\n") }, Profile(new(typeof(int))));
        Assert.That(() => batch.IsValid(0), Throws.TypeOf<InvalidOperationException>());
        Assert.That(batch.Read(), Is.True);
        Assert.That(batch.IsValid(0), Is.True);
        Assert.That(batch.IsValid("value"), Is.True);
        Assert.That(batch.Read(), Is.True);
        Assert.That(batch.IsValid("value"), Is.False);
        Assert.That(batch.Read(), Is.False);
        Assert.That(() => batch.IsValid(0), Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void RowNumberColumn_UsesItsDeclaredIntegerType()
    {
        var builder = new CsvReaderBuilder().WithDialect(d => d.WithHeader().WithDelimiter(';').WithLineTerminator("\n")).WithRowNumber();
        using var reader = builder.Build().ToDataReader(Input("value;other\n42;tail\n"));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.IsValid("rowNumber"), Is.True);
        Assert.That(reader.IsValid(0), Is.True);
        Assert.That(reader.GetValue(0), Is.EqualTo(1));
    }

    [Test]
    public void RowNumberColumn_PreservesNativeGetterDespiteGlobalSanitization()
    {
        var profile = new CsvReaderBuilder().WithDialect(d => d.WithHeader().WithDelimiter(';').WithLineTerminator("\n"))
            .WithRowNumber().WithResource(r => r.WithSequence("1", null)).Build().Profile;
        using var reader = new CsvDataReader(Input("value;other\n42;tail\n"), profile);
        Assert.That(() => reader.IsValid(0), Throws.TypeOf<InvalidOperationException>());
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetValue(0), Is.EqualTo(1));
        Assert.That(reader.IsValid("rowNumber"), Is.True);
    }
}
