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
    public void Read_LazyInitialization_Successful()
    {
        var profile = new CsvProfile(',', '\"', "\r\n", false);

        using var stream1 = CreateStream("1,foo,bar\r\n2,foo,bar\r\n3,foo,bar");
        var stream2Created = false;

        IEnumerable<Stream> LazyPaths()
        {
            yield return stream1;

            // Delay creation of stream2 until it's requested
            stream2Created = true;
            var stream2 = CreateStream("4,foo,bar\r\n5,foo,bar");
            yield return stream2;
        }

        using var dataReader = new CsvBatchDataReader(LazyPaths(), profile);

        Assert.That(stream2Created, Is.False); // stream2 not yet created
        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.GetInt32(0), Is.EqualTo(1));
        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.GetInt32(0), Is.EqualTo(2));
        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.GetInt32(0), Is.EqualTo(3));

        Assert.That(dataReader.Read(), Is.True);
        Assert.That(stream2Created, Is.True); // stream2 should now be created
        Assert.That(dataReader.GetInt32(0), Is.EqualTo(4));
        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.GetInt32(0), Is.EqualTo(5));
        Assert.That(dataReader.Read(), Is.False);
    }

    [Test]
    public void Read_TwoStreamsRepeatHeaders_Successful()
    {
        var dialect = new DialectDescriptorBuilder()
            .WithDelimiter(',')
            .WithLineTerminator("\r\n")
            .WithHeader(true)
            .WithHeaderRepeat(true)
            .Build();
        var profile = new CsvProfile(dialect);
        using var stream1 = CreateStream("foo,bar\r\n1,alpha\r\n2,beta");
        using var stream2 = CreateStream("foo,bar\r\n3,gamma\r\n4,delta");
        using var dataReader = new CsvBatchDataReader([stream1, stream2], profile);

        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.GetInt32(0), Is.EqualTo(1));
        Assert.That(dataReader["foo"], Is.EqualTo("1"));
        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.GetInt32(0), Is.EqualTo(2));
        Assert.That(dataReader["foo"], Is.EqualTo("2"));
        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.GetInt32(0), Is.EqualTo(3));
        Assert.That(dataReader["foo"], Is.EqualTo("3"));
        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.GetInt32(0), Is.EqualTo(4));
        Assert.That(dataReader["foo"], Is.EqualTo("4"));
        Assert.That(dataReader.Read(), Is.False);
    }

    [Test]
    public void Read_TwoStreamsDoNotRepeatHeaders_Successful()
    {
        var dialect = new DialectDescriptorBuilder()
            .WithDelimiter(',')
            .WithLineTerminator("\r\n")
            .WithHeader(true)
            .WithHeaderRepeat(false)
            .Build();
        var profile = new CsvProfile(dialect);
        using var stream1 = CreateStream("foo,bar\r\n1,alpha\r\n2,beta");
        using var stream2 = CreateStream("3,gamma\r\n4,delta");
        using var dataReader = new CsvBatchDataReader([stream1, stream2], profile);

        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.GetInt32(0), Is.EqualTo(1));
        Assert.That(dataReader["foo"], Is.EqualTo("1"));
        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.GetInt32(0), Is.EqualTo(2));
        Assert.That(dataReader["foo"], Is.EqualTo("2"));
        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.GetInt32(0), Is.EqualTo(3));
        Assert.That(dataReader["foo"], Is.EqualTo("3"));
        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.GetInt32(0), Is.EqualTo(4));
        Assert.That(dataReader["foo"], Is.EqualTo("4"));
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
