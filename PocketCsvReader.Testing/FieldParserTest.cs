using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace PocketCsvReader.Testing;
public class FieldParserTest
{
    [Test]
    [TestCase("foo", "foo")]
    public void ReadField_NotQualified_CorrectString(string item, string result)
    {
        Span<char> buffer = stackalloc char[64];
        item.AsSpan().CopyTo(buffer);

        var profile = new CsvProfile(';', '\'', '`', "\r\n", false, false, 4096, "(empty)", "(null)");
        var reader = new FieldParser(profile);
        var value = reader.ReadField(buffer, 0, item.Length, false, false);
        Assert.That(value, Is.EqualTo(result));
    }

    [Test]
    [TestCase("", "?")]
    public void ReadField_Empty_CorrectString(string item, string result)
    {
        Span<char> buffer = stackalloc char[64];
        item.AsSpan().CopyTo(buffer);

        var profile = new CsvProfile(';', '\'', '`', "\r\n", false, false, 4096, "?", "(null)");
        var reader = new FieldParser(profile);
        var value = reader.ReadField(buffer, 0, item.Length, false, false);
        Assert.That(value, Is.EqualTo(result));
    }

    [Test]
    [TestCase("(null)", null)] //Parse (null) to a real null value
    [TestCase("\"(null)\"", "(null)")] //Explicitly quoted (null) should be (null)
    public void ReadField_Null_CorrectString(string item, string result)
    {
        Span<char> buffer = stackalloc char[64];
        item.AsSpan().CopyTo(buffer);

        var profile = new CsvProfile(new CsvDialectDescriptor { NullSequence = "(null)" });
        var reader = new FieldParser(profile);
        var value = reader.ReadField(buffer, 0, item.Length, item.StartsWith("\""), item.StartsWith("\""));
        Assert.That(value, Is.EqualTo(result));
    }

    [TestCase("`a`", "a")]
    [TestCase("`foo`", "foo")]
    [TestCase("`foo bar`", "foo bar")]
    [TestCase("``", "?")]
    public void ReadField_Qualified_CorrectString(string item, string result)
    {
        Span<char> buffer = stackalloc char[64];
        item.AsSpan().CopyTo(buffer);

        var profile = new CsvProfile(';', '`', '\\', "\r\n", false, false, 4096, "?", "(null)");
        var reader = new FieldParser(profile);
        var value = reader.ReadField(buffer, 0, item.Length, true, true);
        Assert.That(value, Is.EqualTo(result));
    }

    [TestCase("`foo`", "foo")]
    [TestCase("``", "")]
    public void ReadFieldWithoutHandleSpecialValues_Qualified_CorrectString(string item, string result)
    {
        Span<char> buffer = stackalloc char[64];
        item.AsSpan().CopyTo(buffer);

        var profile = new CsvProfile(';', '`', '\\', "\r\n", false, false, 4096, "?", "(null)")
        {
            ParserOptimizations = new ParserOptimizationOptions { HandleSpecialValues = false }
        };
        var reader = new FieldParser(profile);
        var value = reader.ReadField(buffer, 0, item.Length, true, true);
        Assert.That(value, Is.EqualTo(result));
    }

    [Test]
    [TestCase("'a`'b'", "a'b")]
    [TestCase("'`'a`'b`''", "'a'b'")]
    public void ReadField_EscapedWithOtherChar_CorrectString(string item, string result)
    {
        Span<char> buffer = stackalloc char[64];
        item.AsSpan().CopyTo(buffer);

        var profile = new CsvProfile(';', '\'', '`', "\r\n", false, false, 4096, "(empty)", "(null)");
        var reader = new FieldParser(profile);
        var value = reader.ReadField(buffer, 0, item.Length, true, true);
        Assert.That(value, Is.EqualTo(result));
    }

    [Test]
    [TestCase("'a`'b'", "a`'b")]
    [TestCase("'`'a`'b`''", "`'a`'b`'")]
    public void ReadFieldWithoutUnescapeChars_EscapedWithOtherChar_CorrectString(string item, string result)
    {
        Span<char> buffer = stackalloc char[64];
        item.AsSpan().CopyTo(buffer);

        var profile = new CsvProfile(';', '\'', '`', "\r\n", false, false, 4096, "(empty)", "(null)");
        profile.ParserOptimizations = new ParserOptimizationOptions { UnescapeChars = false };
        var reader = new FieldParser(profile);
        var value = reader.ReadField(buffer, 0, item.Length, true, true);
        Assert.That(value, Is.EqualTo(result));
    }

