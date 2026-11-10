using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing;

public class ReaderFilteringTest
{
    private static MemoryStream Input(string text) => new(Encoding.UTF8.GetBytes(text));
    private static CsvReaderBuilder Builder() => new CsvReaderBuilder().WithDialect(d => d.WithHeader().WithLineTerminator("\n"));

    [Test]
    public void Filters_RunInOrder_AndSkipBeforeProjection()
    {
        var calls = new List<string>();
        var csv = Builder()
            .WithFilter("Status", "Active")
            .WithFilter("Country", value => { calls.Add("span"); return value.SequenceEqual("BE"); })
            .WithFilter<int>("Age", age => { calls.Add("typed"); return age >= 18; })
            .WithRowFilter(row => { calls.Add("row"); return row.GetFieldValue<int>("Age") > row.GetFieldValue<int>(3); })
            .Build();
        using var input = Input("Status,Country,Age,Limit\nInactive,BE,bad,0\nActive,FR,bad,0\nActive,BE,17,0\nActive,BE,20,21\nActive,BE,22,21");
        var result = csv.ToArrayString(input).ToArray();
        Assert.That(result, Has.Length.EqualTo(1));
        Assert.That(result[0][2], Is.EqualTo("22"));
        Assert.That(calls, Is.EqualTo(new[] { "span", "span", "typed", "span", "typed", "row", "span", "typed", "row" }));
    }

    [Test]
    public async Task FileQueryAppendsRowFiltersAndUsesTheSameParsingRules()
    {
        var file = FileSource.Partitioned("orders.csv", true, () => Input(
            "Channel,Status,Quantity\n" +
            "Store,Ready,bad\n" +
            "Web,Hold,bad\n" +
            "Web,Ready,NULL\n" +
            "Web,Ready,1\n" +
            "Web,Ready,2\n"));
        var query = Builder()
            .WithDialect(d => d.WithNullSequence("NULL"))
            .WithFilter("Channel", "Web")
            .FromFiles(new[] { file }).Build()
            .WithFilter(1, status => status.SequenceEqual("Ready"))
            .WithFilter<int>("Quantity", quantity => quantity >= 2);

        await using var reader = query.ToDataReader();
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetInt32(2), Is.EqualTo(2));
        Assert.That(await reader.ReadAsync(), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Equality_DecodesQuotesAndEscapes(bool async)
    {
        var csv = Builder().WithDialect(d => d.WithEscapeChar('\\')).WithFilter(0, "a\"b").Build();
        using var reader = csv.ToDataReader(Input("Value\n\"a\"\"b\"\n\"a\\\"b\"\nother"));
        var count = 0;
        while (async ? await reader.ReadAsync() : reader.Read())
        {
            Assert.That(reader.GetString(0), Is.EqualTo("a\"b"));
            count++;
        }
        Assert.That(count, Is.EqualTo(2));
    }

    [Test]
    public void QuotedAndUnquotedCells_AreEquivalent_AndComparisonIsConfigurable()
    {
        var csv = Builder().WithFilter("Value", "be", StringComparison.OrdinalIgnoreCase).Build();
        Assert.That(csv.ToArrayString(Input("Value\nBE\n\"BE\"\nFR")).Count(), Is.EqualTo(2));
    }

    [Test]
    public void FormatsAndExplicitTryParse_UseExistingGetterRules()
    {
        var csv = Builder().WithDialect(d => d.WithDelimiter(';'))
            .WithSchema(s => s.Named().WithNumberField<decimal>("Amount", f => f.WithFormat(format => format.WithDecimalChar(','))))
            .WithFilter<decimal>("Amount", value => value > 1.5m).Build();
        using var reader = csv.ToDataReader(Input("Amount\n1,2\n2,5"));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetDecimal(0), Is.EqualTo(2.5m));
        Assert.That(reader.Read(), Is.False);
        csv = Builder().WithFilter(0, value => int.TryParse(value, out var number) && number > 1).Build();
        Assert.That(csv.ToArrayString(Input("Value\nbad\n1\n2")).Count(), Is.EqualTo(1));
    }

    [Test]
    public void NullAndEmpty_HaveDistinctSemantics()
    {
        var seen = new List<string?>();
        var csv = Builder().WithDialect(d => d.WithNullSequence("NULL"))
            .WithFilter<string?>(0, value => { seen.Add(value); return true; }).Build();
        Assert.That(csv.ToArrayString(Input("Value\nNULL\n\"\"\ntext")).Count(), Is.EqualTo(3));
        Assert.That(seen, Is.EqualTo(new string?[] { null, "", "text" }));
        var spanCalls = 0;
        csv = Builder().WithDialect(d => d.WithNullSequence("NULL"))
            .WithFilter(0, value => { spanCalls++; return value.IsEmpty; }).Build();
        Assert.That(csv.ToArrayString(Input("Value\nNULL\n\"\"\ntext")).Count(), Is.EqualTo(1));
        Assert.That(spanCalls, Is.EqualTo(2));
        csv = Builder().WithDialect(d => d.WithNullSequence("NULL")).WithFilter<int?>(0, value => value is null || value > 1).Build();
        Assert.That(csv.ToArrayString(Input("Value\nNULL\n1\n2")).Count(), Is.EqualTo(2));
        var typedCalls = 0;
        csv = Builder().WithDialect(d => d.WithNullSequence("NULL")).WithFilter<int>(0, value => { typedCalls++; return true; }).Build();
        Assert.That(csv.ToArrayString(Input("Value\nNULL\n1")).Count(), Is.EqualTo(1));
        Assert.That(typedCalls, Is.EqualTo(1));
    }

