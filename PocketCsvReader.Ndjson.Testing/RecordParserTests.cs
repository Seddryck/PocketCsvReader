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
        reader.ReadNextRecord(out var values);
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
        reader.ReadNextRecord(out var values);
        Assert.That(values.FieldSpans, Has.Length.EqualTo(2));
        Assert.That(values.Slice(0).ToString(), Is.EqualTo("123"));
        Assert.That(values.Slice(1).ToString(), Is.EqualTo("456"));
    }
}

