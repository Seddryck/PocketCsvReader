using System.Globalization;
using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;
using PocketCsvReader.FieldParsing;

namespace PocketCsvReader.Testing;

public class SpanCustomParserTests
{
    private static int ParseNumber(ReadOnlySpan<char> value)
        => int.Parse(value, CultureInfo.InvariantCulture) + 1;

    private static CsvDataReader Reader(string content, bool field, bool legacy = false)
    {
        var schema = new SchemaDescriptorBuilder().Indexed();
        var parsers = new RuntimeParsersDescriptorBuilder();
        if (field)
            schema.WithField<int>(f => legacy
                ? f.WithParser(s => int.Parse(s, CultureInfo.InvariantCulture) + 1)
                : f.WithParser(ParseNumber));
        else
        {
            schema.WithField<int>();
            if (legacy)
                parsers.WithParser(s => int.Parse(s, CultureInfo.InvariantCulture) + 1);
            else
                parsers.WithParser((ParseSpan<int>)ParseNumber);
        }
        var descriptor = schema.Build()!;
        if (!field)
        {
            // An explicit field format takes precedence over a runtime parser.
            descriptor = new SchemaDescriptor.IndexedSchemaDescriptor();
            descriptor.Fields.Add(new FieldDescriptor(typeof(int)));
        }
        var profile = new CsvProfile(
            new DialectDescriptorBuilder().WithoutHeader()
                .WithArray(a => a.WithDelimiter('|').WithPrefix('[').WithSuffix(']')).Build(),
            descriptor, null, parsers.Build());
        var reader = new CsvDataReader(new MemoryStream(Encoding.UTF8.GetBytes(content)), profile);
        Assert.That(reader.Read(), Is.True);
        return reader;
    }

