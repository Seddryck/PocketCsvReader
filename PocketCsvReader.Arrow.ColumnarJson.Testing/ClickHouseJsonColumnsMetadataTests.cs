using System.Text;
using Apache.Arrow;
using Apache.Arrow.Types;
using NUnit.Framework;

namespace PocketCsvReader.Arrow.ColumnarJson.Testing;

public class ClickHouseJsonColumnsMetadataTests
{
    [Test]
    public async Task DerivesSchemaInMetaOrderAndReadsStatsFromAsyncOnlyInput()
    {
        using var input = new AsyncOnlyInput("""
            {
                "meta":[
                    {"name":"key","type":"UInt32"},
                    {"name":"amount","type":"Nullable(Decimal(16, 2))"},
                    {"name":"tags","type":"Array(UInt8)"},
                    {"name":"code","type":"FixedString(2)"}
                ],
                "data":{"code":["ab","cd"],"amount":["-12.50",null],"tags":[[1,2],[]],"key":[42,43]},
                "rows":2,
                "rows_before_limit_at_least":3,
                "statistics":{"elapsed":0.001,"rows_read":3,"bytes_read":24}
            }
            """, 9);
        using var stream = await ClickHouseJsonColumnsReader.WithMetadata().ToArrowStreamAsync(input);
        Assert.That(stream.Schema.FieldsList.Select(field => field.Name), Is.EqualTo(new[] { "key", "amount", "tags", "code" }));
        Assert.That(stream.Schema.FieldsList[0].DataType, Is.SameAs(UInt32Type.Default));
        Assert.That(stream.Schema.FieldsList[1].IsNullable, Is.True);
        Assert.That(((Decimal128Type)stream.Schema.FieldsList[1].DataType).Scale, Is.EqualTo(2));
        Assert.That(((ListType)stream.Schema.FieldsList[2].DataType).ValueDataType, Is.SameAs(UInt8Type.Default));
        Assert.That(((FixedSizeBinaryType)stream.Schema.FieldsList[3].DataType).ByteWidth, Is.EqualTo(2));
        using var batch = (await stream.ReadNextRecordBatchAsync())!;
        Assert.That(batch.Length, Is.EqualTo(2));
        Assert.That(((UInt32Array)batch.Column(0)).GetValue(1), Is.EqualTo(43));
        Assert.That(((Decimal128Array)batch.Column(1)).GetValue(0), Is.EqualTo(-12.50m));
        Assert.That(batch.Column(1).IsNull(1), Is.True);
        Assert.That(((UInt8Array)((ListArray)batch.Column(2)).Values).GetValue(1), Is.EqualTo(2));
        Assert.That(await stream.ReadNextRecordBatchAsync(), Is.Null);
    }

    [Test]
    public async Task OpeningReadsTheSchemaWithoutConsumingALargeDataBlock()
    {
        var document = """
            {"meta":[{"name":"id","type":"Int32"}],"data":{"id":[
            """ + string.Join(",", Enumerable.Repeat("23", 50_000)) + """]},"rows":50000}""";
        using var input = new AsyncOnlyInput(document, 4096);

        using var stream = await ClickHouseJsonColumnsReader.WithMetadata().ToArrowStreamAsync(input);
        Assert.That(stream.Schema.FieldsList[0].DataType, Is.SameAs(Int32Type.Default));
        Assert.That(input.BytesRead, Is.LessThan(Encoding.UTF8.GetByteCount(document) / 2));

        using var batch = (await stream.ReadNextRecordBatchAsync())!;
        Assert.That(batch.Length, Is.EqualTo(50_000));
        Assert.That(((Int32Array)batch.Column(0)).GetValue(49_999), Is.EqualTo(23));
    }

    [Test]
    public async Task RejectsDataBeforeMetaWithoutReadingTheDataBlock()
    {
        var document = """{"data":{"id":[""" + string.Join(",", Enumerable.Repeat("1", 50_000))
            + """]},"meta":[{"name":"id","type":"Int32"}]}""";
        using var input = new AsyncOnlyInput(document, 4096);
        var reader = ClickHouseJsonColumnsReader.WithMetadata();

        var error = await Assert.ThrowsAsync<InvalidDataException>(async () => await reader.ToArrowStreamAsync(input));
        Assert.That(error?.Message, Does.Contain("requires meta before data"));
        Assert.That(input.BytesRead, Is.LessThan(Encoding.UTF8.GetByteCount(document) / 2));
    }

