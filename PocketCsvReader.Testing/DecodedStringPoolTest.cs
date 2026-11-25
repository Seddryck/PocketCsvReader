using NUnit.Framework;

namespace PocketCsvReader.Testing;

public class DecodedStringPoolTest
{
    [Test]
    public void PoolOwnsItsKeysAndSharesStringsAcrossColumns()
    {
        var pool = new DecodedStringPool();
        Span<char> buffer = stackalloc char[2];
        "東京".AsSpan().CopyTo(buffer);
        var first = pool.GetString(0, buffer);
        buffer[0] = '西';

        Assert.That(first, Is.EqualTo("東京"));
        Assert.That(pool.GetString(0, buffer), Is.EqualTo("西京"));
        Assert.That(pool.GetString(100, "東京"), Is.SameAs(first));
        Assert.That(pool.GetString(16 * 1024, "東京"), Is.SameAs(first));
        Assert.That(pool.GetString(100, "東京"), Is.SameAs(first));
        Assert.That(pool.GetString(0, ""), Is.SameAs(string.Empty));
        var longField = new string('a', 129);
        Assert.That(pool.GetString(0, longField), Is.Not.SameAs(pool.GetString(0, longField)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ReaderPoolsDecodedTextAndKeepsConfiguredPoolPrecedence(bool useCallback)
    {
        int calls = 0;
        PoolString? callback = useCallback ? text =>
        {
            calls++;
            return text.ToString().ToUpperInvariant();
        } : null;
        var profile = new CsvProfile(new(Header: false, LineTerminator: "\n"))
        {
            ParserOptimizations = new(BufferSize: 5, ReadAhead: false, PoolString: callback)
            {
                PoolDecodedStrings = true,
            },
        };

        using var reader = new CsvReader(profile).ToDataReader(new StringReader("\"élan\"\"東京\",x\nx,\"élan\"\"東京\"\n"), leaveOpen: false);
        Assert.That(await reader.ReadAsync(), Is.True);
        var first = reader.GetString(0);
        Assert.That(await reader.ReadAsync(), Is.True);
        var second = (string)reader.GetValue(1);

        if (useCallback)
        {
            Assert.That(first, Is.EqualTo("ÉLAN\"東京"));
            Assert.That(second, Is.EqualTo(first));
            Assert.That(calls, Is.EqualTo(2));
        }
        else
        {
            Assert.That(first, Is.EqualTo("élan\"東京"));
            Assert.That(second, Is.SameAs(first));
        }
    }
}
