using System.Text;
using NUnit.Framework;
using PocketCsvReader.FixedWidth.Configuration;

namespace PocketCsvReader.FixedWidth.Testing;

public class PartitionedFileQueryTest
{
    [Test]
    public void ReadsSelectedFilesWithTheirOwnHeaders()
    {
        var files = new[]
        {
            FileSource.Partitioned("ignored.txt", "FR", () => throw new AssertionException("Rejected file was opened.")),
            FileSource.Partitioned("first.txt", "BE", () => Open("Id Name\n1  Ada \n")),
            FileSource.Partitioned("second.txt", "BE", () => Open("Id Name\n2  Bob \n"))
        };

        using var reader = new FixedWidthReaderBuilder().WithLineTerminator("\n").WithHeader()
            .WithField("Id", 0, 3).WithField("Name", 3, 4).Build().FromFiles(files)
            .WithPartitionFilter(country => country == "BE").WithFilter(1, name => name.SequenceEqual("Bob"))
            .ToDataReader();

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(2));
        Assert.That(reader.Read(), Is.False);

        static MemoryStream Open(string text) => new(Encoding.UTF8.GetBytes(text));
    }
}
