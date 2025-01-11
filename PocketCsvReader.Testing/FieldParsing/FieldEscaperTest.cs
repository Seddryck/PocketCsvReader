using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using PocketCsvReader.FieldParsing;

namespace PocketCsvReader.Testing.FieldParsing;
public class FieldEscaperTest
{
    [Test]
    [TestCase("foo", "foo")]
    public void ReadField_NotQualified_CorrectString(string item, string result)
    {
        Span<char> buffer = stackalloc char[64];
        item.AsSpan().CopyTo(buffer);

        var profile = new CsvProfile(';', '\'', '`', "\r\n", false, false, 4096, "(empty)", "(null)");
        var escaper = new FieldEscaper(profile);
        var value = escaper.Escape(buffer.Slice(0, item.Length));
        Assert.That(value.ToString(), Is.EqualTo(result));
    }

    [TestCase("`a`", "a")]
    [TestCase("`foo`", "foo")]
    [TestCase("`foo bar`", "foo bar")]
    public void ReadField_Qualified_CorrectString(string item, string result)
    {
        Span<char> buffer = stackalloc char[64];
        item.AsSpan().CopyTo(buffer);

        var profile = new CsvProfile(';', '`', '\\', "\r\n", false, false, 4096, "?", "(null)");
        var escaper = new FieldEscaper(profile);
        var value = escaper.Escape(buffer.Slice(1, item.Length - 2));
        Assert.That(value.ToString(), Is.EqualTo(result));
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
        var escaper = new FieldEscaper(profile);
        var value = escaper.Escape(buffer.Slice(1, item.Length - 2));
        Assert.That(value.ToString(), Is.EqualTo(result));
    }

    [Test]
    [TestCase("'a`'b'", "a'b")]
    [TestCase("'`'a`'b`''", "'a'b'")]
    public void ReadField_EscapedWithOtherChar_CorrectString(string item, string result)
    {
        Span<char> buffer = stackalloc char[64];
        item.AsSpan().CopyTo(buffer);

        var profile = new CsvProfile(';', '\'', '`', "\r\n", false, false, 4096, "(empty)", "(null)");
        var escaper = new FieldEscaper(profile);
        var value = escaper.Escape(buffer.Slice(1, item.Length - 2));
        Assert.That(value.ToString(), Is.EqualTo(result));
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
        var escaper = new FieldEscaper(profile);
        var value = escaper.Escape(buffer.Slice(1, item.Length - 2));
        Assert.That(value.ToString(), Is.EqualTo(result));
    }
}
