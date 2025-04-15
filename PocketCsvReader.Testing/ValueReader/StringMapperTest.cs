using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using PocketCsvReader.FieldParsing;

namespace PocketCsvReader.Testing.ValueReader;
public class StringMapperTest
{
    [Test]
    public void Map_StringPool_CorrectString()
    {
        Span<char> buffer = stackalloc char[64];
        "foo".AsSpan().CopyTo(buffer);
        buffer = buffer.Slice(0, "foo".Length);

        var stringPool = new CommunityToolkit.HighPerformance.Buffers.StringPool();

        var mapper = new StringMapper(stringPool.GetOrAdd);
        var value = mapper.Map(buffer);
        Assert.That(value, Is.EqualTo("foo"));
    }

    [Test]
    public void Map_StringPool_Called()
    {
        Span<char> buffer = stackalloc char[64];
        "foo".AsSpan().CopyTo(buffer);
        buffer = buffer.Slice(0, "foo".Length);
        var count = 0;

        PoolString poolString = (span) =>
        {
            count++;
            return span.ToString();
        };

        var mapper = new StringMapper(poolString);
        var value = mapper.Map(buffer);
        Assert.That(value, Is.EqualTo("foo"));
        Assert.That(count, Is.EqualTo(1));
    }
}
