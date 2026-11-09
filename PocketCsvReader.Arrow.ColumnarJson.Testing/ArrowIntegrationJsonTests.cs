using System.Text;
using Apache.Arrow;
using Apache.Arrow.Types;
using NUnit.Framework;

namespace PocketCsvReader.Arrow.ColumnarJson.Testing;

public class ArrowIntegrationJsonTests
{
    private const string Fixture = """
        {
          "schema": {
            "fields": [
              {"name":"id","nullable":false,"type":{"name":"int","isSigned":false,"bitWidth":64}},
              {"name":"label","nullable":true,"type":{"name":"utf8"},"metadata":[{"key":"role","value":"label"}]},
              {"name":"at","nullable":false,"type":{"name":"timestamp","unit":"NANOSECOND","timezone":"UTC"}},
              {"name":"cost","nullable":false,"type":{"name":"decimal","precision":20,"scale":2,"bitWidth":128}}
            ],
            "metadata":[{"key":"source","value":"test"}]
          },
          "batches": [
            {"count":2,"columns":[
              {"name":"id","count":2,"VALIDITY":[1,1],"DATA":["18446744073709551615","4"]},
              {"name":"label","count":2,"VALIDITY":[1,0],"OFFSET":[0,5,5],"DATA":["caf\u00e9",""]},
              {"name":"at","count":2,"VALIDITY":[1,1],"DATA":["1000000001","0"]},
              {"name":"cost","count":2,"VALIDITY":[1,1],"DATA":["-12345","5"]}
            ]},
            {"count":0,"columns":[
              {"name":"id","count":0,"VALIDITY":[],"DATA":[]},
              {"name":"label","count":0,"VALIDITY":[],"OFFSET":[0],"DATA":[]},
              {"name":"at","count":0,"VALIDITY":[],"DATA":[]},
              {"name":"cost","count":0,"VALIDITY":[],"DATA":[]}
            ]}
          ]
        }
        """;

    [Test]
    public async Task PreservesSchemaAndBatchBoundariesOnAsyncOnlyInput()
    {
        using var input = new AsyncOnlyInput(Fixture, 11);
        using var stream = await new ArrowIntegrationJsonReader().ToArrowStreamAsync(input);
        Assert.That(stream.Schema.FieldsList.Select(field => field.Name), Is.EqualTo(new[] { "id", "label", "at", "cost" }));
        Assert.That(stream.Schema.Metadata["source"], Is.EqualTo("test"));
        Assert.That(stream.Schema.FieldsList[1].Metadata["role"], Is.EqualTo("label"));
        Assert.That(stream.Schema.FieldsList[1].IsNullable, Is.True);
        var timestamp = (TimestampType)stream.Schema.FieldsList[2].DataType;
        Assert.That(timestamp.Timezone, Is.EqualTo("UTC"));
        Assert.That(timestamp.Unit, Is.EqualTo(TimeUnit.Nanosecond));
        var decimalType = (Decimal128Type)stream.Schema.FieldsList[3].DataType;
        Assert.That((decimalType.Precision, decimalType.Scale), Is.EqualTo((20, 2)));

        using var first = (await stream.ReadNextRecordBatchAsync())!;
        Assert.That(input.BytesRead, Is.LessThan(Encoding.UTF8.GetByteCount(Fixture)));
        using var empty = (await stream.ReadNextRecordBatchAsync())!;
        Assert.That(empty.Length, Is.Zero);
        Assert.That(await stream.ReadNextRecordBatchAsync(), Is.Null);
        Assert.That(await stream.ReadNextRecordBatchAsync(), Is.Null);
        stream.Dispose();
        Assert.That(input.Disposed, Is.False);
        Assert.That(first.Length, Is.EqualTo(2));
        Assert.That(((UInt64Array)first.Column(0)).GetValue(0), Is.EqualTo(ulong.MaxValue));
        Assert.That(((StringArray)first.Column(1)).GetString(0), Is.EqualTo("café"));
        Assert.That(first.Column(1).IsNull(1), Is.True);
        Assert.That(((TimestampArray)first.Column(2)).Values[0], Is.EqualTo(1000000001L));
        Assert.That(((Decimal128Array)first.Column(3)).GetValue(0), Is.EqualTo(-123.45m));
    }

    [Test]
    public async Task ReadsSchemaOnlyFileViaBothOverloads()
    {
        var file = Path.GetTempFileName();
        try
        {
            File.WriteAllText(file, """{"schema":{"fields":[{"name":"flag","nullable":false,"type":{"name":"bool"}}]}}""");
            var reader = new ArrowIntegrationJsonReader();
            using var synchronous = reader.ToArrowStream(file);
            using var asynchronous = await reader.ToArrowStreamAsync(file);
            Assert.That(synchronous.Schema.FieldsList.Single().DataType, Is.SameAs(BooleanType.Default));
            Assert.That(asynchronous.Schema.FieldsList.Single().DataType, Is.SameAs(BooleanType.Default));
            Assert.That(await asynchronous.ReadNextRecordBatchAsync(), Is.Null);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Test]
    public async Task ReleasesOwnedInputWhenSchemaReadIsCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        using var input = new AsyncOnlyInput(Fixture, 10, cancellation);
        try
        {
            using var stream = await new ArrowIntegrationJsonReader(leaveOpen: false).ToArrowStreamAsync(input, cancellation.Token);
            Assert.Fail("Expected cancellation.");
        }
        catch (OperationCanceledException exception)
        {
            Assert.That(exception.CancellationToken, Is.EqualTo(cancellation.Token));
        }

        Assert.That(input.Disposed, Is.True);
    }

    [Test]
    public void RejectsUnsupportedTypesAndMalformedBatches()
    {
        var reader = new ArrowIntegrationJsonReader();
        using var unsupported = Input(Fixture.Replace("timestamp", "struct", StringComparison.Ordinal));
        Assert.That(() => reader.ToArrowStream(unsupported), Throws.TypeOf<NotSupportedException>().With.Message.Contains("at"));

        using var malformed = Input(Fixture.Replace("[0,5,5]", "[0,4,5]", StringComparison.Ordinal));
        Assert.That(() => reader.ToArrowBatches(malformed).First(), Throws.TypeOf<InvalidDataException>().With.Message.Contains("OFFSET"));
    }

    [Test]
    public void ReadsABatchThatCrossesBuffers()
    {
        const int length = 17000;
        var text = new string('x', length);
        using var input = Input(Fixture.Replace("caf\\u00e9", text, StringComparison.Ordinal)
            .Replace("[0,5,5]", $"[0,{length},{length}]", StringComparison.Ordinal));
        using var batch = new ArrowIntegrationJsonReader().ToArrowBatches(input).First();
        Assert.That(((StringArray)batch.Column(1)).GetString(0), Is.EqualTo(text));
    }

    private static MemoryStream Input(string json) => new(Encoding.UTF8.GetBytes(json));
}
