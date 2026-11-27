using System.Text;
using Apache.Arrow;
using Apache.Arrow.Types;
using NUnit.Framework;
using PocketCsvReader.Configuration;
using PocketCsvReader.Json;
using PocketCsvReader.Json.Configuration;

namespace PocketCsvReader.Arrow.Testing;

[TestFixture]
public class ArrowReaderExtensionsTests
{
    [Test]
    public void ToArrowBatches_WithCsvProjection_UsesProjectedSchema()
    {
        var csv = new CsvReaderBuilder()
            .WithDialect(d => d.WithDelimiter(',').WithLineTerminator("\n").WithHeader())
            .WithSchema(s => s.Indexed().WithField<int>().WithField<int>().WithField<string>())
            .WithProjection("Name", "Id")
            .Build();
        using var stream = StreamOf("Id,Unused,Name\n7,not-an-int,Ada");
        using var batch = csv.ToArrowBatches(stream).Single();

        Assert.That(batch.Schema.FieldsList.Select(field => field.Name), Is.EqualTo(new[] { "Name", "Id" }));
        Assert.That(((StringArray)batch.Column(0)).GetString(0), Is.EqualTo("Ada"));
        Assert.That(((Int32Array)batch.Column(1)).GetValue(0), Is.EqualTo(7));
    }

    [Test]
    public void ToArrowBatches_WithNumericPrimitives_PreservesWidthsAndValues()
    {
        using var stream = StreamOf(
            "i8,u8,i16,u16,i32,u32,i64,u64,f32,f64\n-1,2,-3,4,-5,6,-7,8,1.5,2.5");
        var reader = new CsvReaderBuilder()
            .WithDialect(dialect => dialect.WithDelimiter(',').WithLineTerminator("\n").WithHeader())
            .WithSchema(schema => schema.Named()
                .WithField<sbyte>("i8")
                .WithField<byte>("u8")
                .WithField<short>("i16")
                .WithField<ushort>("u16")
                .WithField<int>("i32")
                .WithField<uint>("u32")
                .WithField<long>("i64")
                .WithField<ulong>("u64")
                .WithField<float>("f32")
                .WithField<double>("f64"))
            .Build();

        using var batch = reader.ToArrowBatches(stream).Single();

        Assert.Multiple(() =>
        {
            Assert.That(((Int8Array)batch.Column(0)).GetValue(0), Is.EqualTo(-1));
            Assert.That(((UInt8Array)batch.Column(1)).GetValue(0), Is.EqualTo(2));
            Assert.That(((Int16Array)batch.Column(2)).GetValue(0), Is.EqualTo(-3));
            Assert.That(((UInt16Array)batch.Column(3)).GetValue(0), Is.EqualTo(4));
            Assert.That(((Int32Array)batch.Column(4)).GetValue(0), Is.EqualTo(-5));
            Assert.That(((UInt32Array)batch.Column(5)).GetValue(0), Is.EqualTo(6));
            Assert.That(((Int64Array)batch.Column(6)).GetValue(0), Is.EqualTo(-7));
            Assert.That(((UInt64Array)batch.Column(7)).GetValue(0), Is.EqualTo(8));
            Assert.That(((FloatArray)batch.Column(8)).GetValue(0), Is.EqualTo(1.5f));
            Assert.That(((DoubleArray)batch.Column(9)).GetValue(0), Is.EqualTo(2.5d));
        });
    }

