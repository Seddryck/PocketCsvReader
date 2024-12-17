﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;

namespace PocketCsvReader.Testing;
public class SingleBufferTest
{
    [Test]
    public void Read_LargerThanMaxLength_MaxLength()
    {
        var reader = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes("Hello World!")));
        var buffer = new SingleBuffer(reader, 5);
        var span = buffer.Read();
        Assert.That(span.Length, Is.EqualTo(5));
        Assert.That(span.ToString, Is.EqualTo("Hello"));
    }

    [Test]
    public void Read_MaxLength_CompleteText()
    {
        var reader = new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes("Hello World!")));
        var buffer = new SingleBuffer(reader, 5);
        var span = buffer.Read();
        Assert.That(span.Length, Is.EqualTo(5));
        Assert.That(span.ToString, Is.EqualTo("Hello"));
        span = buffer.Read();
        Assert.That(span.Length, Is.EqualTo(5));
        Assert.That(span.ToString, Is.EqualTo(" Worl"));
        span = buffer.Read();
        Assert.That(span.Length, Is.EqualTo(2));
        Assert.That(span.ToString, Is.EqualTo("d!"));
        span = buffer.Read();
        Assert.That(span.Length, Is.EqualTo(0));
        Assert.That(span.ToString, Is.Empty);
        Assert.That(buffer.IsEof, Is.True);
    }
}
