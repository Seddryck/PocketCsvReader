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

    [TestCase(false, 6)]
    [TestCase(true, 4)]
    public void CsvReader_UsesLastValueOnlyForTheSameColumn(bool hint, int expectedCalls)
    {
        var calls = 0;
        var profile = new CsvProfile(new(Header: false, LineTerminator: "\n"))
        {
            ParserOptimizations = new(BufferSize: 3, ReadAhead: false, PoolString: span =>
            {
                calls++;
                return span.ToString();
            })
            {
                PoolStringLastValueHint = hint,
            },
        };
        using var reader = new CsvReader(profile).ToDataReader(new StringReader("é,x\né,é\ny,é\n"), leaveOpen: false);

        Assert.That(reader.Read(), Is.True);
        var first = reader.GetString(0);
        Assert.That(reader.GetString(1), Is.EqualTo("x"));
        Assert.That(reader.Read(), Is.True);
        var repeated = reader.GetString(0);
        Assert.That(repeated, Is.EqualTo(first));
        if (hint)
            Assert.That(repeated, Is.SameAs(first));
        var secondColumn = (string)reader.GetValue(1);
        Assert.That(secondColumn, Is.EqualTo(first));
        Assert.That(secondColumn, Is.Not.SameAs(first));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("y"));
        Assert.That(reader.GetString(1), Is.EqualTo(secondColumn));
        Assert.That(calls, Is.EqualTo(expectedCalls));
    }

    [Test]
    public void ColumnHint_DoesNotUseTransformedTextAsTheInputKey()
    {
        var mapper = new StringMapper(span => span.SequenceEqual("a") ? "A" : "different", lastValueHint: true);

        Assert.That(mapper.Map(0, "a"), Is.EqualTo("A"));
        Assert.That(mapper.Map(0, "A"), Is.EqualTo("different"));
    }

    [Test]
    public void ColumnHint_HandlesWideColumnsAndBypassesItsCapacity()
    {
        var calls = 0;
        var mapper = new StringMapper(span => { calls++; return span.ToString(); }, lastValueHint: true);

        Assert.That(mapper.Map(100, "東京"), Is.SameAs(mapper.Map(100, "東京")));
        Assert.That(calls, Is.EqualTo(1));
        Assert.That(mapper.Map(16 * 1024, "東京"), Is.Not.SameAs(mapper.Map(16 * 1024, "東京")));
        Assert.That(calls, Is.EqualTo(3));
    }
}
