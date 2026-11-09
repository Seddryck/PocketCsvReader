using System.Text;
using Apache.Arrow;
using NUnit.Framework;

namespace PocketCsvReader.Arrow.ColumnarJson.Testing;

public class LazyIntegrationJsonTests
{
    [Test]
    public async Task RetainedFileBatchesReadColumnsIndependentlyOfTheScanningCursor()
    {
        var path = Path.GetTempFileName();
        var text = new string('é', 6000) + "😀";
        try
        {
            var json = """
                {
                  "schema":{"fields":[
                    {"name":"text","nullable":false,"type":{"name":"utf8"}},
                    {"name":"id","nullable":false,"type":{"name":"int","bitWidth":32,"isSigned":true}}
                  ]},
                  "batches":[
                    {"count":1,"columns":[
                      {"name":"text","count":1,"VALIDITY":[1],"OFFSET":[0,12004],"DATA":["FIRST_VALUE"]},
                      {"name":"id","count":1,"VALIDITY":[1],"DATA":[41]}
                    ]},
                    {"count":1,"columns":[
                      {"name":"text","count":1,"VALIDITY":[1],"OFFSET":[0,12005],"DATA":["SECOND_VALUE"]},
                      {"name":"id","count":1,"VALIDITY":[1],"DATA":[42]}
                    ]}
                  ]
                }
                """.Replace("FIRST_VALUE", text, StringComparison.Ordinal)
                .Replace("SECOND_VALUE", text + "!", StringComparison.Ordinal);
            File.WriteAllText(path, json, new UTF8Encoding(true));
            using var stream = await new ArrowIntegrationJsonReader().ToLazyArrowStreamAsync(path);
            using var first = await stream.ReadNextRecordAsync() ?? throw new InvalidOperationException("Expected the first batch.");
            Assert.That(((StringArray)first.Column(0)).GetString(0), Is.EqualTo(text));

            using var second = await stream.ReadNextRecordAsync() ?? throw new InvalidOperationException("Expected the second batch.");
            Assert.That(await stream.ReadNextRecordAsync(), Is.Null);
            stream.Dispose();
            File.Delete(path);

            var columns = await Task.WhenAll(Task.Run(() => first.Column(1)), Task.Run(() => second.Column(1)));
            Assert.That(((Int32Array)columns[0]).GetValue(0), Is.EqualTo(41));
            Assert.That(((Int32Array)columns[1]).GetValue(0), Is.EqualTo(42));
            first.Dispose();
            Assert.That(((StringArray)second.Column(0)).GetString(0), Is.EqualTo(text + "!"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [TestCase("1", "3", "OFFSET entries")]
    [TestCase("2", "2", "VALIDITY entries")]
    [TestCase("1", "2,3", "Too many OFFSET entries")]
    public async Task FileIndexDefersBufferErrorsUntilAccess(string validity, string offsets, string message)
    {
        var path = Path.GetTempFileName();
        try
        {
            var json = $$$"""
                {"schema":{"fields":[{"name":"text","nullable":false,"type":{"name":"utf8"}}]},
                 "batches":[{"count":1,"columns":[
                     {"DATA":["é"],"OFFSET":[0,{{{offsets}}}],"VALIDITY":[{{{validity}}}],"count":1,"name":"text"}
                 ]}]}
                """;
            File.WriteAllText(path, json, new UTF32Encoding(true, true));
            using var stream = await new ArrowIntegrationJsonReader().ToLazyArrowStreamAsync(path);
            using var record = await stream.ReadNextRecordAsync() ?? throw new InvalidOperationException("Expected a batch.");
            Assert.That(record.Length, Is.EqualTo(1));
            var error = Assert.Throws<InvalidDataException>(() => record.Column(0));
            Assert.That(error?.Message, Does.Contain(message));
            Assert.That(Assert.Throws<InvalidDataException>(() => record.Column(0)), Is.SameAs(error));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public async Task RejectsAnInflatedDeclaredRowCountWhenTheColumnIsAccessed()
    {
        const string json = """
            {
              "schema":{"fields":[{"name":"id","nullable":false,"type":{"name":"int","bitWidth":32,"isSigned":true}}]},
              "batches":[{"count":2147483647,"columns":[{"name":"id","count":2147483647,"VALIDITY":[1],"DATA":[42]}]}]
            }
            """;
        using var input = new AsyncOnlyInput(json, 13);
        using var stream = await new ArrowIntegrationJsonReader().ToLazyArrowStreamAsync(input);
        using var record = await stream.ReadNextRecordAsync() ?? throw new InvalidOperationException("Expected a batch.");

        Assert.That(() => record.Column(0), Throws.TypeOf<InvalidDataException>().With.Message.Contains("does not contain"));
    }

    [Test]
    public async Task RetainsBatchesAcrossReadsAndDecodesEachColumnOnFirstAccess()
    {
        const string json = """
            {
              "schema":{"fields":[
                {"name":"id","nullable":false,"type":{"name":"int","bitWidth":32,"isSigned":true}},
                {"name":"unused","nullable":false,"type":{"name":"int","bitWidth":32,"isSigned":true}}
              ]},
              "batches":[
                {"count":1,"columns":[
                  {"name":"id","count":1,"VALIDITY":[1],"DATA":[42]},
                  {"name":"unused","count":1,"VALIDITY":[1],"DATA":["invalid"]}
                ]},
                {"count":0,"columns":[
                  {"name":"id","count":0,"VALIDITY":[],"DATA":[]},
                  {"name":"unused","count":0,"VALIDITY":[],"DATA":[]}
                ]}
              ]
            }
            """;
        using var input = new AsyncOnlyInput(json, 13);
        using var stream = await new ArrowIntegrationJsonReader(leaveOpen: false).ToLazyArrowStreamAsync(input);
        using var first = await stream.ReadNextRecordAsync() ?? throw new InvalidOperationException("Expected a batch.");
        using var empty = await stream.ReadNextRecordAsync() ?? throw new InvalidOperationException("Expected an empty batch.");
        Assert.That(await stream.ReadNextRecordAsync(), Is.Null);
        stream.Dispose();

        Assert.That(input.Disposed, Is.True);
        Assert.That(first.Schema, Is.SameAs(stream.Schema));
        Assert.That(first.ColumnCount, Is.EqualTo(2));
        Assert.That(empty.Length, Is.Zero);
        Assert.That(empty.Column(0).Length, Is.Zero);
        var id = (Int32Array)first.Column("id");
        Assert.That(id.GetValue(0), Is.EqualTo(42));
        Assert.That(first.Column(0), Is.SameAs(id));
        Assert.That(() => first.Column("unused"), Throws.TypeOf<InvalidDataException>().With.Message.Contains("unused"));
    }
}
