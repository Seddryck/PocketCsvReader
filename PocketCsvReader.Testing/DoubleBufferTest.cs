using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace PocketCsvReader.Testing;
public class DoubleBufferTest
{
    [Test]
    public void Read_LargerThanMaxLength_MaxLength()
    {
        var reader = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes("Hello World!")));
        var buffer = new DoubleBuffer(reader, 5);
        var span = buffer.Read();
        Assert.That(span.Length, Is.EqualTo(5));
        Assert.That(span.ToArray, Is.EqualTo("Hello"));
    }

    [Test]
    public void IsEof_LengthEqualsMaxLength_False()
    {
        var reader = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes("HelloWorld")));
        var buffer = new DoubleBuffer(reader, 5);
        var span = buffer.Read();
        Assert.That(span.Length, Is.EqualTo(5));
        Assert.That(span.ToArray, Is.EqualTo("Hello"));
        span = buffer.Read();
        Assert.That(span.Length, Is.EqualTo(5));
        Assert.That(span.ToArray, Is.EqualTo("World"));
        Assert.That(buffer.IsEof, Is.False);
        span = buffer.Read();
        Assert.That(span.Length, Is.EqualTo(0));
        Assert.That(buffer.IsEof, Is.True);
    }

    [Test]
    public void Read_MaxLength_CompleteText()
    {
        var reader = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes("Hello World!")));
        var buffer = new DoubleBuffer(reader, 5);
        var span = buffer.Read();
        Assert.That(span.Length, Is.EqualTo(5));
        Assert.That(span.ToArray, Is.EqualTo("Hello"));
        span = buffer.Read();
        Assert.That(span.Length, Is.EqualTo(5));
        Assert.That(span.ToArray, Is.EqualTo(" Worl"));
        span = buffer.Read();
        Assert.That(span.Length, Is.EqualTo(2));
        Assert.That(span.ToArray, Is.EqualTo("d!"));
        Assert.That(buffer.IsEof, Is.True);
    }
}
