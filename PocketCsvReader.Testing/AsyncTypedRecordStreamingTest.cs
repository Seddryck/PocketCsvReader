using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing;

[TestFixture]
public class AsyncTypedRecordStreamingTest
{
    private sealed record Person(int Id, string Name);

    [Test]
    public async Task ToAsync_Stream_MapsRecordsAndLeavesStreamOpen()
    {
        await using var stream = Text("1,Ada\n2,Grace");
        var people = new List<Person>();

        await foreach (var person in new CsvReader(Profile()).ToAsync<Person>(stream))
            people.Add(person);

        Assert.Multiple(() =>
        {
            Assert.That(people, Is.EqualTo(new[] { new Person(1, "Ada"), new Person(2, "Grace") }));
            Assert.That(stream.CanRead, Is.True);
        });
    }

    [Test]
    public async Task ToAsync_File_MapsRecordsAndClosesFileAfterEnumeration()
    {
        var filename = Path.Combine(TestContext.CurrentContext.WorkDirectory, $"async-{Guid.NewGuid():N}.csv");
        await File.WriteAllTextAsync(filename, "1,Ada\n2,Grace");

        try
        {
            var people = new List<Person>();
            await foreach (var person in new CsvReader(Profile()).ToAsync<Person>(filename))
                people.Add(person);

            Assert.That(people, Has.Count.EqualTo(2));
            Assert.DoesNotThrow(() => File.Delete(filename));
        }
        finally
        {
            if (File.Exists(filename))
                File.Delete(filename);
        }
    }

    [Test]
    public async Task ToAsync_IsLazyAndUsesOnlyAsynchronousReadsOnNonSeekableStream()
    {
        await using var stream = new AsyncOnlyStream(Encoding.UTF8.GetBytes("1,Ada\n"));
        var records = new CsvReader(Profile()).ToAsync<Person>(stream);

        Assert.That(stream.AsyncReadCount, Is.Zero);
        await using var enumerator = records.GetAsyncEnumerator();
        Assert.That(stream.AsyncReadCount, Is.Zero);

        Assert.That(await enumerator.MoveNextAsync(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(enumerator.Current, Is.EqualTo(new Person(1, "Ada")));
            Assert.That(stream.AsyncReadCount, Is.GreaterThan(0));
        });
        Assert.That(await enumerator.MoveNextAsync(), Is.False);
    }

    [Test]
    public async Task ToAsync_RecordCrossingBufferBoundaries_ReturnsCompleteRecord()
    {
        var profile = Profile();
        profile.ParserOptimizations = profile.ParserOptimizations with { BufferSize = 4 };
        await using var stream = Text("1,abcdefgh\n2,x");
        var people = new List<Person>();

        await foreach (var person in new CsvReader(profile).ToAsync<Person>(stream))
            people.Add(person);

        Assert.That(people, Is.EqualTo(new[] { new Person(1, "abcdefgh"), new Person(2, "x") }));
    }

    [Test]
    public void ToAsync_MalformedRecord_ThrowsInvalidDataException()
    {
        using var stream = Text("\"unterminated");

        Assert.That(async () =>
        {
            await foreach (var _ in new CsvReader(Profile()).ToAsync<Person>(stream))
            { }
        }, Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public async Task ToAsync_CancellationIsForwardedAndStreamRemainsOpen()
    {
        await using var stream = new BlockingAsyncStream();
        using var cancellation = new CancellationTokenSource();
        var enumerator = new CsvReader(Profile())
            .ToAsync<Person>(stream, cancellationToken: cancellation.Token)
            .GetAsyncEnumerator();
        var moveNext = enumerator.MoveNextAsync().AsTask();

        cancellation.Cancel();

        Assert.That(async () => await moveNext, Throws.InstanceOf<OperationCanceledException>());
        await enumerator.DisposeAsync();
        Assert.That(stream.CanRead, Is.True);
    }

    [Test]
    public async Task ToAsync_CustomMapper_MatchesSynchronousMapping()
    {
        static Person Map(ReadOnlySpan<char> record, IEnumerable<FieldSpan> fields)
        {
            var spans = fields.ToArray();
            return new Person(
                int.Parse(record.Slice(spans[0].Value.Start, spans[0].Value.Length)),
                record.Slice(spans[1].Value.Start, spans[1].Value.Length).ToString().ToUpperInvariant());
        }

        var csv = new CsvReader(Profile());
        using var synchronousStream = Text("1,Ada\n2,Grace");
        var expected = csv.To<Person>(synchronousStream, Map).ToArray();
        await using var asynchronousStream = Text("1,Ada\n2,Grace");
        var actual = new List<Person>();

        await foreach (var person in csv.ToAsync<Person>(asynchronousStream, Map))
            actual.Add(person);

        Assert.That(actual, Is.EqualTo(expected));
    }

    private static CsvProfile Profile()
        => new(new DialectDescriptorBuilder()
            .WithDelimiter(',')
            .WithLineTerminator("\n")
            .WithoutHeader()
            .Build());

    private static MemoryStream Text(string content) => new(Encoding.UTF8.GetBytes(content));

    private sealed class AsyncOnlyStream(byte[] content) : Stream
    {
        private readonly MemoryStream _inner = new(content);
        public int AsyncReadCount { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Synchronous read used.");
        public override int Read(Span<byte> buffer) => throw new InvalidOperationException("Synchronous read used.");
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            AsyncReadCount++;
            return _inner.ReadAsync(buffer, cancellationToken);
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _inner.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class BlockingAsyncStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Synchronous read used.");
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
