using System.Data;
using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing;

public class DictionaryRowsTest
{
    [Test]
    public void ToDictionaries_ParsesNamedFieldsAndPreservesNulls()
    {
        var reader = new CsvReaderBuilder()
            .WithDialect(d => d.WithHeader().WithLineTerminator("\n").WithNullSequence("NULL"))
            .WithSchema(s => s.Named().WithField<int>("Id").WithField<decimal>("Price").WithField<string>("Name"))
            .Build();
        using var stream = Source("Name,Price,Id\nAda,12.5,1\nNULL,3.5,2\n");

        var rows = reader.ToDictionaries(stream).ToArray();

        Assert.That(rows[0]["Id"], Is.TypeOf<int>().And.EqualTo(1));
        Assert.That(rows[0]["Price"], Is.TypeOf<decimal>().And.EqualTo(12.5m));
        Assert.That(rows[0]["Name"], Is.EqualTo("Ada"));
        Assert.That(rows[1]["Name"], Is.Null);
        Assert.That(rows[0], Is.Not.SameAs(rows[1]));
    }

    [Test]
    public void ToDictionaries_FileInputIsLazyAndOwned()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "Name\nBefore\n");
            var rows = Reader().ToDictionaries(path);
            File.WriteAllText(path, "Name\nAfter\n");
            Assert.That(rows.Single()["Name"], Is.EqualTo("After"));
            using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        finally { File.Delete(path); }
    }

    [Test]
    public void ToDictionaries_HeaderlessInputUsesReaderGeneratedNames()
    {
        using var stream = Source("1,Ada\n");
        var row = Reader(false).ToDictionaries(stream).Single();
        Assert.That(row.Keys, Is.EqualTo(new[] { "field_0", "field_1" }));
        Assert.That(row.Values, Is.EqualTo(new[] { "1", "Ada" }));
    }

    [Test]
    public void ToDictionaries_UnnamedAndCaseDistinctHeadersUseReaderNames()
    {
        using var stream = Source(",Name,name\n1,Ada,Grace\n");
        var row = Reader().ToDictionaries(stream).Single();
        Assert.That(row.Keys, Is.EqualTo(new[] { "field_1", "Name", "name" }));
    }

    [Test]
    public void ToDictionaries_DuplicateNamesThrowAndDisposeStream()
    {
        using var stream = Source("Name,Name\nAda,Grace\n");
        Assert.Throws<InvalidDataException>(() => Reader().ToDictionaries(stream).ToArray());
        Assert.That(stream.CanRead, Is.False);
    }

    [Test]
    public void ToDictionaries_IsLazyForwardOnlyAndDisposesOnEarlyExit()
    {
        using var stream = new ForwardStream("Name\nAda\n" + string.Concat(Enumerable.Repeat("Grace\n", 10000)));
        var rows = Reader().ToDictionaries(stream);
        Assert.That(stream.BytesRead, Is.Zero);
        using (var iterator = rows.GetEnumerator())
        {
            Assert.That(iterator.MoveNext(), Is.True);
            Assert.That(iterator.Current["Name"], Is.EqualTo("Ada"));
            Assert.That(stream.BytesRead, Is.LessThan(stream.ContentLength));
        }
        Assert.That(stream.Disposed, Is.True);
    }

    [Test]
    public void ToDictionaries_EmptyInputYieldsNoRows()
    {
        using var stream = Source("");
        Assert.That(Reader(false).ToDictionaries(stream), Is.Empty);
    }

    [Test]
    public void ToDictionaries_NormalizesDatabaseNulls()
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(int));
        table.Rows.Add(DBNull.Value);
        using var reader = table.CreateDataReader();
        Assert.That(DataReaderMaterializer.ToDictionaries(reader).Single()["Id"], Is.Null);
    }

    [Test]
    public void ToDictionaries_UsesFieldAndRuntimeParsers()
    {
        var reader = new CsvReaderBuilder()
            .WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
            .WithSchema(s => s.Named()
                .WithField<int>("Id", f => f.WithParser((ReadOnlySpan<char> text) => int.Parse(text[1..])))
                .WithField<string>("Name"))
            .WithParsers(p => p.WithParser<string>((string text) => text.ToUpperInvariant()))
            .Build();
        using var stream = Source("Id,Name\nx42,Ada\n");
        var row = reader.ToDictionaries(stream).Single();
        Assert.That(row["Id"], Is.EqualTo(42));
        Assert.That(row["Name"], Is.EqualTo("ADA"));
    }

    internal static MemoryStream Source(string text) => new(Encoding.UTF8.GetBytes(text));
    internal static CsvReader Reader(bool header = true) => new(new CsvProfile(
        new DialectDescriptorBuilder().WithHeader(header).WithLineTerminator("\n").Build())
    { StreamInitialization = StreamInitializationOptions.ForwardOnly });

    internal sealed class ForwardStream(string text) : Stream
    {
        private readonly MemoryStream inner = Source(text);
        public int BytesRead { get; private set; }
        public int ContentLength { get; } = Encoding.UTF8.GetByteCount(text);
        public bool Disposed { get; private set; }
        public override bool CanRead => !Disposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            Disposed = true;
            base.Dispose(disposing);
        }
    }
}
