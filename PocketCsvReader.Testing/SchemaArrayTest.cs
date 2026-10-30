using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;
using PocketCsvReader.FieldParsing;

namespace PocketCsvReader.Testing;

public class SchemaArrayTest
{
    private static CsvDataReader Reader(string text, SchemaDescriptor? schema = null, RuntimeParsersDescriptor? parsers = null, bool header = false)
        => new(new MemoryStream(Encoding.UTF8.GetBytes(text)), new CsvProfile(
            new DialectDescriptorBuilder().WithDelimiter(';').WithHeader(header).WithLineTerminator("\n").WithNullSequence("NULL")
                .WithArray(a => a.WithDelimiter('|').WithPrefix('[').WithSuffix(']')).Build(),
            schema, null, parsers));

    [TestCase("[10|20|30]")]
    [TestCase("[42]")]
    public void GetValue_IntegerArrayMatchesTypedRead(string text)
    {
        using var reader = Reader(text, new SchemaDescriptorBuilder().Indexed().WithField<int[]>().Build());
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetFieldType(0), Is.EqualTo(typeof(int[])));
        Assert.That(reader.GetValue(0), Is.TypeOf<int[]>().And.EqualTo(reader.GetArray<int>(0)));
        Assert.That(reader.GetValue(0), Is.Not.SameAs(reader.GetValue(0)));
    }

    [Test]
    public void GetValue_NamedSchemaPreservesScalarFieldsAcrossRows()
    {
        var schema = new SchemaDescriptorBuilder().Named().WithField<int[]>("Scores")
            .WithField<string[]>("Tags").WithField<int>("Age").WithField<string>("Other").Build();
        using var reader = Reader("Tags;Age;Scores;Other\n[a|b];42;[10|20];raw\n[c];43;[30];next\n", schema, header: true);
        for (int row = 0; row < 2; row++)
        {
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.GetValue("Scores"), Is.TypeOf<int[]>().And.EqualTo(reader.GetArray<int>(2)));
            Assert.That(reader["Tags"], Is.TypeOf<string[]>().And.EqualTo(reader.GetArray<string>(0)));
            Assert.That(reader.GetValue(1), Is.EqualTo(42 + row));
            Assert.That(reader.GetValue(3), Is.EqualTo(row == 0 ? "raw" : "next"));
            var values = new object[4];
            reader.GetValues(values);
            Assert.That(values[2], Is.TypeOf<int[]>());
            Assert.That(reader.GetFieldType(0), Is.EqualTo(typeof(string[])));
        }
    }

    [TestCase("[true|false]", typeof(bool[]))]
    [TestCase("[1.5|2.5]", typeof(decimal[]))]
    [TestCase("[2026-10-30]", typeof(DateOnly[]))]
    [TestCase("[a|b]", typeof(string[]))]
    public void GetValue_OtherSupportedElementTypes(string text, Type type)
    {
        using var reader = Reader(text, new SchemaDescriptorBuilder().Indexed().WithField(type).Build());
        reader.Read();
        var array = (Array)reader.GetValue(0);
        Assert.That(array.GetType(), Is.EqualTo(type));
        if (type == typeof(bool[])) Assert.That(array, Is.EqualTo(reader.GetArray<bool>(0)));
        if (type == typeof(decimal[])) Assert.That(array, Is.EqualTo(reader.GetArray<decimal>(0)));
        if (type == typeof(DateOnly[])) Assert.That(array, Is.EqualTo(reader.GetArray<DateOnly>(0)));
        if (type == typeof(string[])) Assert.That(array, Is.EqualTo(reader.GetArray<string>(0)));
    }

    [Test]
    public void GetValue_UsesRuntimeElementParser()
    {
        var parsers = new RuntimeParsersDescriptor();
        parsers.AddParser<int?>(s => s.IsEmpty ? null : int.Parse(s) + 1);
        using var reader = Reader("[1|2]", new SchemaDescriptorBuilder().Indexed().WithField<int?[]>().Build(), parsers);
        reader.Read();
        Assert.That(reader.GetValue(0), Is.TypeOf<int?[]>().And.EqualTo(new int?[] { 2, 3 }));
        Assert.That(reader.GetValue(0), Is.EqualTo(reader.GetArray<int?>(0)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void GetValue_ExplicitFieldArrayParserTakesPrecedence(bool spanParser)
    {
        var schema = new SchemaDescriptorBuilder().Indexed().WithField<byte[]>(field => spanParser
            ? field.WithParser((ReadOnlySpan<char> text) => Convert.FromBase64String(text.ToString()))
            : field.WithParser(Convert.FromBase64String)).Build();
        using var reader = Reader("AQI=\nAwQ=", schema);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetValue(0), Is.TypeOf<byte[]>().And.EqualTo(new byte[] { 1, 2 }));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetValue(0), Is.EqualTo(new byte[] { 3, 4 }));
    }

    [Test]
    public void GetValue_ExplicitRuntimeArrayParserTakesPrecedence()
    {
        var parsers = new RuntimeParsersDescriptor();
        parsers.AddParser<byte[]>(Convert.FromBase64String);
        using var reader = Reader("AQI=", new SchemaDescriptorBuilder().Indexed().WithField<byte[]>().Build(), parsers);
        reader.Read();
        Assert.That(reader.GetValue(0), Is.TypeOf<byte[]>().And.EqualTo(new byte[] { 1, 2 }));
    }

    [Test]
    public void GetValue_CustomArraySyntaxAndEscapingMatchesTypedRead()
    {
        var dialect = new DialectDescriptorBuilder().WithoutHeader().WithDelimiter(';').WithQuoteChar('\'')
            .WithEscapeChar('\\').WithDoubleQuote(true).WithLineTerminator("\n")
            .WithArray(a => a.WithDelimiter('~').WithPrefix('{').WithSuffix('}')).Build();
        using var reader = new CsvDataReader(new MemoryStream(Encoding.UTF8.GetBytes("{'fo''o'~bar};tail")),
            new CsvProfile(dialect, new SchemaDescriptorBuilder().Indexed().WithField<string[]>().Build()));
        reader.Read();
        Assert.That(reader.GetValue(0), Is.TypeOf<string[]>().And.EqualTo(reader.GetArray<string>(0)));
        Assert.That((string[])reader.GetValue(0), Has.Length.EqualTo(2));
    }

    [TestCase("[1|invalid]")]
    [TestCase("[]")]
    [TestCase("")]
    [TestCase("NULL")]
    public void GetValue_EdgeCasesPreserveTypedReadResultsOrExceptions(string text)
    {
        using var reader = Reader($"{text};tail", new SchemaDescriptorBuilder().Indexed().WithField<int[]>().Build());
        reader.Read();
        int[]? expected;
        try { expected = reader.GetArray<int>(0); }
        catch (Exception ex)
        {
            Assert.That(() => reader.GetValue(0), Throws.TypeOf(ex.GetType()).With.Message.EqualTo(ex.Message));
            return;
        }
        Assert.That(reader.GetValue(0), Is.EqualTo(expected));
    }

    [Test]
    public void GetArray_WithoutSchemaRetainsExplicitParsing()
    {
        using var reader = Reader("[10|20];plain");
        reader.Read();
        Assert.That(reader.GetArray<int>(0), Is.EqualTo(new[] { 10, 20 }));
        Assert.That(reader.GetValue(0), Is.TypeOf<string>());
        Assert.That(reader.GetValue(1), Is.EqualTo("plain"));
    }

    private sealed class ArrayRecord : BaseDataRecord<CsvProfile>
    {
        public ArrayRecord(FieldSpan[] children, Type type) : base(
            new CsvProfile(new DialectDescriptorBuilder().Build(),
                new SchemaDescriptorBuilder().Indexed().WithField(type).Build()), new StringMapper())
        {
            Fields = ["value"];
            Record = new RecordMemory("42", [new FieldSpan(new SpanInfo(0, 2), default, children)]);
        }
        public override int FieldCount => 1;
        public override string GetRawString(int i) => throw new NotSupportedException();
        protected override NullableSpan GetValueOrThrow(int i) => Record!.Slice(i).Span;
    }

    [Test]
    public void GetValue_EmptyChildrenReturnEmptyTypedArray()
    {
        var record = new ArrayRecord([], typeof(int[]));
        Assert.That(record.GetValue(0), Is.TypeOf<int[]>().And.Empty);
        Assert.That(record.GetValue(0), Is.EqualTo(record.GetArray<int>(0)));
    }

    [Test]
    public void GetValue_NullChildrenMatchTypedRead()
    {
        var children = new[] { new FieldSpan(new SpanInfo(0, 0, IsNull: true), default), new FieldSpan(0, 2) };
        var record = new ArrayRecord(children, typeof(int[]));
        Assert.That(record.GetValue(0), Is.EqualTo(new[] { 0, 42 }));
        Assert.That(record.GetValue(0), Is.EqualTo(record.GetArray<int>(0)));
        var strings = new ArrayRecord(children, typeof(string[]));
        Assert.That(strings.GetValue(0), Is.EqualTo(new string?[] { null, "42" }));
    }

    [Test]
    public void GetValue_MultidimensionalArrayIsRejected()
    {
        using var reader = Reader("[1|2]", new SchemaDescriptorBuilder().Indexed().WithField<int[,]>().Build());
        reader.Read();
        Assert.That(() => reader.GetValue(0), Throws.TypeOf<NotSupportedException>());
    }
}