    [Test]
    public void ToArrowBatches_WithUtf8FloatingPoint_PreservesFloatingPointBits()
    {
        var schema = new SchemaDescriptor.NamedSchemaDescriptor();
        schema.Fields.Add(new FieldDescriptor(typeof(float), "f32"));
        schema.Fields.Add(new FieldDescriptor(typeof(double), "f64"));
        var reader = new CsvReaderBuilder()
            .WithDialect(d => d.WithLineTerminator("\n").WithHeader())
            .WithSchema(schema)
            .WithStreamInitialization(StreamInitializationOptions.Utf8)
            .Build();
        using var stream = StreamOf("f32,f64\n\"1,234.5\",1.25e-5\n-0,-0\nNaN,-Infinity");
        using var batch = reader.ToArrowBatches(stream).Single();

        Assert.That(batch.Schema.GetFieldByIndex(0).DataType, Is.TypeOf<FloatType>());
        Assert.That(batch.Schema.GetFieldByIndex(1).DataType, Is.TypeOf<DoubleType>());
        var singles = (FloatArray)batch.Column(0);
        var doubles = (DoubleArray)batch.Column(1);
        Assert.That(singles.GetValue(0), Is.EqualTo(1234.5f));
        Assert.That(doubles.GetValue(0), Is.EqualTo(1.25e-5));
        Assert.That(singles.GetValue(1), Is.Not.Null);
        Assert.That(doubles.GetValue(1), Is.Not.Null);
        Assert.That(BitConverter.SingleToInt32Bits(singles.GetValue(1)!.Value), Is.EqualTo(int.MinValue));
        Assert.That(BitConverter.DoubleToInt64Bits(doubles.GetValue(1)!.Value), Is.EqualTo(long.MinValue));
        Assert.That(singles.GetValue(2), Is.EqualTo(float.NaN));
        Assert.That(doubles.GetValue(2), Is.EqualTo(double.NegativeInfinity));
    }

    [Test]
    public void ToArrowBatches_WithTypedSchema_ProducesTypedBoundedBatches()
    {
        const string content = "id,active,name,amount,date,instant,time,payload\n" +
            "1,true,Alice,12.34,2026-10-08,2026-10-08T07:30:00,07:30:01,AQI=\n" +
            "2,false,NULL,0.5,2026-10-09,2026-10-09T08:31:00,08:31:02,AwQ=\n" +
            "3,true,Carol,-4.25,2026-10-10,2026-10-10T09:32:00,09:32:03,BQY=";
        using var stream = StreamOf(content);

        var batches = CreateCsvReader()
            .ToArrowBatches(stream, batchSize: 2)
            .ToArray();

        try
        {
            Assert.That(batches.Select(batch => batch.Length), Is.EqualTo(new[] { 2, 1 }));
            Assert.That(batches[0].Schema.FieldsList.Select(field => field.Name),
                Is.EqualTo(new[] { "id", "active", "name", "amount", "date", "instant", "time", "payload" }));
            Assert.Multiple(() =>
            {
                Assert.That(batches[0].Schema.GetFieldByIndex(0).DataType, Is.TypeOf<Int32Type>());
                Assert.That(batches[0].Schema.GetFieldByIndex(1).DataType, Is.TypeOf<BooleanType>());
                Assert.That(batches[0].Schema.GetFieldByIndex(2).DataType, Is.TypeOf<StringType>());
                Assert.That(batches[0].Schema.GetFieldByIndex(3).DataType, Is.TypeOf<Decimal128Type>());
                Assert.That(batches[0].Schema.GetFieldByIndex(4).DataType, Is.TypeOf<Date32Type>());
                Assert.That(batches[0].Schema.GetFieldByIndex(5).DataType, Is.TypeOf<TimestampType>());
                Assert.That(batches[0].Schema.GetFieldByIndex(6).DataType, Is.TypeOf<Time64Type>());
                Assert.That(batches[0].Schema.GetFieldByIndex(7).DataType, Is.TypeOf<BinaryType>());
            });

            var ids = (Int32Array)batches[0].Column(0);
            var names = (StringArray)batches[0].Column(2);
            var amounts = (Decimal128Array)batches[0].Column(3);
            var dates = (Date32Array)batches[0].Column(4);
            var instants = (TimestampArray)batches[0].Column(5);
            var times = (Time64Array)batches[0].Column(6);
            var payloads = (BinaryArray)batches[0].Column(7);

            Assert.Multiple(() =>
            {
                Assert.That(ids.GetValue(0), Is.EqualTo(1));
                Assert.That(names.GetString(0), Is.EqualTo("Alice"));
                Assert.That(names.IsNull(1), Is.True);
                Assert.That(amounts.GetValue(0), Is.EqualTo(12.34m));
                Assert.That(dates.GetDateOnly(0), Is.EqualTo(new DateOnly(2026, 10, 8)));
                Assert.That(instants.GetTimestamp(0), Is.EqualTo(new DateTimeOffset(2026, 10, 8, 7, 30, 0, TimeSpan.Zero)));
                Assert.That(times.GetTime(0), Is.EqualTo(new TimeOnly(7, 30, 1)));
                Assert.That(payloads.GetBytes(0).ToArray(), Is.EqualTo(new byte[] { 1, 2 }));
            });
        }
        finally
        {
            foreach (var batch in batches)
                batch.Dispose();
        }
    }

