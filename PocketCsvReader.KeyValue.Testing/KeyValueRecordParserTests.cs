using PocketCsvReader.KeyValue.Configuration;
using NUnit.Framework;

namespace PocketCsvReader.KeyValue.Testing;

public class KeyValueRecordParserTests
{
    [Test]
    public void Ltsv_ParsesLabelsAndValues()
    {
        const string record = "time:2026-09-29T12:00:00Z\tmessage:Login succeeded: user Ada\tempty:";

        var fields = KeyValueRecordParser.Parse(record, KeyValueFormat.Ltsv);

        Assert.Multiple(() =>
        {
            Assert.That(fields, Has.Length.EqualTo(3));
            Assert.That(SliceLabel(record, fields[0]), Is.EqualTo("time"));
            Assert.That(SliceValue(record, fields[0]), Is.EqualTo("2026-09-29T12:00:00Z"));
            Assert.That(SliceLabel(record, fields[1]), Is.EqualTo("message"));
            Assert.That(SliceValue(record, fields[1]), Is.EqualTo("Login succeeded: user Ada"));
            Assert.That(SliceValue(record, fields[2]), Is.Empty);
        });
    }

    [TestCase("missing-separator")]
    [TestCase(":missing-label")]
    [TestCase("bad label:value")]
    [TestCase("valid:value\t")]
    public void Ltsv_RejectsInvalidRecords(string record)
        => Assert.Throws<InvalidDataException>(() => KeyValueRecordParser.Parse(record, KeyValueFormat.Ltsv));

    [Test]
    public void Logfmt_ParsesReferenceGrammarForms()
    {
        const string record = "foo=bar a=14 baz=\"hello kitty\" cool%story=bro f %^asdf empty=";

        var fields = KeyValueRecordParser.Parse(record, KeyValueFormat.Logfmt);

        Assert.Multiple(() =>
        {
            Assert.That(fields, Has.Length.EqualTo(7));
            Assert.That(SliceLabel(record, fields[2]), Is.EqualTo("baz"));
            Assert.That(SliceValue(record, fields[2]), Is.EqualTo("hello kitty"));
            Assert.That(fields[2].Value.WasQuoted, Is.True);
            Assert.That(SliceLabel(record, fields[3]), Is.EqualTo("cool%story"));
            Assert.That(SliceValue(record, fields[4]), Is.Empty);
            Assert.That(SliceValue(record, fields[5]), Is.Empty);
            Assert.That(SliceValue(record, fields[6]), Is.Empty);
        });
    }

    [Test]
    public void Logfmt_DecodesEscapedQuotedValue()
    {
        const string record = "message=\"line\\nquote \\\"ok\\\" \\\\ done\"";

        var field = KeyValueRecordParser.Parse(record, KeyValueFormat.Logfmt).Single();

        Assert.Multiple(() =>
        {
            Assert.That(field.Value.IsEscaped, Is.True);
            Assert.That(field.DecodedValue, Is.EqualTo("line\nquote \"ok\" \\ done"));
        });
    }

    [TestCase("message=\"unterminated")]
    [TestCase("message=\"value\"suffix")]
    [TestCase("=value")]
    [TestCase("message=raw=value")]
    [TestCase("message=\"bad\\q\"")]
    public void Logfmt_RejectsInvalidRecords(string record)
        => Assert.Throws<InvalidDataException>(() => KeyValueRecordParser.Parse(record, KeyValueFormat.Logfmt));

    private static string SliceLabel(string record, FieldSpan field)
        => record.AsSpan(field.Label.Start, field.Label.Length).ToString();

    private static string SliceValue(string record, FieldSpan field)
        => record.AsSpan(field.Value.Start, field.Value.Length).ToString();
}
