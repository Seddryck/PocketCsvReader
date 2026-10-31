using System.Data;
using System.Dynamic;
using NUnit.Framework;
using PocketCsvReader.Configuration;
using static PocketCsvReader.Testing.DictionaryRowsTest;

namespace PocketCsvReader.Testing;

public class DynamicRowsTest
{
    [Test]
    public void ToDynamic_ExposesTypedMembersNullsAndIndependentDictionaries()
    {
        var reader = new CsvReaderBuilder()
            .WithDialect(d => d.WithHeader().WithLineTerminator("\n").WithNullSequence("NULL"))
            .WithSchema(s => s.Named().WithField<int>("Id").WithField<decimal>("Price")
                .WithField<string>("Full name"))
            .Build();
        using var stream = Source("Full name,Price,Id\nAda,12.5,1\nNULL,3.5,2\n");

        var rows = reader.ToDynamic(stream).ToArray();
        dynamic first = rows[0];
        dynamic second = rows[1];
        Assert.That((object)first, Is.TypeOf<ExpandoObject>());
        Assert.That((object)first.Id, Is.TypeOf<int>().And.EqualTo(1));
        Assert.That((object)first.Price, Is.TypeOf<decimal>().And.EqualTo(12.5m));
        var firstFields = (IDictionary<string, object?>)first;
        var secondFields = (IDictionary<string, object?>)second;
        Assert.That(firstFields["Full name"], Is.EqualTo("Ada"));
        Assert.That(secondFields["Full name"], Is.Null);
        secondFields["Full name"] = "Changed";
        Assert.That(firstFields["Full name"], Is.EqualTo("Ada"));
        Assert.That(stream.CanRead, Is.False);
    }

    [TestCase("Name,Name\nAda,Grace\n")]
    [TestCase(",field_1\nAda,Grace\n")]
    public void ToDynamic_DuplicateNamesMatchDataTableAndDisposeBeforeYield(string input)
    {
        using var tableStream = Source(input);
        Assert.Throws<DuplicateNameException>(() => Reader().ToDataTable(tableStream));
        using var stream = Source(input);
        using var iterator = Reader().ToDynamic(stream).GetEnumerator();
        var error = Assert.Throws<DuplicateNameException>(() => iterator.MoveNext());
        Assert.That(error!.Message, Does.Contain("field").IgnoreCase);
        Assert.That(stream.CanRead, Is.False);
    }

    [TestCase(false, "1,Ada\n", new[] { "field_0", "field_1" })]
    [TestCase(true, ",Name,name\n1,Ada,Grace\n", new[] { "field_1", "Name", "name" })]
    public void ToDynamic_PreservesGeneratedAndCaseDistinctNames(bool header, string input, string[] names)
    {
        using var stream = Source(input);
        var row = (IDictionary<string, object?>)Reader(header).ToDynamic(stream).Single();
        Assert.That(row.Keys, Is.EqualTo(names));
        Assert.That(row["field_" + (header ? "1" : "0")], Is.EqualTo("1"));
    }

    [Test]
    public void ToDynamic_UsesFieldAndRuntimeParsers()
    {
        var reader = new CsvReaderBuilder()
            .WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
            .WithSchema(s => s.Named()
                .WithField<int>("Id", f => f.WithParser((ReadOnlySpan<char> text) => int.Parse(text[1..])))
                .WithField<string>("Name"))
            .WithParsers(p => p.WithParser<string>((string text) => text.ToUpperInvariant()))
            .Build();
        using var stream = Source("Id,Name\nx42,Ada\n");
        dynamic row = reader.ToDynamic(stream).Single();
        Assert.That((object)row.Id, Is.EqualTo(42));
        Assert.That((object)row.Name, Is.EqualTo("ADA"));
    }

    [Test]
    public void ToDynamic_IsLazyForwardOnlyAndDisposesOnEarlyExit()
    {
        using var stream = new ForwardStream("Name\nAda\n" + string.Concat(Enumerable.Repeat("Grace\n", 10000)));
        var rows = Reader().ToDynamic(stream);
        Assert.That(stream.BytesRead, Is.Zero);
        object first;
        using (var iterator = rows.GetEnumerator())
        {
            Assert.That(iterator.MoveNext(), Is.True);
            first = iterator.Current;
            Assert.That(stream.BytesRead, Is.LessThan(stream.ContentLength));
        }
        Assert.That(stream.Disposed, Is.True);
        Assert.That(((IDictionary<string, object?>)first)["Name"], Is.EqualTo("Ada"));
    }

    [Test]
    public void ToDynamic_FileInputIsLazyAndReleasesFile()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "Name\nBefore\n");
            var rows = Reader().ToDynamic(path);
            File.WriteAllText(path, "Name\nAfter\n");
            dynamic row = rows.Single();
            Assert.That((object)row.Name, Is.EqualTo("After"));
            using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally { File.Delete(path); }
    }

    [Test]
    public void ToDynamic_ParsingFailureDisposesStream()
    {
        var reader = new CsvReaderBuilder()
            .WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
            .WithSchema(s => s.Named().WithField<int>("Id"))
            .Build();
        using var stream = Source("Id\ninvalid\n");
        Assert.Throws<FormatException>(() => reader.ToDynamic(stream).ToArray());
        Assert.That(stream.CanRead, Is.False);
    }

    [TestCase("")]
    [TestCase("Name\n")]
    public void ToDynamic_NoRecordsYieldsEmptySequenceAndDisposes(string input)
    {
        using var stream = Source(input);
        Assert.That(Reader().ToDynamic(stream), Is.Empty);
        Assert.That(stream.CanRead, Is.False);
    }

    [Test]
    public void ToDynamic_NormalizesDatabaseNullsFromStandardDataReader()
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Rows.Add(DBNull.Value);
        using var reader = table.CreateDataReader();
        dynamic row = DataReaderMaterializer.ToDynamic(reader).Single();
        Assert.That((object?)row.Id, Is.Null);
    }

    [Test]
    public void ToDynamic_ValidatesArgumentsImmediately()
    {
        Assert.Throws<ArgumentNullException>(() => Reader().ToDynamic((Stream)null!));
        Assert.Throws<FileNotFoundException>(() => Reader().ToDynamic(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv")));
    }
}
