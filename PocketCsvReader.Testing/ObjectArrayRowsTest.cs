using System.Data;
using NUnit.Framework;
using PocketCsvReader.Configuration;
using static PocketCsvReader.Testing.DictionaryRowsTest;

namespace PocketCsvReader.Testing;

public class ObjectArrayRowsTest
{
    [Test]
    public void ToObjectArrays_ParsesMixedTypesInInputOrderAndPreservesNulls()
    {
        var reader = new CsvReaderBuilder()
            .WithDialect(d => d.WithHeader().WithLineTerminator("\n").WithNullSequence("NULL"))
            .WithSchema(s => s.Named().WithField<int>("Id").WithField<string>("Name")
                .WithField<decimal>("Price").WithField<bool>("Active").WithField<int?>("Optional"))
            .Build();
        using var stream = Source("Name,Price,Active,Id,Optional\nAda,12.5,true,1,NULL\nGrace,3.5,false,2,42\n");

        var rows = reader.ToObjectArrays(stream).ToArray();

        Assert.That(rows[0], Is.EqualTo(new object?[] { "Ada", 12.5m, true, 1, null }));
        Assert.That(rows[1], Is.EqualTo(new object?[] { "Grace", 3.5m, false, 2, 42 }));
        Assert.That(rows[0][1], Is.TypeOf<decimal>());
        Assert.That(rows[0][3], Is.TypeOf<int>());
        Assert.That(rows[0], Is.Not.SameAs(rows[1]));
        rows[1][0] = "changed";
        Assert.That(rows[0][0], Is.EqualTo("Ada"));
    }

    [Test]
    public void ToObjectArrays_UsesFieldAndRuntimeParsers()
    {
        var reader = new CsvReaderBuilder()
            .WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
            .WithSchema(s => s.Named()
                .WithField<int>("Id", f => f.WithParser((ReadOnlySpan<char> text) => int.Parse(text[1..])))
                .WithField<string>("Name"))
            .WithParsers(p => p.WithParser<string>((string text) => text.ToUpperInvariant()))
            .Build();
        using var stream = Source("Id,Name\nx42,Ada\n");
        Assert.That(reader.ToObjectArrays(stream).Single(), Is.EqualTo(new object[] { 42, "ADA" }));
    }

    [Test]
    public void ToObjectArrays_FileInputIsLazyAndReleasesFile()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "Name\nBefore\n");
            var rows = Reader().ToObjectArrays(path);
            File.WriteAllText(path, "Name\nAfter\n");
            Assert.That(rows.Single(), Is.EqualTo(new[] { "After" }));
            using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally { File.Delete(path); }
    }

    [Test]
    public void ToObjectArrays_HeaderlessUntypedFieldsRemainStrings()
    {
        using var stream = Source("1,true\n2,false\n");
        Assert.That(Reader(false).ToObjectArrays(stream), Is.EqualTo(new[]
        {
            new object[] { "1", "true" }, new object[] { "2", "false" }
        }));
    }

    [Test]
    public void ToObjectArrays_IsLazyForwardOnlyAndDisposesOnEarlyExit()
    {
        using var stream = new ForwardStream("Name\nAda\n" + string.Concat(Enumerable.Repeat("Grace\n", 10000)));
        var rows = Reader().ToObjectArrays(stream);
        Assert.That(stream.BytesRead, Is.Zero);
        using (var iterator = rows.GetEnumerator())
        {
            Assert.That(iterator.MoveNext(), Is.True);
            Assert.That(iterator.Current, Is.EqualTo(new[] { "Ada" }));
            Assert.That(stream.BytesRead, Is.LessThan(stream.ContentLength));
        }
        Assert.That(stream.Disposed, Is.True);
    }

    [TestCase(4)]
    [TestCase(4096)]
    public void ToObjectArrays_RetainsEscapedValuesAcrossBufferBoundaries(int bufferSize)
    {
        var text = new string('x', 9000) + "\"tail";
        using var stream = new ForwardStream($"Name,Id\n\"{text.Replace("\"", "\"\"")}\",1\nshort,2\n");
        var profile = new CsvProfile(new DialectDescriptorBuilder().WithHeader().WithLineTerminator("\n").Build())
        {
            StreamInitialization = StreamInitializationOptions.ForwardOnly,
            ParserOptimizations = new ParserOptimizationOptions(BufferSize: bufferSize)
        };
        var rows = new CsvReader(profile).ToObjectArrays(stream).ToArray();
        Assert.That(rows[0], Is.EqualTo(new[] { text, "1" }));
        Assert.That(rows[1], Is.EqualTo(new[] { "short", "2" }));
    }

    [TestCase("Name,Value\nAda\n", new[] { "Ada" })]
    [TestCase("Name,Value\nAda,\n", new[] { "Ada", "" })]
    public void ToObjectArrays_IncompleteAndEmptyFieldsMatchReaderWidth(string content, string[] expected)
    {
        using var stream = Source(content);
        Assert.That(Reader().ToObjectArrays(stream).Single(), Is.EqualTo(expected));
    }

    [Test]
    public void ToObjectArrays_ExtraFieldsThrowAndDisposeStream()
    {
        using var stream = Source("Name\nAda,extra\n");
        Assert.Throws<InvalidDataException>(() => Reader().ToObjectArrays(stream).ToArray());
        Assert.That(stream.CanRead, Is.False);
    }

    [Test]
    public void ToObjectArrays_InvalidTypedValueThrowsAndDisposesStream()
    {
        var reader = new CsvReaderBuilder()
            .WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
            .WithSchema(s => s.Named().WithField<int>("Id"))
            .Build();
        using var stream = Source("Id\ninvalid\n");
        Assert.Throws<FormatException>(() => reader.ToObjectArrays(stream).ToArray());
        Assert.That(stream.CanRead, Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ToObjectArrays_MalformedRecordsFollowBadDataPolicy(bool skip)
    {
        var profile = new CsvProfile(new DialectDescriptorBuilder().WithoutHeader().WithLineTerminator("\r\n").Build());
        var diagnostics = new List<BadDataContext>();
        if (skip)
            profile.BadDataPolicy = new BadDataPolicy(context =>
            {
                diagnostics.Add(context);
                return BadDataAction.SkipRecord;
            }, 2);
        using var stream = Source("a,b\r\n\"bad\"x,z\r\nc,d");
        var rows = new CsvReader(profile).ToObjectArrays(stream);
        if (skip)
        {
            Assert.That(rows, Is.EqualTo(new[] { new[] { "a", "b" }, new[] { "c", "d" } }));
            Assert.That(diagnostics, Has.Count.EqualTo(1));
        }
        else
            Assert.Throws<InvalidDataException>(() => rows.ToArray());
    }

    [Test]
    public void ToObjectArrays_NormalizesDatabaseNulls()
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Rows.Add(DBNull.Value);
        using var reader = table.CreateDataReader();
        Assert.That(DataReaderMaterializer.ToObjectArrays(reader).Single(), Is.EqualTo(new object?[] { null }));
    }

    [Test]
    public void ToObjectArrays_EmptyInputYieldsNoRows()
    {
        using var stream = Source("");
        Assert.That(Reader(false).ToObjectArrays(stream), Is.Empty);
    }

    [Test]
    public void ToObjectArrays_ValidatesArgumentsImmediately()
    {
        Assert.Throws<ArgumentNullException>(() => Reader().ToObjectArrays((Stream)null!));
        Assert.Throws<FileNotFoundException>(() => Reader().ToObjectArrays(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv")));
    }
}
