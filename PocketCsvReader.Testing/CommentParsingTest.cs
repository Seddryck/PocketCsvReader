using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing;

public class CommentParsingTest
{
    private static CsvProfile Profile() => new(new DialectDescriptorBuilder()
        .WithCommentChar('#')
        .WithoutHeader()
        .Build());

    [Test]
    public void ArrayReader_SkipsCommentAndKeepsFollowingRows()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("a,b\r\n#ignored\r\nc,d"));
        var rows = new CsvReader(Profile()).ToArrayString(stream).ToArray();
        Assert.That(rows, Is.EqualTo(new[] { new[] { "a", "b" }, new[] { "c", "d" } }));
    }

    [Test]
    public void CommentMarkerInLaterField_IsData()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("a,#not-comment\r\nb,c"));
        var rows = new CsvReader(Profile()).ToArrayString(stream).ToArray();
        Assert.That(rows, Is.EqualTo(new[] { new[] { "a", "#not-comment" }, new[] { "b", "c" } }));
    }
}
