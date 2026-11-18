using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing;

public class TextReaderInputTests
{
    private const string Input = "Id;Name\r\n1;\"Ada\r\nLovelace\"\r\n2;Grace";

    [Test]
    public void ToDataReader_ReadsFromCurrentPositionWithDialectAndSchema()
    {
        using var input = new StringReader("prefix\n" + Input);
        input.ReadLine();

        using (var reader = CreateCsv().ToDataReader(input))
        {
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.GetOrdinal("Name"), Is.EqualTo(1));
            Assert.That(reader.GetValue(0), Is.EqualTo(1));
            Assert.That(reader.GetString(1), Is.EqualTo("Ada\r\nLovelace"));
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.GetString(1), Is.EqualTo("Grace"));
            Assert.That(reader.Read(), Is.False);
        }

        Assert.That(input.Peek(), Is.EqualTo(-1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ToDataReader_CloseAndDisposeReleaseOwnedReaderOnce(bool readFirst)
    {
        var input = new TrackingReader(Input);
        using var reader = CreateCsv().ToDataReader(input, leaveOpen: false);
        if (readFirst)
            Assert.That(reader.Read(), Is.True);

        reader.Close();
        reader.Dispose();

        Assert.That(input.DisposeCount, Is.EqualTo(1));
    }

    [Test]
    public async Task ToDbDataReader_UsesAsyncRefillsAndDisposesOwnedInput()
    {
        var input = new AsyncOnlyReader(Input);
        await using (var reader = CreateCsv().ToDbDataReader(input, leaveOpen: false))
        {
            Assert.That(input.ReadCount, Is.Zero);
            Assert.That(await reader.ReadAsync(), Is.True);
            Assert.That(reader.GetString(reader.GetOrdinal("Name")), Is.EqualTo("Ada\r\nLovelace"));
            Assert.That(input.ReadCount, Is.GreaterThan(1));
        }

        Assert.That(input.IsDisposed, Is.True);
    }

    [Test]
    public void ToDataTable_MaterializesAndDisposesOwnedInput()
    {
        using var input = new StringReader(Input);
        using var table = CreateCsv().ToDataTable(input, leaveOpen: false);

        Assert.That(table.Rows.Count, Is.EqualTo(2));
        Assert.That(table.Rows[0]["Name"], Is.EqualTo("Ada\r\nLovelace"));
        Assert.Throws<ObjectDisposedException>(() => input.Peek());
    }

    [Test]
    public void ToDataTable_DisposesOwnedInputWhenParsingFails()
    {
        using var input = new StringReader("Id;Name\r\n1;\"unterminated");

        Assert.Throws<InvalidDataException>(() => CreateCsv().ToDataTable(input, leaveOpen: false));
        Assert.Throws<ObjectDisposedException>(() => input.Peek());
    }

    [TestCase(nameof(CsvReader.ToArrayString))]
    [TestCase(nameof(CsvReader.ToDynamic))]
    [TestCase(nameof(CsvReader.ToDictionaries))]
    [TestCase(nameof(CsvReader.ToObjectArrays))]
    public void RowMaterializers_AreLazyAndDisposeOwnedInputWhenEnumerationStops(string method)
    {
        using var input = new StringReader(Input);
        var csv = CreateCsv();
        IEnumerable<object> rows = method switch
        {
            nameof(CsvReader.ToArrayString) => csv.ToArrayString(input, leaveOpen: false),
            nameof(CsvReader.ToDynamic) => csv.ToDynamic(input, leaveOpen: false),
            nameof(CsvReader.ToDictionaries) => csv.ToDictionaries(input, leaveOpen: false),
            nameof(CsvReader.ToObjectArrays) => csv.ToObjectArrays(input, leaveOpen: false),
            _ => throw new ArgumentOutOfRangeException(nameof(method))
        };

        Assert.That(input.Peek(), Is.EqualTo((int)'I'));
        object row = rows.First();
        switch (row)
        {
            case string[] strings:
                Assert.That(strings, Is.EqualTo(new[] { "1", "Ada\r\nLovelace" }));
                break;
            case object[] values:
                Assert.That(values, Is.EqualTo(new object[] { 1, "Ada\r\nLovelace" }));
                break;
            case IDictionary<string, object?> fields:
                Assert.That(fields["Id"], Is.EqualTo(1));
                Assert.That(fields["Name"], Is.EqualTo("Ada\r\nLovelace"));
                break;
            default:
                Assert.Fail($"Unexpected result type {row.GetType()}.");
                break;
        }
        Assert.Throws<ObjectDisposedException>(() => input.Peek());
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task TypedMapping_RespectsFiltersAndRowWindow(bool asynchronous)
    {
        var csv = Builder()
            .WithFilter<int>("Id", id => id < 3)
            .WithSkip(1)
            .WithTake(1)
            .Build();
        using var input = new StringReader(Input + "\r\n3;Katherine");
        var people = new List<Person>();

        if (asynchronous)
        {
            await foreach (var person in csv.ToAsync<Person>(input))
                people.Add(person);
        }
        else
        {
            people.AddRange(csv.To<Person>(input));
        }

        Assert.That(people, Is.EqualTo(new[] { new Person(2, "Grace") }));
        Assert.DoesNotThrow(() => input.Peek());
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task TypedMapping_InvokesCustomMapperAndDisposesOwnedInputOnEarlyExit(bool asynchronous)
    {
        var csv = CreateCsv();
        using var input = new StringReader(Input);
        string? name = null;

        if (asynchronous)
        {
            await foreach (var value in csv.ToAsync<string>(input, MapName, leaveOpen: false))
            {
                name = value;
                break;
            }
        }
        else
        {
            name = csv.To<string>(input, MapName, leaveOpen: false).First();
        }

        Assert.That(name, Is.EqualTo("ADA\r\nLOVELACE"));
        Assert.Throws<ObjectDisposedException>(() => input.Peek());
    }

    [Test]
    public async Task ToAsync_UsesOnlyAsyncIOAndMapsDefaultType()
    {
        using var input = new AsyncOnlyReader(Input);
        var rows = CreateCsv().ToAsync<Person>(input);
        await using var enumerator = rows.GetAsyncEnumerator();

        Assert.That(input.ReadCount, Is.Zero);
        Assert.That(await enumerator.MoveNextAsync(), Is.True);
        Assert.That(enumerator.Current, Is.EqualTo(new Person(1, "Ada\r\nLovelace")));
        Assert.That(await enumerator.MoveNextAsync(), Is.True);
        Assert.That(enumerator.Current, Is.EqualTo(new Person(2, "Grace")));
        Assert.That(await enumerator.MoveNextAsync(), Is.False);
        Assert.That(input.IsDisposed, Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ToAsync_ForwardsEnumeratorCancellationAndHonorsOwnership(bool leaveOpen)
    {
        using var input = new AsyncOnlyReader(null);
        using var cancellation = new CancellationTokenSource();
        await using var enumerator = CreateCsv().ToAsync<Person>(input, leaveOpen: leaveOpen)
            .GetAsyncEnumerator(cancellation.Token);
        var pending = enumerator.MoveNextAsync().AsTask();

        Assert.That(input.ReadCount, Is.EqualTo(1));
        Assert.That(input.ReadToken, Is.EqualTo(cancellation.Token));
        cancellation.Cancel();

        Assert.That(async () => await pending, Throws.InstanceOf<OperationCanceledException>());
        Assert.That(input.IsDisposed, Is.EqualTo(!leaveOpen));
    }

    private static CsvReader CreateCsv() => Builder().Build();

    private static CsvReaderBuilder Builder()
        => new CsvReaderBuilder()
            .WithDialect(d => d.WithHeader().WithDelimiter(';').WithLineTerminator("\r\n"))
            .WithSchema(s => s.Indexed().WithField<int>().WithField<string>())
            .WithParserOptimizations(new(BufferSize: 5))
            .WithStreamInitialization(new(InputMode: CsvInputMode.Utf8Bytes));

    private static string MapName(ReadOnlySpan<char> record, IEnumerable<FieldSpan> fields)
    {
        var field = fields.ElementAt(1).Value;
        return record.Slice(field.Start, field.Length).ToString().ToUpperInvariant();
    }

    private sealed record Person(int Id, string Name);

    private sealed class TrackingReader(string text) : StringReader(text)
    {
        internal int DisposeCount { get; private set; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                DisposeCount++;
            base.Dispose(disposing);
        }
    }

    private sealed class AsyncOnlyReader(string? text) : TextReader
    {
        private readonly StringReader? _reader = text is null ? null : new(text);
        internal int ReadCount { get; private set; }
        internal CancellationToken ReadToken { get; private set; }
        internal bool IsDisposed { get; private set; }

        public override int Read(Span<char> buffer)
            => throw new InvalidOperationException("Synchronous read used.");

        public override async ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
        {
            ReadCount++;
            ReadToken = cancellationToken;
            if (_reader is null)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return 0;
            }
            return await _reader.ReadAsync(buffer[..Math.Min(2, buffer.Length)], cancellationToken);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                IsDisposed = true;
                _reader?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
