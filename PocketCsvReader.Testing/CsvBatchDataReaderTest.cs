using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing;

[TestFixture]
public class CsvBatchDataReaderTest
{
    private static MemoryStream CreateStream(string content)
    {
        var byteArray = Encoding.UTF8.GetBytes(content);
        var stream = new MemoryStream(byteArray) { Position = 0 };
        return stream;
    }

    [Test]
    public void Read_TwoStreams_Successful()
    {
        var profile = new CsvProfile(',', '\"', "\r\n", false);
        using var stream1 = CreateStream("1,foo,bar\r\n2,foo,bar\r\n3,foo,bar");
        using var stream2 = CreateStream("4,foo,bar\r\n5,foo,bar");
        using var dataReader = new CsvBatchDataReader([stream1, stream2], profile);

        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.GetInt32(0), Is.EqualTo(1));
        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.GetInt32(0), Is.EqualTo(2));
        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.GetInt32(0), Is.EqualTo(3));

        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.GetInt32(0), Is.EqualTo(4));
        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.GetInt32(0), Is.EqualTo(5));

        Assert.That(dataReader.Read(), Is.False);
    }

    [Test]
    public void Close_StreamsDisposed_Successful()
    {
        var profile = new CsvProfile(',', '\"', "\r\n", false);
        using var stream1 = CreateStream("1,foo,bar\r\n2,foo,bar\r\n3,foo,bar");
        using var stream2 = CreateStream("4,foo,bar\r\n5,foo,bar");
        using var dataReader = new CsvBatchDataReader([stream1, stream2], profile);
        dataReader.Close();

        Assert.That(stream1.CanRead, Is.False);
        Assert.That(stream2.CanRead, Is.False);
    }
}
