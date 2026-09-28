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
}
