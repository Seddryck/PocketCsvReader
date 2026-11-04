using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing;

public class ReaderSkipTakeTest
{
    private static MemoryStream Input(string text) => new(Encoding.UTF8.GetBytes(text));
    private static CsvReaderBuilder Builder() => new CsvReaderBuilder()
        .WithDialect(d => d.WithHeader().WithLineTerminator("\n").WithCommentChar('#'));

    public record Number(int Value);

    [TestCase(null, null, "1,2,3")]
    [TestCase(1, null, "2,3")]
    [TestCase(null, 2, "1,2")]
    [TestCase(1, 1, "2")]
    [TestCase(0, 1, "1")]
    [TestCase(1, 0, "")]
    [TestCase(5, 1, "")]
    [TestCase(1, 5, "2,3")]
    public void OutputApisReturnTheSameRecords(int? skip, int? take, string expected)
    {
        const string data = "Value\n# comment\n1\n2\n3";
        var builder = Builder();
        if (take.HasValue)
            builder.WithTake(take.Value);
        if (skip.HasValue)
            builder.WithSkip(skip.Value);
        var csv = builder.Build();

        using var input = Input(data);
        Assert.That(string.Join(",", csv.To<Number>(input).Select(row => row.Value)), Is.EqualTo(expected));
        Assert.That(input.CanRead, Is.True);
        Assert.That(string.Join(",", csv.ToArrayString(Input(data)).Select(row => row[0])), Is.EqualTo(expected));
        Assert.That(string.Join(",", csv.ToDataTable(Input(data)).Rows.Cast<System.Data.DataRow>().Select(row => row[0])),
            Is.EqualTo(expected));

        using var reader = csv.ToDataReader(Input(data));
        var values = new List<int>();
        while (reader.Read())
            values.Add(reader.GetInt32(0));
        Assert.That(string.Join(",", values), Is.EqualTo(expected));
    }

    [Test]
    public async Task AsyncObjectsCountOnlyRowsAcceptedByFilters()
    {
        var csv = Builder().WithSkip(1).WithTake(1).WithFilter<int>(0, value => value % 2 == 0).Build();
        using var input = Input("Value\n1\n2\n3\n4\n\"bad\"x");
        var values = new List<int>();
        await foreach (var row in csv.ToAsync<Number>(input))
            values.Add(row.Value);
        Assert.That(values, Is.EqualTo(new[] { 4 }));
        Assert.That(input.CanRead, Is.True);
    }

    [Test]
    public void FileBatchStopsBeforeOpeningMissingFile()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var first = Path.Combine(directory.FullName, "first.csv");
            var second = Path.Combine(directory.FullName, "second.csv");
            File.WriteAllText(first, "Value\n1");
            File.WriteAllText(second, "2\n3");

            var csv = Builder().WithDialect(d => d.WithoutHeaderRepeat()).WithSkip(1).WithTake(1).Build();
            using var reader = csv.ToDataReader(new[] { first, second, Path.Combine(directory.FullName, "missing.csv") });
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.GetInt32(reader.GetOrdinal("Value")), Is.EqualTo(2));
            Assert.That(reader.Read(), Is.False);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Test]
    public void NegativeCountsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Builder().WithSkip(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Builder().WithTake(-1));
    }

    [TestCase("")]
    [TestCase("Value\n")]
    public void EmptyInputsReturnNoRecords(string data)
    {
        var csv = Builder().WithSkip(1).WithTake(2).Build();
        Assert.That(csv.ToArrayString(Input(data)), Is.Empty);
    }

    [Test]
    public void OnlyAcceptedRecordsCountAndParsingStopsAtTheCap()
    {
        const string data = "Value,Other\n# comment\n\"bad\"x,z\n1,a\n# another comment\n2,b\n\"late\"x,z";
        var csv = Builder().WithSkip(1).WithTake(1).Build();
        var badRecords = 0;
        csv.Profile.BadDataPolicy = new BadDataPolicy(_ =>
        {
            badRecords++;
            return BadDataAction.SkipRecord;
        }, 1);
        Assert.That(csv.ToArrayString(Input(data)).Single()[0], Is.EqualTo("2"));
        Assert.That(badRecords, Is.EqualTo(1));

        csv.Profile.BadDataPolicy = null;
        Assert.Throws<InvalidDataException>(() => csv.ToArrayString(Input(data)).ToArray());
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task BatchSharesCountsAndDoesNotOpenLaterInputs(bool asynchronous)
    {
        var csv = Builder().WithDialect(d => d.WithHeaderRepeat()).WithSkip(2).WithTake(2).Build();
        using var first = Input("Value\n1");
        using var second = Input("Value\n2\n3");
        using var third = Input("Value\n4\n\"bad\"x");
        Func<Stream>[] openers = [() => first, () => second, () => third, () => throw new AssertionException("Opened a later input")];

        var reader = csv.ToDataReader(openers);
        var values = new List<int>();
        while (asynchronous ? await reader.ReadAsync() : reader.Read())
            values.Add(reader.GetInt32(0));
        if (asynchronous)
            await reader.DisposeAsync();
        else
            reader.Dispose();

        Assert.That(values, Is.EqualTo(new[] { 3, 4 }));
        Assert.That(first.CanRead || second.CanRead || third.CanRead, Is.False);
    }

    [Test]
    public void ZeroTakeDoesNotOpenAnInputAndStillDisposesOwnedStreams()
    {
        var csv = Builder().WithSkip(10).WithTake(0).Build();
        using var lazy = csv.ToDataReader(new Func<Stream>[] { () => throw new AssertionException("Opened an input") });
        Assert.That(lazy.Read(), Is.False);

        using var input = Input("Value\n1");
        using (var reader = csv.ToDataReader(new Stream[] { input }))
            Assert.That(reader.Read(), Is.False);
        Assert.That(input.CanRead, Is.False);
    }

    [Test]
    public void DataReaderDisposesItsStreamAfterEarlyTermination()
    {
        var csv = Builder().WithTake(1).Build();
        using var input = Input("Value\n1\n2");
        using (var reader = csv.ToDataReader(input))
            Assert.That(reader.Read(), Is.True);
        Assert.That(input.CanRead, Is.False);
    }
}