    public record Number(int Value);

    [TestCase(false)]
    [TestCase(true)]
    public async Task ParsedValues_AreSharedByFiltersAndObjectProjection(bool async)
    {
        var calls = 0;
        ParseSpan<int> parse = span => { calls++; return int.Parse(span); };
        var csv = Builder().WithParsers(p => p.WithParser(parse))
            .WithFilter<int>(0, value => value > 1)
            .WithRowFilter(row => row.GetFieldValue<int>(0) < 4).Build();
        var values = new List<Number>();
        using var input = Input("Value\n1\n2\n3\n4");
        if (async)
        {
            await foreach (var value in csv.ToAsync<Number>(input)) values.Add(value);
        }
        else
        {
            values.AddRange(csv.To<Number>(input));
        }
        Assert.That(values, Is.EqualTo(new[] { new Number(2), new Number(3) }));
        Assert.That(calls, Is.EqualTo(4));
    }

    [Test]
    public void Cache_SeparatesTypesAndParserOverrides_AndInvalidatesRejectedRows()
    {
        var calls = 0;
        ParseSpan<int> normal = span => { calls++; return int.Parse(span); };
        var overrides = 0;
        ParseSpan<int> other = span => { overrides++; return int.Parse(span) + 10; };
        var csv = Builder().WithParsers(p => p.WithParser(normal)).WithRowFilter(row =>
        {
            Assert.That(row.GetFieldValue<int>(0, normal), Is.EqualTo(row.GetFieldValue<int>(0)));
            Assert.That(row.GetFieldValue<int>(0, other), Is.EqualTo(row.GetFieldValue<int>(0) + 10));
            Assert.That(row.GetFieldValue<long>(0), Is.EqualTo(row.GetFieldValue<int>(0)));
            return row.GetFieldValue<int>(0) > 1;
        }).Build();
        using var reader = csv.ToDataReader(Input("Value\n1\n2\n3"));
        while (reader.Read())
        {
            Assert.That(reader.GetInt32(0), Is.EqualTo(reader.GetFieldValue<int>(0)));
            Assert.That(reader.GetFieldValue<int>(0, other), Is.EqualTo(reader.GetInt32(0) + 10));
        }
        Assert.That(calls, Is.EqualTo(3));
        Assert.That(overrides, Is.EqualTo(3));
    }

    [Test]
    public void SchemaParser_IsSharedWithObjectArraysAndDynamic()
    {
        var calls = 0;
        ParseSpan<int> parse = span => { calls++; return int.Parse(span); };
        var csv = Builder().WithSchema(s => s.Named().WithField<int>("Value", f => f.WithParser(parse)))
            .WithFilter<int>("Value", value => value > 0).Build();
        Assert.That(csv.ToObjectArrays(Input("Value\n1\n2")).Select(row => row[0]), Is.EqualTo(new[] { 1, 2 }));
        Assert.That(calls, Is.EqualTo(2));
        calls = 0;
        Assert.That(csv.ToDynamic(Input("Value\n1\n2")).Count(), Is.EqualTo(2));
        Assert.That(calls, Is.EqualTo(2));
    }

