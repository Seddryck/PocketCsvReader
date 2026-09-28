using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using PocketCsvReader.Configuration;
using PocketCsvReader.Ndjson.Configuration;

namespace PocketCsvReader.Ndjson.Testing;

[TestFixture]
public class NdjsonReaderTest
{
    [Test]
    [TestCase(@"Resources\metrics.ndjson")]
    public void ToDataReader_Metrics_Successful(string filename)
    {
        var rowCount = 0;
        var profile = new NdjsonProfile(Environment.NewLine);
        var reader = new NdjsonReader(profile).ToDataReader(filename);
        while (reader.Read())
        {
            rowCount++;
            for (var i = 0; i < reader.FieldCount; i++)
                reader.GetString(i);
        }
        Assert.That(rowCount, Is.EqualTo(7));
    }

    [Test]
    [TestCase(@"Resources\metrics.ndjson")]
    public void ToDataReader_MetricsStream_Successful(string filename)
    {
        using var stream = File.OpenRead(filename);
        var profile = new NdjsonProfile(Environment.NewLine);
        var reader = new NdjsonReader(profile).ToDataReader(filename);
        var rowCount = 0;
        while (reader.Read())
        {
            rowCount++;
            for (var i = 0; i < reader.FieldCount; i++)
                reader.GetString(i);
        }
        Assert.That(rowCount, Is.EqualTo(7));
    }