    [TestCase(true, false)]
    [TestCase(false, false)]
    [TestCase(true, true)]
    [TestCase(false, true)]
    public void TypedAndBoxedReads_CacheTheSameParser(bool field, bool legacy)
    {
        using var reader = Reader("41", field, legacy);
        Assert.That(reader.GetFieldValue<int>(0), Is.EqualTo(42));
        Assert.That(reader.GetValue(0), Is.EqualTo(42));
        Assert.That(reader.GetFieldValue<int>(0), Is.EqualTo(42));
        Assert.That(reader.GetInt32(0), Is.EqualTo(42));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void BoxedReadBeforeTypedRead_PreservesParserType(bool field)
    {
        using var reader = Reader("41", field);
        Assert.That(reader.GetValue(0), Is.EqualTo(42));
        Assert.That(reader.GetFieldValue<int>(0), Is.EqualTo(42));
    }

    [TestCase(true, false)]
    [TestCase(false, false)]
    [TestCase(true, true)]
    [TestCase(false, true)]
    public void Arrays_UseCustomParserForElements(bool field, bool legacy)
    {
        using var reader = Reader("[40|41]", field, legacy);
        Assert.That(reader.GetArray<int>(0), Is.EqualTo(new[] { 41, 42 }));
        Assert.That(reader.GetArray(0), Is.EqualTo(new object[] { 41, 42 }));
        Assert.That(reader.GetArrayItem<int>(0, 1), Is.EqualTo(42));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void SpanReads_DoNotAllocateIntermediateStrings(bool field)
    {
        using var reader = Reader("41", field);
        for (int i = 0; i < 1000; i++)
            reader.GetFieldValue<int>(0);
        long before = GC.GetAllocatedBytesForCurrentThread();
        int sum = 0;
        for (int i = 0; i < 1000; i++)
            sum += reader.GetFieldValue<int>(0);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.That(sum, Is.EqualTo(42000));
        Assert.That(allocated, Is.Zero);
    }

    [Test]
    public void AdHocOverloads_AcceptSpanAndLegacyLambdasAndMethodGroups()
    {
        using var reader = Reader("41", false);
        Assert.That(reader.GetFieldValue(0, (ParseSpan<int>)ParseNumber), Is.EqualTo(42));
        Assert.That(reader.GetFieldValue(reader.GetName(0), (ReadOnlySpan<char> s) => ParseNumber(s)), Is.EqualTo(42));
        Assert.That(reader.GetFieldValue(0, s => int.Parse(s, CultureInfo.InvariantCulture)), Is.EqualTo(41));
        Assert.That(reader.GetFieldValue<int>(0, int.Parse), Is.EqualTo(41));
    }

    [Test]
    public void FieldParser_TakesPrecedenceOverRuntimeParserAndFormat()
    {
        var profile = new CsvProfile(new DialectDescriptorBuilder().WithoutHeader().Build(),
            new SchemaDescriptorBuilder().Indexed()
                .WithIntegerField<int>(f => f.WithParser(ParseNumber).WithName("value")).Build(),
            null, new RuntimeParsersDescriptorBuilder().WithParser((ReadOnlySpan<char> _) => -1).Build());
        using var reader = new CsvDataReader(new MemoryStream("41"u8.ToArray()), profile);
        reader.Read();
        Assert.That(reader.GetInt32(0), Is.EqualTo(42));
        Assert.That(reader.GetFieldValue<int>(0), Is.EqualTo(42));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Nulls_SkipCustomParserAndEmptyStringsReachIt(bool field)
    {
        int calls = 0;
        ParseSpan<string> parse = s => { calls++; return $"<{s.ToString()}>"; };
        var schema = new SchemaDescriptorBuilder().Indexed();
        if (field) schema.WithField<string>(f => f.WithParser(parse));
        else schema.WithField<string>();
        var parsers = new RuntimeParsersDescriptor();
        if (!field) parsers.AddParser(parse);
        var profile = new CsvProfile(new DialectDescriptorBuilder().WithoutHeader().Build(), schema.Build(),
            new ResourceDescriptorBuilder().WithSequence("NULL", null).Build(), parsers);
        using var reader = new CsvDataReader(new MemoryStream("NULL\r\n\"\""u8.ToArray()), profile);
        reader.Read();
        Assert.That(reader.GetFieldValue<string>(0), Is.Null);
        Assert.That(reader.GetFieldValue(0, parse), Is.Null);
        Assert.That(calls, Is.Zero);
        reader.Read();
        Assert.That(reader.GetFieldValue<string>(0), Is.EqualTo("<>"));
        Assert.That(calls, Is.EqualTo(1));
    }

    [Test]
    public void Builder_LastRegistrationWinsAndEnumerationRemainsCompatible()
    {
        var builder = new RuntimeParsersDescriptorBuilder()
            .WithParser((string _) => -1).WithParser((ParseSpan<int>)ParseNumber);
        var descriptor = builder.Build()!;
        Assert.That(descriptor.Count, Is.EqualTo(1));
        Assert.That(descriptor.Single().Value("41"), Is.EqualTo(42));
        builder.WithParser((string _) => 7);
        Assert.That(builder.Build()!.Single().Value("41"), Is.EqualTo(7));
        Assert.That(descriptor.Single().Value("41"), Is.EqualTo(42));
    }

    [Test]
    public void SpecializedFieldBuilders_PreserveSpanParsersAndFluentReturnTypes()
    {
        var schema = new SchemaDescriptorBuilder().Indexed()
            .WithNumberField<decimal>(f => f.WithParser((ReadOnlySpan<char> _) => 12m).WithFormat(x => x))
            .WithTemporalField<DateOnly>(f => f.WithParser((ReadOnlySpan<char> _) => new DateOnly(2026, 10, 22)).WithFormat("yyyy"))
            .WithCustomField(typeof(Guid), f => f.WithParser((ReadOnlySpan<char> _) => Guid.Empty).WithFormat("D"))
            .Build()!;
        var profile = new CsvProfile(new DialectDescriptorBuilder().WithoutHeader().Build(), schema);
        using var reader = new CsvDataReader(new MemoryStream("ignored,ignored,ignored"u8.ToArray()), profile);
        reader.Read();
        Assert.That(reader.GetDecimal(0), Is.EqualTo(12m));
        Assert.That(reader.GetDate(1), Is.EqualTo(new DateOnly(2026, 10, 22)));
        Assert.That(reader.GetValue(2), Is.EqualTo(Guid.Empty));
    }

    [Test]
    public void FieldBuilder_LastParserWinsAndRejectsWrongSpanReturnType()
    {
        var schema = new SchemaDescriptorBuilder().Indexed()
            .WithField<int>(f => f.WithParser((ReadOnlySpan<char> _) => -1).WithParser((string _) => 42))
            .WithField<int>(f => f.WithParser((string _) => -1).WithParser((ReadOnlySpan<char> _) => 43)).Build()!;
        Assert.That(schema.Fields[0].SpanParse, Is.Null);
        Assert.That(schema.Fields[0].Parse!(""), Is.EqualTo(42));
        Assert.That(((ParseSpan<int>)schema.Fields[1].SpanParse!)(""), Is.EqualTo(43));
        Assert.Throws<ArgumentException>(() => new SchemaDescriptorBuilder().Indexed()
            .WithField<int>(f => f.WithParser((ReadOnlySpan<char> _) => "wrong")));
    }

    private sealed class ArrayRecord : BaseDataRecord<CsvProfile>
    {
        public ArrayRecord(CsvProfile profile) : base(profile, new StringMapper(profile.ParserOptimizations.PoolString))
        {
            Fields = ["value"];
            Record = new RecordMemory("41", [new FieldSpan(new SpanInfo(0, 2), default,
                [new FieldSpan(new SpanInfo(0, 0, IsNull: true), default), new FieldSpan(0, 2)])]);
        }
        public override int FieldCount => 1;
        public override string GetRawString(int i) => throw new NotSupportedException();
        protected override NullableSpan GetValueOrThrow(int i) => Record!.Slice(i).Span;
    }

    [TestCase(true)]
    [TestCase(false)]
    public void NullableArrayElements_SkipParserForNullChildren(bool field)
    {
        int calls = 0;
        ParseSpan<int?> parse = s => { calls++; return ParseNumber(s); };
        var schema = new SchemaDescriptor.IndexedSchemaDescriptor();
        schema.Fields.Add(new FieldDescriptor(typeof(int?)) { SpanParse = field ? parse : null });
        var parsers = new RuntimeParsersDescriptor();
        if (!field) parsers.AddParser(parse);
        var record = new ArrayRecord(new CsvProfile(new DialectDescriptorBuilder().Build(), schema, null, parsers));
        Assert.That(record.GetArray<int?>(0), Is.EqualTo(new int?[] { null, 42 }));
        Assert.That(record.GetArray(0), Is.EqualTo(new object?[] { null, 42 }));
        Assert.That(record.GetArrayItem<int?>(0, 0), Is.Null);
        Assert.That(calls, Is.EqualTo(2));
    }

    [Test]
    public void SanitizedAndDecodedValues_ReachSpanParser()
    {
        var profile = new CsvProfile(new DialectDescriptorBuilder().WithoutHeader().Build(),
            new SchemaDescriptorBuilder().Indexed().WithField<string>(f =>
                f.WithParser((ReadOnlySpan<char> s) => s.ToString())).Build());
        using var reader = new CsvDataReader(new MemoryStream("\"a\"\"b\""u8.ToArray()), profile);
        reader.Read();
        Assert.That(reader.GetFieldValue<string>(0), Is.EqualTo("a\"b"));
    }
}
