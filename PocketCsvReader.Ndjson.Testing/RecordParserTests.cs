using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using PocketCsvReader.Ndjson.Configuration;

namespace PocketCsvReader.Ndjson.Testing;
public class RecordParserTest
{
    [Test]
    [TestCase("{\"foo\": \"bar\"}")]
    [TestCase("{\"foo\": true}")]
    [TestCase("{\"foo\": \"bar\"}\r\n")]
    [TestCase("{\"foo\": true}\r\n")]
    public void ReadNextRecord_SingleField_CorrectParsing(string record)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record));

        using var reader = new RecordParser(new StreamReader(buffer), NdjsonProfile.Default, ArrayPool<char>.Create(256, 5));
        reader.IsEndOfFile(out var values, out _);
        Assert.That(values.FieldSpans, Has.Length.EqualTo(1));
        Assert.That(values.Slice(0).ToString(), Is.EqualTo("bar").Or.EqualTo("true"));
    }

    [Test]
    [TestCase("{\"foo\": \"123\", \"bar\": 456}")]
    [TestCase("{\"foo\": 123, \"bar\": 456}")]
    [TestCase("{\"foo\": \"123\", \"bar\": \"456\"}")]
    [TestCase("{\"foo\": 123, \"bar\": \"456\"}")]
    [TestCase("{\"foo\": \"123\", \"bar\": 456}\r\n")]
    [TestCase("{\"foo\": 123, \"bar\": 456}\r\n")]
    [TestCase("{\"foo\": \"123\", \"bar\": \"456\"}\r\n")]
    [TestCase("{\"foo\": 123, \"bar\": \"456\"}\r\n")]
    public void ReadNextRecord_TwoFields_CorrectParsing(string record)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record));

        using var reader = new RecordParser(new StreamReader(buffer), NdjsonProfile.Default, ArrayPool<char>.Create(256, 5));
        reader.IsEndOfFile(out var values, out _);
        Assert.That(values.FieldSpans, Has.Length.EqualTo(2));
        Assert.That(values.Slice(0).ToString(), Is.EqualTo("123"));
        Assert.That(values.Slice(1).ToString(), Is.EqualTo("456"));
    }

    [Test]
    public void ReadNextRecord_ArrayField_CorrectParsing()
    {
        const string record = "{\"foo\": [10, 25, 36], \"bar\": true}";
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record));

        using var reader = new RecordParser(new StreamReader(buffer), NdjsonProfile.Default, ArrayPool<char>.Create(256, 5));
        reader.IsEndOfFile(out var values, out _);

        Assert.That(values.FieldSpans, Has.Length.EqualTo(2));
        Assert.That(values.FieldSpans[0].Children, Has.Length.EqualTo(3));
        Assert.That(values.Slice(0).ToString(), Is.EqualTo("10, 25, 36"));
        Assert.That(values.Slice(1).ToString(), Is.EqualTo("true"));
    }

    [Test]
    public void ReadNextRecord_NestedObjects_PreservesChildren()
    {
        const string record = "{\"user\":{\"name\":\"Ada\",\"details\":{\"active\":true,\"scores\":[10,20]},\"nickname\":null}}";
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record));

        using var reader = new RecordParser(new StreamReader(buffer), NdjsonProfile.Default, ArrayPool<char>.Create(256, 5));
        reader.IsEndOfFile(out var values, out _);

        Assert.That(values.FieldSpans, Has.Length.EqualTo(1));
        var user = values.FieldSpans[0];
        Assert.That(user.Children, Has.Length.EqualTo(3));
        Assert.That(values.Span.Slice(user.Children![0].Label.Start, user.Children[0].Label.Length).ToString(), Is.EqualTo("name"));
        Assert.That(values.Span.Slice(user.Children[0].Value.Start, user.Children[0].Value.Length).ToString(), Is.EqualTo("Ada"));

        var details = user.Children[1];
        Assert.That(details.Children, Has.Length.EqualTo(2));
        Assert.That(values.Span.Slice(details.Children![0].Value.Start, details.Children[0].Value.Length).ToString(), Is.EqualTo("true"));
        Assert.That(details.Children[1].Children, Has.Length.EqualTo(2));
        Assert.That(values.Span.Slice(user.Children[2].Value.Start, user.Children[2].Value.Length).ToString(), Is.EqualTo("null"));
    }

    [TestCase("42", "42")]
    [TestCase("true", "true")]
    [TestCase("null", "null")]
    [TestCase("\"Ada\"", "Ada")]
    public void ReadNextRecord_PrimitiveRoot_ExposesSingleField(string record, string expected)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record));

        using var reader = new RecordParser(new StreamReader(buffer), NdjsonProfile.Default, ArrayPool<char>.Create(256, 5));
        reader.IsEndOfFile(out var values, out _);

        Assert.That(values.FieldSpans, Has.Length.EqualTo(1));
        Assert.That(values.Slice(0).ToString(), Is.EqualTo(expected));
        Assert.That(values.FieldSpans[0].Label.Length, Is.Zero);
    }

    [Test]
    public void ReadNextRecord_ArrayRoot_ExposesSingleArrayField()
    {
        const string record = "[10,20,30]";
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record));

        using var reader = new RecordParser(new StreamReader(buffer), NdjsonProfile.Default, ArrayPool<char>.Create(256, 5));
        reader.IsEndOfFile(out var values, out _);

        Assert.That(values.FieldSpans, Has.Length.EqualTo(1));
        Assert.That(values.FieldSpans[0].Children, Has.Length.EqualTo(3));
    }

    [Test]
    public void ReadNextRecord_NestedArrays_PreservesRecursiveChildren()
    {
        const string record = "{\"matrix\":[[1,2],[],[3,[4,5]]]}";
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record));

        using var reader = new RecordParser(new StreamReader(buffer), NdjsonProfile.Default, ArrayPool<char>.Create(256, 5));
        reader.IsEndOfFile(out var values, out _);

        var matrix = values.FieldSpans[0];
        Assert.That(matrix.Children, Has.Length.EqualTo(3));
        Assert.That(matrix.Children![0].Children, Has.Length.EqualTo(2));
        Assert.That(matrix.Children[1].Children, Is.Empty);
        Assert.That(matrix.Children[2].Children, Has.Length.EqualTo(2));
        Assert.That(matrix.Children[2].Children![1].Children, Has.Length.EqualTo(2));
    }

    [Test]
    public void ReadNextRecord_ArrayOfObjects_PreservesObjectFields()
    {
        const string record = "{\"users\":[{\"name\":\"Ada\",\"roles\":[\"admin\"]},{\"name\":\"Grace\"}]}";
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record));

        using var reader = new RecordParser(new StreamReader(buffer), NdjsonProfile.Default, ArrayPool<char>.Create(256, 5));
        reader.IsEndOfFile(out var values, out _);

        var users = values.FieldSpans[0].Children;
        Assert.That(users, Has.Length.EqualTo(2));
        Assert.That(users![0].Children, Has.Length.EqualTo(2));
        Assert.That(users[1].Children, Has.Length.EqualTo(1));
        var ada = users[0].Children!;
        var grace = users[1].Children!;
        Assert.That(values.Span.Slice(ada[0].Value.Start, ada[0].Value.Length).ToString(), Is.EqualTo("Ada"));
        Assert.That(ada[1].Children, Has.Length.EqualTo(1));
        Assert.That(values.Span.Slice(grace[0].Value.Start, grace[0].Value.Length).ToString(), Is.EqualTo("Grace"));
    }

    [TestCase("{}")]
    [TestCase("{   }")]
    public void ReadNextRecord_EmptyObject_ReturnsRecordWithNoFields(string record)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record));

        using var reader = new RecordParser(new StreamReader(buffer), NdjsonProfile.Default, ArrayPool<char>.Create(256, 5));
        var isEof = reader.IsEndOfFile(out var values, out var state);

        Assert.That(isEof, Is.True);
        Assert.That(state, Is.EqualTo(RecordState.Record));
        Assert.That(values.FieldSpans, Is.Empty);
    }

    [Test]
    public void ReadNextRecord_NestedEmptyObject_PreservesEmptyChildren()
    {
        const string record = "{\"value\":{},\"items\":[{},{}]}";
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record));

        using var reader = new RecordParser(new StreamReader(buffer), NdjsonProfile.Default, ArrayPool<char>.Create(256, 5));
        reader.IsEndOfFile(out var values, out _);

        Assert.That(values.FieldSpans[0].Children, Is.Empty);
        var items = values.FieldSpans[1].Children;
        Assert.That(items, Has.Length.EqualTo(2));
        Assert.That(items![0].Children, Is.Empty);
        Assert.That(items[1].Children, Is.Empty);
    }

    [Test]
    public void ReadNextRecord_TabsAroundTokens_ParsesAllValues()
    {
        const string record = "\t{\t\"first\"\t:\t1\t,\t\"array\"\t:\t[\t2\t,\t3\t]\t,\t\"empty\"\t:\t{\t}\t}\t";
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record));

        using var reader = new RecordParser(new StreamReader(buffer), NdjsonProfile.Default, ArrayPool<char>.Create(256, 5));
        reader.IsEndOfFile(out var values, out _);

        Assert.That(values.FieldSpans, Has.Length.EqualTo(3));
        Assert.That(values.Slice(0).ToString(), Is.EqualTo("1"));
        Assert.That(values.FieldSpans[1].Children, Has.Length.EqualTo(2));
        Assert.That(values.FieldSpans[2].Children, Is.Empty);
    }
}

