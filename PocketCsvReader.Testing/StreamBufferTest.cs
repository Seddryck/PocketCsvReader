using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace PocketCsvReader.Testing;
public class StreamBufferTest
{
    [Test]
    public void Read_LargerThanMaxLength_MaxLength()
    {
        var reader = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes("Hello World!")));
        var buffer = new StreamBuffer(reader, 5);
        Assert.That(buffer.Length, Is.EqualTo(5));
        buffer.Read();
        Assert.That(buffer.Length, Is.EqualTo(5));
        Assert.That(buffer.Memory.Length, Is.EqualTo(5));
        Assert.That(buffer.IsEof, Is.False);
        Assert.That(buffer.Memory.ToArray(), Is.EqualTo("Hello"));
    }

    [Test]
    public void Read_ExactMaxLength_MaxLength()
    {
        var reader = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes("Hello")));
        var buffer = new StreamBuffer(reader, 5);
        Assert.That(buffer.Length, Is.EqualTo(5));
        buffer.Read();
        Assert.That(buffer.Length, Is.EqualTo(5));
        Assert.That(buffer.Memory.Length, Is.EqualTo(5));
        Assert.That(buffer.IsEof, Is.False);
        Assert.That(buffer.Memory.ToArray(), Is.EqualTo("Hello"));
    }

    [Test]
    public void Read_LessThanMaxLength_MaxLength()
    {
        var reader = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes("Hi")));
        var buffer = new StreamBuffer(reader, 5);
        Assert.That(buffer.Length, Is.EqualTo(5));
        buffer.Read();
        Assert.That(buffer.Length, Is.EqualTo(2));
        Assert.That(buffer.Memory.Length, Is.EqualTo(2));
        Assert.That(buffer.IsEof, Is.True);
        Assert.That(buffer.Memory.ToArray(), Is.EqualTo("Hi"));
    }

    [Test]
    public void Read_PartiallyConsumed_MaxLength()
    {
        var reader = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes("HelloWorld")));
        var buffer = new StreamBuffer(reader, 5);
        buffer.Read();
        buffer.Read();
        Assert.That(buffer.Length, Is.EqualTo(5));
        Assert.That(buffer.IsEof, Is.False);
        Assert.That(buffer.Memory.ToArray(), Is.EqualTo("World"));
    }

    [Test]
    public void Read_CompletelyConsumedThenEnd_MaxLength()
    {
        var reader = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes("HelloWorld")));
        var buffer = new StreamBuffer(reader, 5);
        buffer.Read();
        buffer.Read();
        Assert.That(buffer.IsEof, Is.False);
        buffer.Read();
        Assert.That(buffer.IsEof, Is.True);
        Assert.That(buffer.Length, Is.EqualTo(0));
    }

    [Test]
    public void Read_CompletelyConsumedThenEndPartial_MaxLength()
    {
        var reader = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes("Hello World!")));
        var buffer = new StreamBuffer(reader, 5);
        buffer.Read();
        buffer.Read();
        Assert.That(buffer.IsEof, Is.False);
        buffer.Read();
        Assert.That(buffer.Length, Is.EqualTo(2));
        Assert.That(buffer.IsEof, Is.True);
        Assert.That(buffer.Memory.ToArray(), Is.EqualTo("d!"));
    }
}
