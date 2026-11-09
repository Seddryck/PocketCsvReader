using System.Text;
using Apache.Arrow;
using Apache.Arrow.Types;
using NUnit.Framework;

namespace PocketCsvReader.Arrow.ColumnarJson.Testing;

public class ClickHouseJsonColumnsTests
{
    private static readonly Schema Schema = new Schema.Builder()
        .Field(new Field("id", UInt64Type.Default, false))
        .Field(new Field("label", StringType.Default, true))
        .Field(new Field("enabled", BooleanType.Default, false))
        .Build();

    [Test]
    public async Task MatchesColumnsInSchemaOrderAndHonorsTypesOnAsyncOnlyInput()
    {
        using var input = new AsyncOnlyInput("""
            {"enabled":[true,false],"label":["a\tb",null],"id":["18446744073709551615",1]}
            """, 5);
        using var stream = await new ClickHouseJsonColumnsReader(Schema).ToArrowStreamAsync(input);
        Assert.That(stream.Schema, Is.SameAs(Schema));
        using var batch = (await stream.ReadNextRecordBatchAsync())!;
        Assert.That(batch.Length, Is.EqualTo(2));
        Assert.That(((UInt64Array)batch.Column(0)).GetValue(0), Is.EqualTo(ulong.MaxValue));
        Assert.That(((StringArray)batch.Column(1)).GetString(0), Is.EqualTo("a\tb"));
        Assert.That(batch.Column(1).IsNull(1), Is.True);
        Assert.That(((BooleanArray)batch.Column(2)).GetValue(1), Is.False);
        Assert.That(await stream.ReadNextRecordBatchAsync(), Is.Null);
    }

    [Test]
    public void DecodesDatesAndListValues()
    {
        var schema = new Schema.Builder()
            .Field(new Field("day", Date32Type.Default, false))
            .Field(new Field("values", new ListType(new Field("item", Int32Type.Default, true)), true))
            .Build();
        using var input = Input("""{"values":[[1,null,3],null,[]],"day":["1970-01-02","1969-12-31","1970-01-01"]}""");
        using var batch = new ClickHouseJsonColumnsReader(schema).ToArrowBatches(input).Single();
        Assert.That(((Date32Array)batch.Column(0)).Values[0], Is.EqualTo(1));
        Assert.That(((Date32Array)batch.Column(0)).Values[1], Is.EqualTo(-1));
        var lists = (ListArray)batch.Column(1);
        Assert.That(lists.ValueOffsets.ToArray(), Is.EqualTo(new[] { 0, 3, 3, 3 }));
        Assert.That(lists.IsNull(1), Is.True);
        var items = (Int32Array)lists.Values;
        Assert.That(items.GetValue(0), Is.EqualTo(1));
        Assert.That(items.IsNull(1), Is.True);
    }

    [Test]
    public async Task RejectsAnInvalidFirstCellBeforeReadingTheRestOfItsColumn()
    {
        var schema = new Schema.Builder().Field(new Field("id", Int32Type.Default, false)).Build();
        var document = """{"id":["bad",""" + string.Join(",", Enumerable.Repeat("1", 50_000)) + "]}";
        using var input = new AsyncOnlyInput(document, 4096);
        using var stream = await new ClickHouseJsonColumnsReader(schema).ToArrowStreamAsync(input);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(async () => await stream.ReadNextRecordBatchAsync());
        Assert.That(exception?.Message, Does.Contain("column 'id'"));
        Assert.That(input.BytesRead, Is.LessThan(Encoding.UTF8.GetByteCount(document) / 2));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void LargeInputWithSmallOutputStaysWithinTheAllocationBudget(bool asynchronous)
    {
        var schema = new Schema.Builder().Field(new Field("id", Int32Type.Default, false)).Build();
        var reader = new ClickHouseJsonColumnsReader(schema);
        const string small = """{"id":[1,2]}""";
        var large = """{"id":[1,""" + new string(' ', 1_000_000) + "2]}";
        using var warmInput = TestInput(small, asynchronous);
        using var warmBatch = ReadBatch(reader, warmInput, asynchronous);
        using var input = TestInput(large, asynchronous);

        var before = GC.GetAllocatedBytesForCurrentThread();
        using var batch = ReadBatch(reader, input, asynchronous);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.That(((Int32Array)batch.Column(0)).GetValue(1), Is.EqualTo(2));
        // The Arrow output has two values. Capturing a million chars for the whole
        // column would need over 2 MB even before the consolidated frame is built.
        Assert.That(allocated, Is.LessThan(512_000));
    }

    private static Stream TestInput(string json, bool asynchronous)
        => asynchronous ? new AsyncOnlyInput(json, 4096) : Input(json);

    private static RecordBatch ReadBatch(ClickHouseJsonColumnsReader reader, Stream input, bool asynchronous)
    {
        // AsyncOnlyInput completes reads inline, keeping the allocation measurement on this thread.
        using var stream = asynchronous
            ? reader.ToArrowStreamAsync(input).AsTask().GetAwaiter().GetResult()
            : reader.ToArrowStream(input);
        return stream.ReadNextRecordBatchAsync().AsTask().GetAwaiter().GetResult()
            ?? throw new InvalidOperationException("Expected a ClickHouse batch.");
    }

    [Test]
    public void EmptyColumnsProduceOneEmptyBatch()
    {
        using var input = Input("""{"enabled":[],"label":[],"id":[]}""");
        using var batch = new ClickHouseJsonColumnsReader(Schema).ToArrowBatches(input).Single();
        Assert.That(batch.Length, Is.Zero);
        Assert.That(batch.ColumnCount, Is.EqualTo(3));
    }

    [TestCase("""{"enabled":[true],"id":[1]}""", "Missing ClickHouse column 'label'")]
    [TestCase("""{"enabled":[true],"label":[],"id":[1]}""", "contains 0 rows")]
    [TestCase("""{"enabled":[true],"label":[null],"id":[-1]}""", "column 'id'")]
    [TestCase("""{"enabled":[null],"label":[null],"id":[1]}""", "non-nullable")]
    [TestCase("""{"enabled":[1],"label":[null],"id":[1]}""", "boolean")]
    [TestCase("""{"enabled":[true],"label":[7],"id":[1]}""", "JSON string")]
    [TestCase("""{"enabled":[true],"label":[null],"id":[1],"i\u0064":[1]}""", "Duplicate")]
    [TestCase("""{"enabled":[true],"label":[null],"id":[1]} {}""", "Unexpected content")]
    [TestCase("""{"enabled":[true],"label":[null],"id":[1,]}""", "JSON value")]
    public void RejectsMalformedOrIncompatibleInput(string json, string message)
    {
        using var input = Input(json);
        Assert.That(() => new ClickHouseJsonColumnsReader(Schema).ToArrowBatches(input).First(),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains(message));
    }

    private static MemoryStream Input(string json) => new(Encoding.UTF8.GetBytes(json));
}