    [Test]
    public async Task LazyModeUsesTheMetadataSchemaAndDefersValueConversion()
    {
        using var input = new AsyncOnlyInput("""
            {"meta":[{"name":"id","type":"Int32"},{"name":"extra","type":"UInt8"}],
             "data":{"extra":[256],"id":[3]},"rows":1}
            """, 7);
        using var stream = await ClickHouseJsonColumnsReader.WithMetadata().ToLazyArrowStreamAsync(input);
        Assert.That(stream.Schema.FieldsList[1].DataType, Is.SameAs(UInt8Type.Default));
        using var record = await stream.ReadNextRecordAsync() ?? throw new InvalidOperationException("Expected a batch.");
        Assert.That(((Int32Array)record.Column("id")).GetValue(0), Is.EqualTo(3));
        Assert.That(() => record.Column("extra"), Throws.TypeOf<InvalidDataException>().With.Message.Contains("extra"));
    }

    [Test]
    public async Task LazyFileWithMetadataReadsTheOriginalColumnRange()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """
                {"meta":[{"name":"café","type":"Int32"}],"data":{"café":[17]},"rows":1}
                """, new UTF8Encoding(true));
            using var stream = await ClickHouseJsonColumnsReader.WithMetadata().ToLazyArrowStreamAsync(path);
            using var record = await stream.ReadNextRecordAsync() ?? throw new InvalidOperationException("Expected a batch.");
            stream.Dispose();
            File.Delete(path);

            Assert.That(((Int32Array)record.Column("café")).GetValue(0), Is.EqualTo(17));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task ReadsZeroRowFilesSynchronouslyAndAsynchronously()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, """{"meta":[{"name":"label","type":"LowCardinality(Nullable(String))"}],"data":{"label":[]},"rows":0}""");
            var reader = ClickHouseJsonColumnsReader.WithMetadata();
            using var batch = reader.ToArrowBatches(file).Single();
            Assert.That(batch.Length, Is.Zero);
            using var stream = await reader.ToArrowStreamAsync(file);
            Assert.That(stream.Schema.FieldsList[0].DataType, Is.SameAs(StringType.Default));
            Assert.That(stream.Schema.FieldsList[0].IsNullable, Is.True);
            using var asyncBatch = (await stream.ReadNextRecordBatchAsync())!;
            Assert.That(asyncBatch.Length, Is.Zero);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [TestCase("""{"meta":[{"name":"id","type":"Int32"},{"name":"id","type":"Int32"}],"data":{"id":[]}}""", "duplicate")]
    [TestCase("""{"meta":[{"name":"id"}],"data":{"id":[]}}""", "type")]
    [TestCase("""{"meta":[],"meta":[],"data":{}}""", "Duplicate")]
    [TestCase("""{"meta":[],"data":{},"rows":-1}""", "non-negative")]
    [TestCase("""{"meta":[{"name":"id","type":"Int32"}],"data":{"id":[1]},"rows":2}""", "declares 2 rows")]
    public void RejectsInvalidMetadata(string json, string message)
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(json));
        Assert.That(() => ClickHouseJsonColumnsReader.WithMetadata().ToArrowBatches(input).First(),
            Throws.TypeOf<InvalidDataException>().With.Message.Contains(message));
    }

    [Test]
    public void ReportsTheColumnNameAndUnsupportedType()
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes("""
            {"meta":[{"name":"attributes","type":"Map(String, String)"}],"data":{"attributes":[]}}
            """));
        Assert.That(() => ClickHouseJsonColumnsReader.WithMetadata().ToArrowStream(input),
            Throws.TypeOf<NotSupportedException>().With.Message.Contains("Map(String, String)").And.Message.Contains("attributes"));
    }

    [Test]
    public async Task CancelsMetadataReadAndClosesOwnedInput()
    {
        using var cancellation = new CancellationTokenSource();
        using var input = new AsyncOnlyInput("""{"meta":[],"data":{}}""", 3, cancellation);
        try
        {
            using var stream = await ClickHouseJsonColumnsReader.WithMetadata(leaveOpen: false)
                .ToArrowStreamAsync(input, cancellation.Token);
            Assert.Fail("Expected cancellation.");
        }
        catch (OperationCanceledException exception)
        {
            Assert.That(exception.CancellationToken, Is.EqualTo(cancellation.Token));
        }
        Assert.That(input.Disposed, Is.True);
    }
}