    [Test]
    public void ToDataReader_ArrayField_ReturnsTypedArray()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"values\": [10, 25, 36]}"));
        using var reader = new NdjsonReader().ToDataReader(stream);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetArray<int>(0), Is.EqualTo(new[] { 10, 25, 36 }));
    }

    [Test]
    public void ToDataReader_QuotedAndEmptyArrays_ReturnsArrays()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"values\": [\"a]b\", \"qrz\"], \"empty\": []}"));
        using var reader = new NdjsonReader().ToDataReader(stream);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetArray<string>(0), Is.EqualTo(new[] { "a]b", "qrz" }));
        Assert.That(reader.GetArray<string>(1), Is.Empty);
    }

    [Test]
    public void ToDataReader_RootValues_ExposesOrdinalZero()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("[1,2,3]\n\"Ada\"\n42\ntrue\nnull"));
        using var reader = new NdjsonReader(new NdjsonProfile("\n")).ToDataReader(stream);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetArray<int>(0), Is.EqualTo(new[] { 1, 2, 3 }));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("Ada"));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(42));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetBoolean(0), Is.True);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.IsDBNull(0), Is.True);
        Assert.That(reader.GetValue(0), Is.SameAs(DBNull.Value));
        Assert.That(reader.Read(), Is.False);
    }

    [Test]
    public void ToDataReader_EmptyObjects_ReturnsZeroFieldRecords()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{}\n{ }\n{}"));
        using var reader = new NdjsonReader(new NdjsonProfile("\n")).ToDataReader(stream);

        for (var i = 0; i < 3; i++)
        {
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.FieldCount, Is.Zero);
        }

        Assert.That(reader.Read(), Is.False);
        Assert.That(reader.RowCount, Is.EqualTo(3));
    }

    [Test]
    public void ToDataReader_JsonWhitespaceAroundTokens_ReturnsValues()
    {
        const string content = "\t{\t\"value\"\t:\t42\t,\t\"items\"\t:\t[\ttrue\t,\tfalse\t]\t}\t";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        using var reader = new NdjsonReader().ToDataReader(stream);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(reader.GetOrdinal("value")), Is.EqualTo(42));
        Assert.That(reader.GetArray<bool>(reader.GetOrdinal("items")), Is.EqualTo(new[] { true, false }));
    }

    [TestCase("\n")]
    [TestCase("\r")]
    [TestCase("\r\n")]
    [TestCase("|")]
    [TestCase("<END>")]
    public void ToDataReader_ConfiguredLineTerminator_SplitsRecords(string lineTerminator)
    {
        var content = $"{{\"value\":1}}{lineTerminator}{{\"value\":2}}{lineTerminator}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        using var reader = new NdjsonReader(new NdjsonProfile(lineTerminator)).ToDataReader(stream);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(reader.GetOrdinal("value")), Is.EqualTo(1));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(reader.GetOrdinal("value")), Is.EqualTo(2));
        Assert.That(reader.Read(), Is.False);
    }

    [Test]
    public void ToDataReader_CustomTerminatorInsideString_PreservesStringContent()
    {
        const string content = "{\"value\":\"left|quoted \\\"| right\"}|{\"value\":\"next\"}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        using var reader = new NdjsonReader(new NdjsonProfile("|")).ToDataReader(stream);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(reader.GetOrdinal("value")), Is.EqualTo("left|quoted \"| right"));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(reader.GetOrdinal("value")), Is.EqualTo("next"));
        Assert.That(reader.Read(), Is.False);
    }

    [Test]
    public void ToDataReader_CustomTerminator_PreservesJsonNewlineWhitespace()
    {
        const string content = "{\n\"value\": 1\n}<END>\r\n{\n\"value\": 2\n}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        using var reader = new NdjsonReader(new NdjsonProfile("<END>")).ToDataReader(stream);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(reader.GetOrdinal("value")), Is.EqualTo(1));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(reader.GetOrdinal("value")), Is.EqualTo(2));
        Assert.That(reader.Read(), Is.False);
    }

    [Test]
    public void ToDataReader_CrLfTerminator_DoesNotAcceptLoneLineFeed()
    {
        const string content = "{\"value\":1}\n{\"value\":2}\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        using var reader = new NdjsonReader(new NdjsonProfile("\r\n")).ToDataReader(stream);

        Assert.Throws<InvalidDataException>(() => reader.Read());
    }

    [Test]
    public void ToDataReader_MultiCharacterTerminatorAcrossBufferBoundary_SplitsRecords()
    {
        var value = new string('x', 70 * 1024);
        var content = $"{{\"value\":\"{value}\"}}<END>{{\"value\":\"done\"}}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        using var reader = new NdjsonReader(new NdjsonProfile("<END>")).ToDataReader(stream);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(reader.GetOrdinal("value")), Has.Length.EqualTo(value.Length));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(reader.GetOrdinal("value")), Is.EqualTo("done"));
        Assert.That(reader.Read(), Is.False);
    }

    [Test]
    public void ToDataReader_CustomTerminator_SkipsBlankRecordsAndReturnsFinalUnterminatedRecord()
    {
        const string content = "|  |{\"value\":1}||{\"value\":2}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        using var reader = new NdjsonReader(new NdjsonProfile("|")).ToDataReader(stream);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(reader.GetOrdinal("value")), Is.EqualTo(1));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(reader.GetOrdinal("value")), Is.EqualTo(2));
        Assert.That(reader.Read(), Is.False);
    }

    [TestCase("\n")]
    [TestCase("\r")]
    [TestCase("\r\n")]
    [TestCase("|")]
    [TestCase("<END>")]
    public void ToDataReader_ConfiguredComments_SkipsFullLineAndTrailingComments(string lineTerminator)
    {
        var content = string.Join(lineTerminator,
            "# before \"unterminated quote",
            "{\"value\":1,\"text\":\"# retained\"} # trailing \"unterminated quote",
            "# between",
            "42# trailing primitive",
            "# after");
        var profile = new NdjsonProfile(
            new NdjsonDialectDescriptorBuilder()
                .WithLineTerminator(lineTerminator)
                .WithCommentChar('#')
                .Build());
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        using var reader = new NdjsonReader(profile).ToDataReader(stream);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(reader.GetOrdinal("value")), Is.EqualTo(1));
        Assert.That(reader.GetString(reader.GetOrdinal("text")), Is.EqualTo("# retained"));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(42));
        Assert.That(reader.Read(), Is.False);
        Assert.That(reader.RowCount, Is.EqualTo(2));
    }

    [Test]
    public void ToDataReader_CommentWithoutConfiguration_IsRejected()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("# comment"));
        using var reader = new NdjsonReader().ToDataReader(stream);

        Assert.Throws<InvalidDataException>(() => reader.Read());
    }

    [Test]
    public void ToDataReader_InvalidPrefixBeforeFirstCommentMarker_IsRejected()
    {
        const string content = "{\"value\":# first # second}";
        var profile = new NdjsonProfile(
            new NdjsonDialectDescriptorBuilder()
                .WithCommentChar('#')
                .Build());
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        using var reader = new NdjsonReader(profile).ToDataReader(stream);

        Assert.Throws<InvalidDataException>(() => reader.Read());
    }

    [Test]
    public void ReaderBuilder_CommentConfiguration_IsApplied()
    {
        var reader = new NdjsonReaderBuilder()
            .WithDialect(dialect => dialect.WithCommentChar('#'))
            .Build();

        Assert.That(reader.Dialect.CommentChar, Is.EqualTo('#'));
    }
}
