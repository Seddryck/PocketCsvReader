using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace PocketCsvReader.Testing;
public class RecordParserTTest
{
    [Test]
    [TestCase("foo")]
    [TestCase("'foo'")]
    [TestCase("foo\r\n")]
    [TestCase("'foo'\r\n")]
    public void ReadNextRecord_SingleField_CorrectParsing(string record)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record));

        var profile = new CsvProfile(';', '\'', '\'', "\r\n", false, true, 4096, string.Empty, string.Empty);
        var spanMapper = new SpanMapper<string>((span, fieldSpans) => span.Slice(fieldSpans.First().ValueStart, fieldSpans.First().ValueLength).ToString());
        using var reader = new RecordParser<string>(new StreamReader(buffer), profile, spanMapper, ArrayPool<char>.Create(256, 5));
        var eof = reader.IsEndOfFile(out var value);
        Assert.That(value, Is.EqualTo("foo"));
    }

    private record struct Employee(string Name, int Age);

    [TestCase("foo;16\r\n", "foo", 16)]
    [TestCase("'foo';16\r\n", "foo", 16)]
    public void ReadNextRecord_RecordWithLineTerminator_CorrectParsing(string record, string name, int age)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record));

        var profile = new CsvProfile(';', '\'', '\'', "\r\n", false, false, 4096, "(empty)", "(null)");
        var spanMapper = new SpanMapper<Employee>((span, fieldSpans) =>
        {
            return new Employee(
                span.Slice(fieldSpans.First().ValueStart, fieldSpans.First().ValueLength).ToString(),
                int.Parse(span.Slice(fieldSpans.Last().ValueStart, fieldSpans.Last().ValueLength).ToString()));
        });
        using var reader = new RecordParser<Employee>(new StreamReader(buffer), profile, spanMapper, ArrayPool<char>.Create(256, 5));
        reader.IsEndOfFile(out var value);
        Assert.That(value, Is.TypeOf<Employee>());
        Assert.That(value.Name, Is.EqualTo(name));
        Assert.That(value.Age, Is.EqualTo(age));
    }

    [TestCase("foo;16\r\nbar;18")]
    [TestCase("'foo';16\r\nbar;'18'")]
    public void ReadNextRecord_TwoRecords_CorrectParsing(string record)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record));

        var profile = new CsvProfile(';', '\'', '\'', "\r\n", false, false, 4096, "(empty)", "(null)");
        var spanMapper = new SpanMapper<Employee>((span, fieldSpans) =>
        {
            return new Employee(
                span.Slice(fieldSpans.First().ValueStart, fieldSpans.First().ValueLength).ToString(),
                int.Parse(span.Slice(fieldSpans.Last().ValueStart, fieldSpans.Last().ValueLength).ToString()));
        });
        using var reader = new RecordParser<Employee>(new StreamReader(buffer), profile, spanMapper, ArrayPool<char>.Create(256, 5));
        reader.IsEndOfFile(out var value);
        Assert.That(value, Is.TypeOf<Employee>());
        Assert.That(value.Name, Is.EqualTo("foo"));
        Assert.That(value.Age, Is.EqualTo(16));
        reader.IsEndOfFile(out value);
        Assert.That(value, Is.TypeOf<Employee>());
        Assert.That(value.Name, Is.EqualTo("bar"));
        Assert.That(value.Age, Is.EqualTo(18));
    }

    private record struct Human(string Name, bool IsAdult);

    [TestCase("foo;22\r\nbar;26")]
    public void ReadNextRecord_LogicBasedRecord_CorrectParsing(string record)
    {
        var buffer = new MemoryStream(Encoding.UTF8.GetBytes(record));

        var profile = new CsvProfile(';', '\'', '\'', "\r\n", false, false, 4096, "(empty)", "(null)");
        var spanMapper = new SpanMapper<Human>((span, fieldSpans) =>
        {
            return new Human(
                span.Slice(fieldSpans.First().ValueStart, fieldSpans.First().ValueLength).ToString(),
                int.Parse(span.Slice(fieldSpans.Last().ValueStart, fieldSpans.Last().ValueLength).ToString())>18);
        });
        using var reader = new RecordParser<Human>(new StreamReader(buffer), profile, spanMapper, ArrayPool<char>.Create(256, 5));
        reader.IsEndOfFile(out var value);
        Assert.That(value, Is.TypeOf<Human>());
        Assert.That(value.Name, Is.EqualTo("foo"));
        Assert.That(value.IsAdult, Is.True);
        reader.IsEndOfFile(out value);
        Assert.That(value, Is.TypeOf<Human>());
        Assert.That(value.Name, Is.EqualTo("bar"));
        Assert.That(value.IsAdult, Is.True);
    }
}