    [Test]
    public void Errors_Propagate_AndViewsCannotAdvanceOrOutlivePredicate()
    {
        IRowView? retained = null;
        var csv = Builder().WithRowFilter(row => { retained = row; Assert.That(row, Is.Not.InstanceOf<System.Data.IDataReader>()); return true; }).Build();
        using var reader = csv.ToDataReader(Input("Value\n1"));
        Assert.That(reader.Read(), Is.True);
        Assert.Throws<InvalidOperationException>(() => retained!.GetFieldValue<int>(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Builder().WithFilter("Missing", "x").Build().ToArrayString(Input("Value\n1")).ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => Builder().WithFilter(1, "x").Build().ToArrayString(Input("Value\n1")).ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => Builder().WithFilter(-1, "x"));
        Assert.Throws<FormatException>(() => Builder().WithFilter<int>(0, _ => true).Build().ToArrayString(Input("Value\nbad")).ToArray());
        Assert.Throws<ApplicationException>(() => Builder().WithRowFilter(_ => throw new ApplicationException()).Build().ToArrayString(Input("Value\n1")).ToArray());
    }

    [TestCase("")]
    [TestCase("Value\n")]
    [TestCase("Value\n1\n2")]
    public async Task EmptyOrAllRejected_SyncAndAsyncReachEof(string text)
    {
        var csv = Builder().WithRowFilter(_ => false).Build();
        using var sync = csv.ToDataReader(Input(text));
        Assert.That(sync.Read(), Is.False);
        using var asyncReader = csv.ToDataReader(Input(text));
        Assert.That(await asyncReader.ReadAsync(), Is.False);
    }

    [Test]
    public void Cancellation_IsCheckedBetweenRejectedRows()
    {
        using var cancellation = new CancellationTokenSource();
        var csv = Builder().WithRowFilter(_ => { cancellation.Cancel(); return false; }).Build();
        using var reader = csv.ToDataReader(Input("Value\n1\n2"));
        Assert.ThrowsAsync<OperationCanceledException>(async () => await reader.ReadAsync(cancellation.Token));
    }

    [Test]
    public void Batches_RebindHeaders_AndPreserveSourceRowNumbers()
    {
        var csv = Builder().WithDialect(d => d.WithHeaderRepeat()).WithRowNumber()
            .WithFilter<int>("Value", value => value % 2 == 0).Build();
        using var reader = csv.ToDataReader(new Stream[] { Input("Value,Other\n1,a\n2,b"), Input("Other,Value\nc,3\nd,4") });
        var values = new List<int>();
        var numbers = new List<int>();
        while (reader.Read())
        {
            values.Add(reader.GetFieldValue<int>(reader.GetOrdinal("Value")));
            numbers.Add(reader.GetInt32(0));
        }
        Assert.That(values, Is.EqualTo(new[] { 2, 4 }));
        Assert.That(numbers, Is.EqualTo(new[] { 2, 4 }));
    }

    [Test]
    public void DataTable_FiltersBeforeMaterialization_AndNoFiltersKeepAllRows()
    {
        Assert.That(Builder().WithFilter<int>(0, x => x > 1).Build().ToDataTable(Input("Value\n1\n2")).Rows.Count, Is.EqualTo(1));
        Assert.That(Builder().Build().ToArrayString(Input("Value\n1\n2")).Count(), Is.EqualTo(2));
    }

    [TestCase(CsvInputMode.DecodedChars)]
    [TestCase(CsvInputMode.Utf8Bytes)]
    public async Task Filtering_PreservesAsyncOnlyIo_AndSourcePositions(CsvInputMode mode)
    {
        var csv = Builder().WithPositionTracking().WithStreamInitialization(new(InputMode: mode))
            .WithFilter<int>(0, value => value > 1).Build();
        await using var stream = new AsyncOnlyStream(Encoding.UTF8.GetBytes("Value\n1\n2"));
        await using var reader = csv.ToDataReader(stream);
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetFieldValue<int>(0), Is.EqualTo(2));
        Assert.That(reader.CurrentPosition!.Value.RecordNumber, Is.EqualTo(2));
        Assert.That(reader.CurrentPosition!.Value.PhysicalLine, Is.EqualTo(3));
        Assert.That(await reader.ReadAsync(), Is.False);
        Assert.That(reader.CurrentPosition, Is.Null);
    }

    [Test]
    public async Task AsyncBatches_InvalidateParsedValuesBetweenStreams()
    {
        var calls = 0;
        ParseSpan<int> parse = span => { calls++; return int.Parse(span); };
        var csv = Builder().WithParsers(p => p.WithParser(parse)).WithFilter<int>(0, value => value > 0).Build();
        await using var reader = csv.ToDataReader(new Stream[] { Input("Value\n1"), Input("Value\n2") });
        var values = new List<int>();
        while (await reader.ReadAsync()) values.Add(reader.GetFieldValue<int>(0));
        Assert.That(values, Is.EqualTo(new[] { 1, 2 }));
        Assert.That(calls, Is.EqualTo(2));
    }

    [Test]
    public void CustomMapperAndLegacyArrays_OnlyMaterializeAcceptedRows()
    {
        var csv = Builder().WithFilter<int>(0, value => value > 1).Build();
        var calls = 0;
        SpanMapper<int> mapper = (span, fields) => { calls++; return 42; };
        Assert.That(csv.To(Input("Value\n1\n2"), mapper).ToArray(), Is.EqualTo(new[] { 42 }));
        Assert.That(calls, Is.EqualTo(1));
        using var arrays = new CsvArrayString(Input("Value\n1\n2"), csv.Profile);
        Assert.That(arrays.Read().Single(), Is.EqualTo(new[] { "2" }));
    }

    private sealed class AsyncOnlyStream(byte[] content) : Stream
    {
        private readonly MemoryStream _inner = new(content);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Synchronous read used.");
        public override int Read(Span<byte> buffer) => throw new InvalidOperationException("Synchronous read used.");
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => _inner.ReadAsync(buffer, cancellationToken);
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
}