    [Test]
    public async Task ToArrowBatchesAsync_WithNonSeekableStream_ReadsAllBatches()
    {
        await using var stream = new NonSeekableStream(StreamOf("id,name\n1,Alice\n2,Bob\n3,Carol"));
        var batches = new List<RecordBatch>();

        await foreach (var batch in CreateSimpleCsvReader().ToArrowBatchesAsync(stream, batchSize: 2))
            batches.Add(batch);

        try
        {
            Assert.That(batches.Select(batch => batch.Length), Is.EqualTo(new[] { 2, 1 }));
            Assert.That(((StringArray)batches[1].Column(1)).GetString(0), Is.EqualTo("Carol"));
        }
        finally
        {
            foreach (var batch in batches)
                batch.Dispose();
        }
    }

    [Test]
    public void ToArrowBatches_WithJsonReader_UsesSharedReaderAbstraction()
    {
        using var stream = StreamOf("[{\"id\":1,\"name\":\"Alice\"},{\"id\":2,\"name\":\"Bob\"}]");
        var profile = new JsonProfile(
            new SchemaDescriptorBuilder().Named()
                .WithField<int>("id")
                .WithField<string>("name")
                .Build());
        var reader = new JsonReader(profile);

        using var batch = reader.ToArrowBatches(stream).Single();

        Assert.That(batch.Length, Is.EqualTo(2));
        Assert.That(((Int32Array)batch.Column(0)).GetValue(1), Is.EqualTo(2));
        Assert.That(((StringArray)batch.Column(1)).GetString(1), Is.EqualTo("Bob"));
    }

    [Test]
    public void ToArrowBatches_WithEmptyInput_ProducesNoBatch()
    {
        using var stream = StreamOf(string.Empty);

        var batches = CreateSimpleCsvReader().ToArrowBatches(stream).ToArray();

        Assert.That(batches, Is.Empty);
    }