    [Test]
    [TestCase("\"")]
    [TestCase("\"a")]
    public void ReadField_ContainsQualifierChar_CorrectString(string item)
    {
        var profile = new CsvProfile(';', '\"', '\"', "\r\n", false, false, 4096, "(empty)", "(null)");
        var reader = new FieldParser(profile);
        var value =
        Assert.Throws<InvalidDataException>(() =>
        {
            Span<char> buffer = stackalloc char[64];
            item.AsSpan().CopyTo(buffer);
            reader.ReadField(buffer, 0, item.Length, false, false);
        });
    }

    [TestCase("\"ab\"", "ab")]
    [TestCase("\"abc\"", "abc")]
    [TestCase("\"a\"\"b\"", "a\"b")]
    [TestCase("\"\"\"a\"\"b\"\"\"", "\"a\"b\"")]
    public void ReadField_EscapedWithDoubleChar_CorrectString(string item, string result)
    {
        Span<char> buffer = stackalloc char[64];
        item.AsSpan().CopyTo(buffer);

        var profile = new CsvProfile(';', '\"', '\"', "\r\n", false, false, 4096, "(empty)", "(null)");
        var reader = new FieldParser(profile);
        var value = reader.ReadField(buffer, 0, item.Length, true, true);
        Assert.That(value, Is.EqualTo(result));
    }

    [Test]
    public void ReadField_StringPool_CorrectString()
    {
        Span<char> buffer = stackalloc char[64];
        "foo".AsSpan().CopyTo(buffer);

        var stringPool = new CommunityToolkit.HighPerformance.Buffers.StringPool();

        var profile = new CsvProfile(';', '\"', '\"', "\r\n", false, false, 4096, "(empty)", "(null)")
        {
            ParserOptimizations = new ParserOptimizationOptions { PoolString = stringPool.GetOrAdd }
        };
        var reader = new FieldParser(profile);
        var value = reader.ReadField(buffer, 0, "foo".Length, false, false);
        Assert.That(value, Is.EqualTo("foo"));
    }


    [Test]
    public void ReadField_StringPool_Called()
    {
        Span<char> buffer = stackalloc char[64];
        "foo".AsSpan().CopyTo(buffer);
        int count = 0;

        PoolString poolString = (span) =>
        {
            count++;
            return span.ToString();
        };

        var profile = new CsvProfile(';', '\"', '\"', "\r\n", false, false, 4096, "(empty)", "(null)")
        {
            ParserOptimizations = new ParserOptimizationOptions { PoolString = poolString }
        }; 
        var reader = new FieldParser(profile);
        var value = reader.ReadField(buffer, 0, "foo".Length, false, false);
        Assert.That(value, Is.EqualTo("foo"));
        Assert.That(count, Is.EqualTo(1));
    }

    [TestCase(@"\N", @"\N")]
    [TestCase("(null)", "(null)")]
    [TestCase("null", "null")]
    public void ReadField_NullSequence_NullValue(string field, string NullSequence)
    {
        Span<char> buffer = stackalloc char[64];
        field.AsSpan().CopyTo(buffer);

        var profile = new CsvProfile(new CsvDialectDescriptor { NullSequence = NullSequence });
        
        var reader = new FieldParser(profile);
        var value = reader.ReadField(buffer, 0, field.Length, false, false);
        Assert.That(value, Is.Null);
    }

    [TestCase("(null)", @"\N")]
    [TestCase(@"\N", "(null)")]
    [TestCase("null", "(null)")]
    public void ReadField_WrongNullSequence_NotNullValue(string field, string NullSequence)
    {
        Span<char> buffer = stackalloc char[64];
        field.AsSpan().CopyTo(buffer);

        var profile = new CsvProfile(new CsvDialectDescriptor { NullSequence = NullSequence });

        var reader = new FieldParser(profile);
        var value = reader.ReadField(buffer, 0, field.Length, false, false);
        Assert.That(value, Is.Not.Null);
        Assert.That(value, Is.EqualTo(field));
    }

    [TestCase("(empty)", "(empty)", "")]
    [TestCase("(ae)", "(ae)", "Albert Einstein")]
    public void ReadField_AnySequence_MappedValue(string field, string sequence, string map)
    {
        Span<char> buffer = stackalloc char[64];
        field.AsSpan().CopyTo(buffer);

        var profile = CsvProfile.CommaDoubleQuote;
        profile.Sequences.Add(sequence, map);

        var reader = new FieldParser(profile);
        var value = reader.ReadField(buffer, 0, field.Length, false, false);
        Assert.That(value, Is.Not.Null);
        Assert.That(value, Is.EqualTo(map));
    }
}
