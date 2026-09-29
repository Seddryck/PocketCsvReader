using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing;

public class BadDataPolicyTest
{
    [Test]
    public void DefaultPolicy_ThrowsOnMalformedQuote()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("a,b\r\n\"bad\"x,z\r\nc,d"));
        Assert.Throws<InvalidDataException>(() => new CsvReader(Profile()).ToArrayString(stream).ToArray());
    }

    [TestCase(4)]
    [TestCase(4096)]
    public void SkipPolicy_ReportsAndContinues(int bufferSize)
    {
        var diagnostics = new List<BadDataContext>();
        var profile = Profile();
        profile.ParserOptimizations = profile.ParserOptimizations with { BufferSize = bufferSize };
        profile.BadDataPolicy = new BadDataPolicy(context =>
        {
            diagnostics.Add(context);
            return BadDataAction.SkipRecord;
        }, 2);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("a,b\r\n\"bad\"x,z\r\nc,d"));

        var rows = new CsvReader(profile).ToArrayString(stream).ToArray();

        Assert.That(rows, Is.EqualTo(new[] { new[] { "a", "b" }, new[] { "c", "d" } }));
        Assert.That(diagnostics, Has.Count.EqualTo(1));
        Assert.That(diagnostics[0].ParserState, Is.EqualTo(ParserState.Error));
    }

    private static CsvProfile Profile() => new(new DialectDescriptorBuilder()
        .WithDelimiter(',').WithQuoteChar('"').WithLineTerminator("\r\n").WithoutHeader().Build());
}
