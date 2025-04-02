using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;
using PocketCsvReader.Ndjson.Configuration;

namespace PocketCsvReader.Ndjson.Testing;
public class NdjsonDataReaderTests
{
    [Test]
    public void Read_FirstRowByIndex_Success()
    {
        using var stream =
                    Assembly.GetExecutingAssembly()
                        .GetManifestResourceStream($"{Assembly.GetExecutingAssembly().GetName().Name}.Resources.metrics.ndjson")
                    ?? throw new FileNotFoundException();
        var dataReader = new NdjsonDataReader(stream, NdjsonProfile.Default);
        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.GetString(0), Is.EqualTo("RAMP_UP_SCORE"));
        Assert.That(dataReader.GetString(1), Is.EqualTo("clone"));
        Assert.That(dataReader.GetString(2), Is.EqualTo("src/clone_repo.ts"));
        Assert.That(dataReader.GetString(3), Is.EqualTo("335"));
    }

    [Test]
    public void Read_FirstRowByIndexTyped_Success()
    {
        using var stream =
                    Assembly.GetExecutingAssembly()
                        .GetManifestResourceStream($"{Assembly.GetExecutingAssembly().GetName().Name}.Resources.metrics.ndjson")
                    ?? throw new FileNotFoundException();
        var dataReader = new NdjsonDataReader(stream, NdjsonProfile.Default);
        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.GetString(0), Is.EqualTo("RAMP_UP_SCORE"));
        Assert.That(dataReader.GetFieldValue<string>(0), Is.EqualTo("RAMP_UP_SCORE"));
        Assert.That(dataReader.GetInt32(3), Is.EqualTo(335));
        Assert.That(dataReader.GetFieldValue<int>(3), Is.EqualTo(335));
    }

    [Test]
    public void GetOrdinal_ExistingNames_Success()
    {
        using var stream =
                    Assembly.GetExecutingAssembly()
                        .GetManifestResourceStream($"{Assembly.GetExecutingAssembly().GetName().Name}.Resources.metrics.ndjson")
                    ?? throw new FileNotFoundException();
        var dataReader = new NdjsonDataReader(stream, NdjsonProfile.Default);
        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.GetOrdinal("metric"), Is.EqualTo(0));
        Assert.That(dataReader.GetOrdinal("line"), Is.EqualTo(3));
    }

    [Test]
    public void GetOrdinal_ChangingNames_Success()
    {
        var content = "{\"foo\":123,\"bar\":true}\r\n{\"bar\":true}\r\n{\"bar\":true,\"foo\":123}\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var dataReader = new NdjsonDataReader(stream, NdjsonProfile.Default);
        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.FieldCount, Is.EqualTo(2));
        Assert.That(dataReader.GetOrdinal("foo"), Is.EqualTo(0));
        Assert.That(dataReader.GetOrdinal("bar"), Is.EqualTo(1));
        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.FieldCount, Is.EqualTo(1));
        Assert.Throws<ArgumentOutOfRangeException>(() => dataReader.GetOrdinal("foo"));
        Assert.That(dataReader.GetOrdinal("bar"), Is.EqualTo(0));
        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.FieldCount, Is.EqualTo(2));
        Assert.That(dataReader.GetOrdinal("foo"), Is.EqualTo(1));
        Assert.That(dataReader.GetOrdinal("bar"), Is.EqualTo(0));
    }

    [Test]
    public void Read_FirstRowByNameTyped_Success()
    {
        using var stream =
                    Assembly.GetExecutingAssembly()
                        .GetManifestResourceStream($"{Assembly.GetExecutingAssembly().GetName().Name}.Resources.metrics.ndjson")
                    ?? throw new FileNotFoundException();
        var dataReader = new NdjsonDataReader(stream, NdjsonProfile.Default);
        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.GetFieldValue<string>("metric"), Is.EqualTo("RAMP_UP_SCORE"));
        Assert.That(dataReader.GetFieldValue<int>("line"), Is.EqualTo(335));
    }

    [Test]
    public void Read_FirstRowByNameWithSchema_Success()
    {
        var schema = new SchemaDescriptorBuilder().Named()
                            .WithField<string>("metric")
                            .WithIntegerField<int>("line")
                            .Build();
        using var stream =
                    Assembly.GetExecutingAssembly()
                        .GetManifestResourceStream($"{Assembly.GetExecutingAssembly().GetName().Name}.Resources.metrics.ndjson")
                    ?? throw new FileNotFoundException();
        var dataReader = new NdjsonDataReader(stream, new NdjsonProfile(new DialectDescriptor(), schema));
        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.GetValue("metric"), Is.EqualTo("RAMP_UP_SCORE"));
        Assert.That(dataReader.GetValue("line"), Is.EqualTo(335));
    }

    [Test]
    public void Read_AllRows_Success()
    {
        using var stream =
                    Assembly.GetExecutingAssembly()
                        .GetManifestResourceStream($"{Assembly.GetExecutingAssembly().GetName().Name}.Resources.metrics.ndjson")
                    ?? throw new FileNotFoundException();
        var dataReader = new NdjsonDataReader(stream, NdjsonProfile.Default);
        for (int i = 0; i <= 6; i++)
            Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.Read(), Is.False);
    }
}
