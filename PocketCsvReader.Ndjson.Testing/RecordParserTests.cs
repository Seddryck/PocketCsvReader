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
}