    [Test]
    public void ToArrowBatches_WithInvalidBatchSize_Throws()
    {
        using var stream = StreamOf("id,name\n1,Alice");

        Assert.That(
            () => CreateSimpleCsvReader().ToArrowBatches(stream, batchSize: 0),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void ToArrowBatches_WithoutTypedSchema_ThrowsActionableError()
    {
        using var stream = StreamOf("value\nabc");
        var reader = new CsvReaderBuilder()
            .WithDialect(dialect => dialect.WithDelimiter(',').WithLineTerminator("\n").WithHeader())
            .Build();

        Assert.That(
            () => reader.ToArrowBatches(stream).ToArray(),
            Throws.TypeOf<NotSupportedException>().With.Message.Contains("Configure the PocketCsvReader schema"));
    }

    [Test]
    public async Task ToArrowBatchesAsync_WhenCancelled_ThrowsOperationCancelled()
    {
        await using var stream = new NonSeekableStream(StreamOf("id,name\n1,Alice"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        async Task Enumerate()
        {
            await foreach (var batch in CreateSimpleCsvReader()
                .ToArrowBatchesAsync(stream, cancellationToken: cancellation.Token))
            {
                batch.Dispose();
            }
        }

        Assert.That(Enumerate, Throws.TypeOf<OperationCanceledException>());
    }

    [Test]
    public void ToArrowBatches_WithMalformedRecord_PropagatesParserFailure()
    {
        using var stream = StreamOf("id,name\n1,Alice,unexpected");

        Assert.That(
            () => CreateSimpleCsvReader().ToArrowBatches(stream).ToArray(),
            Throws.TypeOf<InvalidDataException>());
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ToArrowBatches_WithNativeUtf8_PreservesLogicalValuesAndOwnsBuffers(bool asynchronous)
    {
        var longText = new string('x', 89) + "😀";
        var content = $"id,text,alias,parsed\n1,  ASCII,NULL,first\n2,\"é漢😀\",,second\n"
            + $"3,\"line\n{longText}\"\"end\",alias,third\n4,\"\",keep,fourth\n5,";
        byte[] bytes = [.. Encoding.UTF8.GetBytes(content), 0xFF, .. ",keep,last"u8];
        var csv = new CsvReaderBuilder()
            .WithDialect(d => d.WithHeader().WithLineTerminator("\n").WithSkipInitialSpace())
            .WithSchema(s => s.Named().WithField<int>("id").WithField<string>("text")
                .WithField<string>("alias", f => f.WithSequence("NULL", null).WithSequence("alias", "actual"))
                .WithField<string>("parsed", f => f.WithParser((string text) => text.ToUpperInvariant())))
            .WithParserOptimizations(new ParserOptimizationOptions(BufferSize: 32))
            .WithStreamInitialization(StreamInitializationOptions.Utf8)
            .Build();

        using var stream = new NonSeekableStream(new MemoryStream(bytes));
        var batches = new List<RecordBatch>();
        try
        {
            if (asynchronous)
            {
                await foreach (var batch in csv.ToArrowBatchesAsync(stream, batchSize: 2))
                    batches.Add(batch);
            }
            else
            {
                batches.AddRange(csv.ToArrowBatches(stream, batchSize: 2));
            }

            // Inspect the earliest batch after the reader has advanced and been disposed.
            Assert.That(batches.Select(batch => batch.Length), Is.EqualTo(new[] { 2, 2, 1 }));
            Assert.That(batches[0].Schema.FieldsList.Select(field => field.Name), Is.EqualTo(new[] { "id", "text", "alias", "parsed" }));
            Assert.That(batches.SelectMany(batch => ((Int32Array)batch.Column(0)).Values.ToArray()), Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));
            Assert.That(StringValues(batches, 1), Is.EqualTo(new[] { "ASCII", "é漢😀", $"line\n{longText}\"end", "", "�" }));
            Assert.That(StringValues(batches, 2), Is.EqualTo(new[] { null, "", "actual", "keep", "keep" }));
            Assert.That(StringValues(batches, 3), Is.EqualTo(new[] { "FIRST", "SECOND", "THIRD", "FOURTH", "LAST" }));
        }
        finally
        {
            foreach (var batch in batches)
                batch.Dispose();
        }
    }

    [Test]
    public void TryGetUtf8Span_WithNativeUtf8_UsesProjectedLogicalTextAndRejectsFallbackValues()
    {
        var csv = new CsvReaderBuilder()
            .WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
            .WithSchema(s => s.Indexed().WithField<string>()
                .WithField<string>(f => f.WithSequence("NULL", null)).WithField<string>())
            .WithProjection("C", "B", "A")
            .WithStreamInitialization(StreamInitializationOptions.Utf8)
            .Build();
        using var stream = StreamOf("A,B,C\n,A,\"say \"\"é\"\"\"\nnext,NULL,");
        using var reader = csv.ToDataReader(stream);
        IUtf8DataRecord utf8 = reader;

        Assert.That(utf8.TryGetUtf8Span(0, out _), Is.False);
        Assert.That(reader.Read(), Is.True);
        Assert.That(utf8.TryGetUtf8Span(0, out var text), Is.True);
        Assert.That(text.SequenceEqual("say \"é\""u8), Is.True);
        Assert.That(utf8.TryGetUtf8Span(2, out var empty), Is.True);
        Assert.That(empty.IsEmpty, Is.True);

        Assert.That(reader.Read(), Is.True);
        Assert.That(utf8.TryGetUtf8Span(1, out _), Is.False);
        Assert.That(utf8.IsDBNull(1), Is.True);
        Assert.That(reader.Read(), Is.False);
        Assert.That(utf8.TryGetUtf8Span(0, out _), Is.False);
    }

    [Test]
    public void ToArrowBatches_WithUtf8Capability_SkipsStringAccessAndNullCheckForBorrowedText()
    {
        var schema = new SchemaDescriptorBuilder().Named()
            .WithField<string>("borrowed").WithField<string>("fallback")
            .WithField<char>("character").WithField<Guid>("guid").Build();
        var csv = new BorrowingCsvReader(new CsvProfile(
            new DialectDescriptorBuilder().WithHeader().WithLineTerminator("\n").Build(), schema));
        using var stream = StreamOf("borrowed,fallback,character,guid\nunused,legacy,A,6f1a911c-7aa8-4e8b-9c3e-d6eedbe0c554");
        using var batch = csv.ToArrowBatches(stream).Single();

        Assert.That(((StringArray)batch.Column(0)).GetString(0), Is.EqualTo("borrowed é"));
        Assert.That(((StringArray)batch.Column(1)).GetString(0), Is.EqualTo("legacy"));
        Assert.That(((StringArray)batch.Column(2)).GetString(0), Is.EqualTo("A"));
        Assert.That(((StringArray)batch.Column(3)).GetString(0), Is.EqualTo("6f1a911c-7aa8-4e8b-9c3e-d6eedbe0c554"));
    }

    [Test]
    public void ToArrowBatches_WithUtf16Bom_UsesDecodedText()
    {
        var csv = new CsvReaderBuilder()
            .WithDialect(d => d.WithHeader().WithLineTerminator("\n"))
            .WithSchema(s => s.Named().WithField<string>("text"))
            .WithStreamInitialization(new StreamInitializationOptions(InputMode: CsvInputMode.Auto))
            .Build();
        byte[] bytes = [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes("text\nété 😀")];
        using var stream = new MemoryStream(bytes);
        using var batch = csv.ToArrowBatches(stream).Single();

        Assert.That(((StringArray)batch.Column(0)).GetString(0), Is.EqualTo("été 😀"));
    }

    private static IEnumerable<string?> StringValues(IEnumerable<RecordBatch> batches, int ordinal)
    {
        foreach (var batch in batches)
        {
            var column = (StringArray)batch.Column(ordinal);
            for (int i = 0; i < column.Length; i++)
                yield return column.GetString(i);
        }
    }

    private static CsvReader CreateSimpleCsvReader()
        => new CsvReaderBuilder()
            .WithDialect(dialect => dialect.WithDelimiter(',').WithLineTerminator("\n").WithHeader())
            .WithSchema(schema => schema.Named()
                .WithField<int>("id")
                .WithField<string>("name"))
            .Build();

    private static CsvReader CreateCsvReader()
        => new CsvReaderBuilder()
            .WithDialect(dialect => dialect
                .WithDelimiter(',')
                .WithLineTerminator("\n")
                .WithHeader()
                .WithNullSequence("NULL"))
            .WithSchema(schema => schema.Named()
                .WithField<int>("id")
                .WithField<bool>("active")
                .WithField<string>("name")
                .WithNumberField<decimal>("amount")
                .WithTemporalField<DateOnly>("date", field => field.WithFormat("yyyy-MM-dd"))
                .WithTemporalField<DateTime>("instant", field => field.WithFormat("yyyy-MM-ddTHH:mm:ss"))
                .WithTemporalField<TimeOnly>("time", field => field.WithFormat("HH:mm:ss"))
                .WithField<byte[]>("payload", field => field.WithParser(Convert.FromBase64String)))
            .Build();

    private static MemoryStream StreamOf(string value)
        => new(Encoding.UTF8.GetBytes(value));

    private sealed class BorrowingCsvReader(CsvProfile profile) : CsvReader(profile)
    {
        protected override CsvDataReader CreateDataReader(Stream stream) => new BorrowingDataReader(stream, Profile);
    }

    private sealed class BorrowingDataReader(Stream stream, CsvProfile profile) : CsvDataReader(stream, profile), IUtf8DataRecord
    {
        bool IUtf8DataRecord.CanGetUtf8Span(int ordinal) => true;

        bool IUtf8DataRecord.TryGetUtf8Span(int ordinal, out ReadOnlySpan<byte> value)
        {
            Assert.That(ordinal, Is.LessThan(2), "Only actual string columns should request UTF-8 text.");
            value = ordinal == 0 ? "borrowed é"u8 : default;
            return ordinal == 0;
        }

        protected override bool IsNull(int i) => i == 0 ? throw new AssertionException("Borrowed text reached IsDBNull.") : base.IsNull(i);
        public override object GetValue(int i) => i == 0 ? throw new AssertionException("Borrowed text reached GetValue.") : base.GetValue(i);
        public override string GetString(int i) => i == 0 ? throw new AssertionException("Borrowed text reached GetString.") : base.GetString(i);
    }

    private sealed class NonSeekableStream : Stream
    {
        private Stream Inner { get; }

        public NonSeekableStream(Stream inner) => Inner = inner;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => Inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => Inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => Inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing)
                Inner.Dispose();
            base.Dispose(disposing);
        }
        public override ValueTask DisposeAsync() => Inner.DisposeAsync();
    }
}
