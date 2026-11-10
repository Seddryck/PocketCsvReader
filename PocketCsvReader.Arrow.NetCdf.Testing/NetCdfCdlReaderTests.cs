using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Apache.Arrow;
using Apache.Arrow.Ipc;
using Apache.Arrow.Types;
using NUnit.Framework;

namespace PocketCsvReader.Arrow.NetCdf.Testing;

public sealed class NetCdfCdlReaderTests
{
    private const string Weather = """
        netcdf weather {
        dimensions:
            time = 1, latitude = 2, longitude = 2;
        variables:
            int time(time);
            float latitude(latitude), longitude(longitude), temperature(time, latitude, longitude);
            short quality(longitude, latitude);
            int station;
            char temperature:units = "degC";
            temperature:long_name = "Air temperature";
            :title = "Weather\nobservations";
        data:
            // Variable sections need not be in declaration order.
            temperature = 18.5f, 19.0f, 17.2f, 18.1f;
            quality = 100s, 200s, 101s, 201s;
            time = 12;
            latitude = 50, 51;
            longitude = 4, 5;
            station = 7;
        }
        """;

    [Test]
    public void FlattensAndBroadcastsAcrossBatchBoundaries()
    {
        using var input = Input(Weather);
        var reader = new NetCdfCdlReader(new() { BatchSize = 3 });
        using var stream = reader.ToArrowStream(input);
        Assert.That(stream.Schema.FieldsList.Select(field => field.Name),
            Is.EqualTo(new[] { "time", "latitude", "longitude", "temperature", "quality", "station" }));
        Assert.That(stream.Schema.Metadata["title"], Is.EqualTo("Weather\nobservations"));
        Assert.That(stream.Schema.Metadata["netcdf:dimensions"], Is.EqualTo("time,latitude,longitude"));
        Assert.That(stream.Schema.Metadata["netcdf:shape"], Is.EqualTo("1,2,2"));
        Assert.That(stream.Schema.FieldsList[3].Metadata["units"], Is.EqualTo("degC"));
        Assert.That(stream.Schema.FieldsList[3].Metadata["long_name"], Is.EqualTo("Air temperature"));
        Assert.That(stream.Schema.FieldsList[4].Metadata["netcdf:dimensions"], Is.EqualTo("longitude,latitude"));

        using var first = Read(stream)!;
        using var second = Read(stream)!;
        Assert.That(Read(stream), Is.Null);
        stream.Dispose();

        Assert.That(first.Length, Is.EqualTo(3));
        Assert.That(second.Length, Is.EqualTo(1));
        Assert.That(((Int32Array)first.Column(0)).Values.ToArray(), Is.EqualTo(new[] { 12, 12, 12 }));
        Assert.That(((FloatArray)first.Column(1)).Values.ToArray(), Is.EqualTo(new[] { 50f, 50f, 51f }));
        Assert.That(((FloatArray)first.Column(2)).Values.ToArray(), Is.EqualTo(new[] { 4f, 5f, 4f }));
        Assert.That(((FloatArray)first.Column(3)).Values.ToArray(), Is.EqualTo(new[] { 18.5f, 19f, 17.2f }));
        Assert.That(((Int16Array)first.Column(4)).Values.ToArray(), Is.EqualTo(new short[] { 100, 101, 200 }));
        Assert.That(((Int16Array)second.Column(4)).GetValue(0), Is.EqualTo(201));
        Assert.That(((Int32Array)second.Column(5)).GetValue(0), Is.EqualTo(7));
        Assert.That(input.CanRead, Is.True);
    }

    [Test]
    public void SelectsAndReordersDimensionsAndVariables()
    {
        using var input = Input(Weather);
        IArrowStreamSource reader = new NetCdfCdlReader(new()
        {
            RowDimensions = ["longitude", "latitude", "time"],
            DataVariables = ["station", "temperature"],
        });

        using var batch = reader.ToArrowBatches(input).Single();
        Assert.That(batch.Schema.FieldsList.Select(field => field.Name),
            Is.EqualTo(new[] { "longitude", "latitude", "time", "station", "temperature" }));
        Assert.That(((FloatArray)batch.Column(4)).Values.ToArray(), Is.EqualTo(new[] { 18.5f, 17.2f, 19f, 18.1f }));
    }

    [Test]
    public void RejectsConflictingShapesUntilExplicitlySelected()
    {
        const string cdl = """
            netcdf incompatible {
            dimensions: x = 2; y = 2;
            variables: int a(x), b(y);
            data: a = 1, 2; b = 3, 4;
            }
            """;
        using var input = Input(cdl);
        var ambiguous = Assert.Throws<InvalidDataException>(() => new NetCdfCdlReader().ToArrowStream(input))!;
        Assert.That(ambiguous.Message, Does.Contain("a(x)").And.Contain("b(y)").And.Contain("RowDimensions"));

        input.Position = 0;
        var fixedAxis = new NetCdfCdlReader(new() { RowDimensions = ["x"] });
        Assert.That(Assert.Throws<InvalidDataException>(() => fixedAxis.ToArrowStream(input))!.Message,
            Does.Contain("'b'").And.Contain("[y]").And.Contain("[x]"));

        input.Position = 0;
        var selected = new NetCdfCdlReader(new() { DataVariables = ["a"] });
        using var batch = selected.ToArrowBatches(input).Single();
        Assert.That(((Int64Array)batch.Column(0)).Values.ToArray(), Is.EqualTo(new long[] { 0, 1 }));
        Assert.That(((Int32Array)batch.Column(1)).Values.ToArray(), Is.EqualTo(new[] { 1, 2 }));
        Assert.That(batch.Schema.FieldsList[0].IsNullable, Is.False);
    }

    [Test]
    public void MatchingDimensionSetsInDifferentOrdersRequireExplicitOrder()
    {
        using var input = Input("netcdf d { dimensions: x=2,y=2; variables: int a(x,y), b(y,x); data: a=1,2,3,4; b=5,6,7,8; }");
        Assert.That(Assert.Throws<InvalidDataException>(() => new NetCdfCdlReader().ToArrowStream(input))!.Message,
            Does.Contain("a(x,y)").And.Contain("b(y,x)"));
        input.Position = 0;
        var reader = new NetCdfCdlReader(new() { RowDimensions = ["x", "y"] });
        using var batch = reader.ToArrowBatches(input).Single();
        Assert.That(((Int32Array)batch.Column(3)).Values.ToArray(), Is.EqualTo(new[] { 5, 7, 6, 8 }));
    }

    [Test]
    public void InfersOnlyFromCoordinatesWhenDataSelectionIsEmpty()
    {
        using var input = Input("netcdf d { dimensions: x=2,y=2; variables: int x(x),a(x,y); data: x=1,2; a=3,4,5,6; }");
        var reader = new NetCdfCdlReader(new() { DataVariables = [] });
        using var batch = reader.ToArrowBatches(input).Single();
        Assert.That(batch.Schema.FieldsList.Select(field => field.Name), Is.EqualTo(new[] { "x" }));
        Assert.That(((Int32Array)batch.Column(0)).Values.ToArray(), Is.EqualTo(new[] { 1, 2 }));
    }

    [Test]
    public void AppliesFillAndMissingAttributesAndPadsShortAssignments()
    {
        using var input = Input("""
            netcdf missing {
            dimensions: x = 6;
            variables:
                float x(x), reading(x);
                int status(x);
                reading:_FillValue = -999f;
                reading:missing_value = -1f, NaNf;
            data:
                reading = 1.5, -999, _, -1, NaN;
            }
            """);
        using var batch = new NetCdfCdlReader().ToArrowBatches(input).Single();
        Assert.That(batch.Length, Is.EqualTo(6));
        Assert.That(batch.Column(0).NullCount, Is.EqualTo(6));
        Assert.That(batch.Column(1).NullCount, Is.EqualTo(5));
        Assert.That(((FloatArray)batch.Column(1)).GetValue(0), Is.EqualTo(1.5f));
        Assert.That(batch.Column(2).NullCount, Is.EqualTo(6));
        Assert.That(batch.Schema.FieldsList[1].Metadata["missing_value"], Is.EqualTo("-1f, NaNf"));
    }

    [Test]
    public void InfersUnlimitedExtentAndPadsPartialRecord()
    {
        using var input = Input("""
            netcdf growing {
            dimensions: time = UNLIMITED; x = 2;
            variables: int time(time), a(time,x);
            data: a = 1, 2, 3; time = 10;
            }
            """);
        using var batch = new NetCdfCdlReader().ToArrowBatches(input).Single();
        var time = (Int32Array)batch.Column(0);
        var values = (Int32Array)batch.Column(2);
        Assert.That(batch.Schema.Metadata["netcdf:shape"], Is.EqualTo("2,2"));
        Assert.That(Enumerable.Range(0, 4).Select(time.GetValue), Is.EqualTo(new int?[] { 10, 10, null, null }));
        Assert.That(Enumerable.Range(0, 4).Select(values.GetValue), Is.EqualTo(new int?[] { 1, 2, 3, null }));
    }

    [TestCase("netcdf blank { }", 0)]
    [TestCase("/* empty */", 0)]
    [TestCase("netcdf header { dimensions: x=5; variables: int a(x); }", 2)]
    [TestCase("netcdf empty { dimensions: x=0; variables: int a(x), scalar; data: scalar=1; }", 3)]
    [TestCase("netcdf empty { dimensions: time=UNLIMITED; variables: int a(time); }", 2)]
    public void ExposesSchemaWithoutBatches(string cdl, int fields)
    {
        using var input = Input(cdl);
        using var stream = new NetCdfCdlReader().ToArrowStream(input);
        Assert.That(stream.Schema.FieldsList, Has.Count.EqualTo(fields));
        Assert.That(Read(stream), Is.Null);
    }

    [Test]
    public void MapsNumericPrimitiveTypesAndConstants()
    {
        using var input = Input("""
            netcdf numbers {
            variables:
                byte b; short s; int i; int64 l; ubyte ub; ushort us; uint ui; uint64 ul; float f; double d;
            data:
                b = -128b; s = 017s; i = 0x7ff; l = -9223372036854775808LL;
                ub = 255UB; us = 65535US; ui = 4294967295U; ul = 18446744073709551615ULL;
                f = Infinityf; d = NaN;
            }
            """);
        using var batch = new NetCdfCdlReader().ToArrowBatches(input).Single();
        Assert.That(batch.Length, Is.EqualTo(1));
        Assert.That(((Int8Array)batch.Column(0)).GetValue(0), Is.EqualTo(sbyte.MinValue));
        Assert.That(((Int16Array)batch.Column(1)).GetValue(0), Is.EqualTo(15));
        Assert.That(((Int32Array)batch.Column(2)).GetValue(0), Is.EqualTo(2047));
        Assert.That(((Int64Array)batch.Column(3)).GetValue(0), Is.EqualTo(long.MinValue));
        Assert.That(((UInt8Array)batch.Column(4)).GetValue(0), Is.EqualTo(byte.MaxValue));
        Assert.That(((UInt16Array)batch.Column(5)).GetValue(0), Is.EqualTo(ushort.MaxValue));
        Assert.That(((UInt32Array)batch.Column(6)).GetValue(0), Is.EqualTo(uint.MaxValue));
        Assert.That(((UInt64Array)batch.Column(7)).GetValue(0), Is.EqualTo(ulong.MaxValue));
        Assert.That(((FloatArray)batch.Column(8)).GetValue(0), Is.EqualTo(float.PositiveInfinity));
        Assert.That(((DoubleArray)batch.Column(9)).GetValue(0), Is.EqualTo(double.NaN));
    }

    [TestCase("dimensions: x=-1;", "extent")]
    [TestCase("dimensions: x=2,x=3;", "Duplicate dimension")]
    [TestCase("dimensions: x=9223372036854775807,y=2; variables: int a(x,y);", "exceeds Int64")]
    [TestCase("dimensions: x=2; variables: int a(y);", "Unknown dimension 'y'")]
    [TestCase("dimensions: x=2; variables: int a(x,x);", "repeats dimension 'x'")]
    [TestCase("dimensions: x=2,y=UNLIMITED; variables: int a(x,y);", "must be first")]
    [TestCase("dimensions: x=UNLIMITED,y=UNLIMITED;", "Only one unlimited")]
    [TestCase("variables: int a; data: a=1,2;", "Too many values for variable 'a'")]
    [TestCase("variables: int a; data: a=1; a=2;", "Duplicate data assignment")]
    [TestCase("dimensions: x=2; variables: int a(x); data: a=1,;", "Invalid")]
    [TestCase("variables: byte a; data: a=128;", "Invalid")]
    [TestCase("variables: int a; data: a=1.2;", "Invalid")]
    [TestCase("variables: float a; data: a=1e999;", "Invalid")]
    [TestCase("variables: int a; data: b=1;", "Unknown variable 'b'")]
    [TestCase("variables: int a; a:_FillValue=-1,-2;", "single _FillValue")]
    [TestCase("variables: int a; a:units=\"oops;", "Unterminated quoted")]
    [TestCase("variables: char a;", "unsupported")]
    [TestCase("types: compound t { int x; };", "Unsupported NetCDF-4 construct 'types'")]
    [TestCase("group: child { variables: int a; }", "Unsupported NetCDF-4 construct 'group'")]
    public void RejectsMalformedOrUnsupportedDocuments(string body, string message)
    {
        using var input = Input($"netcdf invalid {{ {body} }}");
        Assert.That(Assert.Throws<InvalidDataException>(() => new NetCdfCdlReader().ToArrowStream(input))!.Message,
            Does.Contain(message));
    }

    [Test]
    public async Task ReadsFileAndAsyncOnlyForwardInput()
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            await File.WriteAllTextAsync(path, Weather);
            IArrowStreamSource reader = new NetCdfCdlReader(new() { BatchSize = 2 });
            using var file = reader.ToArrowStream(path);
            Assert.That(file.Schema.FieldsList, Has.Count.EqualTo(6));
            using var first = Read(file)!;
            Assert.That(first.Length, Is.EqualTo(2));

            var fileRows = 0;
            await foreach (var batch in reader.ToArrowBatchesAsync(path))
            {
                using (batch)
                    fileRows += batch.Length;
            }

            Assert.That(fileRows, Is.EqualTo(4));
            using var stream = new ForwardInput(Encoding.UTF8.GetBytes(Weather), maxRead: 7);
            var streamRows = 0;
            await foreach (var batch in reader.ToArrowBatchesAsync(stream))
            {
                using (batch)
                    streamRows += batch.Length;
            }

            Assert.That(streamRows, Is.EqualTo(4));
            Assert.That(stream.Disposed, Is.False);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Test]
    public void ReadsFromCurrentPositionAndClosesOwnedInputBeforeReturning()
    {
        var reader = new NetCdfCdlReader(new() { LeaveOpen = false, BatchSize = 1 });
        using var input = Input("skip" + Weather);
        input.Position = 4;
        using var batches = reader.ToArrowBatches(input).GetEnumerator();
        Assert.That(batches.MoveNext(), Is.True);
        using var first = batches.Current;
        Assert.That(input.CanRead, Is.False);
        batches.Dispose();
        Assert.That(((FloatArray)first.Column(3)).GetValue(0), Is.EqualTo(18.5f));
    }

    [Test]
    public void ReadsAcrossChunksWithBorrowedTokenStorage()
    {
        var longValue = new string('x', 9500);
        var cdl = $"netcdf split {{ variables: int a; a:note=\"{longValue}\\t雪\"; /*{longValue}*/ data: a=7; }}";
        using var input = Input(cdl);
        using var stream = new NetCdfCdlReader().ToArrowStream(input);
        using var batch = Read(stream)!;
        Assert.That(batch.Schema.FieldsList[0].Metadata["note"], Is.EqualTo(longValue + "\t雪"));
        Assert.That(((Int32Array)batch.Column(0)).GetValue(0), Is.EqualTo(7));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task LexerReusesChunkAndOverflowStorage(bool asynchronous)
    {
        using var stream = Input("ab cd   123456789012 xyzabcdefghk ");
        using var reader = new StreamReader(stream);
        using var buffer = new SingleBuffer(reader, 8);
        var lexer = new CdlLexer(buffer, asynchronous, default);

        var first = await lexer.ReadAsync();
        var second = await lexer.ReadAsync();
        var longFirst = await lexer.ReadAsync();
        Assert.That(longFirst.Text.ToString(), Is.EqualTo("123456789012"));
        var longSecond = await lexer.ReadAsync();
        Assert.That(longSecond.Text.ToString(), Is.EqualTo("xyzabcdefghk"));

        Assert.That(MemoryMarshal.TryGetArray(first.Text, out var firstChunk), Is.True);
        Assert.That(MemoryMarshal.TryGetArray(second.Text, out var secondChunk), Is.True);
        Assert.That(MemoryMarshal.TryGetArray(longFirst.Text, out var firstOverflow), Is.True);
        Assert.That(MemoryMarshal.TryGetArray(longSecond.Text, out var secondOverflow), Is.True);
        Assert.That(firstChunk.Array, Is.SameAs(secondChunk.Array));
        Assert.That(firstOverflow.Array, Is.SameAs(secondOverflow.Array).And.Not.SameAs(firstChunk.Array));
    }

    [Test]
    public void ReadsUtf8WithBomFromNonSeekableInput()
    {
        var bytes = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(Weather)).ToArray();
        using var input = new ForwardInput(bytes, maxRead: 1, allowSync: true);
        using var batch = new NetCdfCdlReader().ToArrowBatches(input).Single();
        Assert.That(batch.Length, Is.EqualTo(4));
    }

    [Test]
    public void RejectsInvalidUtf8AndClosesOwnedInput()
    {
        using var input = new MemoryStream([0x43, 0x44, 0xff]);
        var reader = new NetCdfCdlReader(new() { LeaveOpen = false });
        Assert.That(Assert.Throws<InvalidDataException>(() => reader.ToArrowStream(input))!.Message, Does.Contain("UTF-8"));
        Assert.That(input.CanRead, Is.False);
    }

    [Test]
    public async Task CancelsDuringAsyncInputAndDisposesOwnedInput()
    {
        using var cancellation = new CancellationTokenSource();
        using var input = new ForwardInput(Encoding.UTF8.GetBytes(Weather), cancel: cancellation);
        var reader = new NetCdfCdlReader(new() { LeaveOpen = false });
        await Assert.CatchAsync<OperationCanceledException>(async () => await reader.ToArrowStreamAsync(input, cancellation.Token));
        Assert.That(input.Disposed, Is.True);

        using var validInput = Input(Weather);
        using var stream = await reader.ToArrowStreamAsync(validInput);
        await Assert.CatchAsync<OperationCanceledException>(async () => await stream.ReadNextRecordBatchAsync(cancellation.Token));
        using var batch = await stream.ReadNextRecordBatchAsync();
        Assert.That(batch!.Length, Is.EqualTo(4));
        stream.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(async () => await stream.ReadNextRecordBatchAsync());
    }

    [Test]
    public async Task DenseArraysRoundTripThroughArrowIpc()
    {
        using var input = Input(Weather);
        using var stream = new NetCdfCdlReader().ToArrowStream(input);
        using var batch = Read(stream)!;
        using var ipc = new MemoryStream();
        using (var writer = new ArrowStreamWriter(ipc, stream.Schema, leaveOpen: true))
        {
            await writer.WriteRecordBatchAsync(batch);
            await writer.WriteEndAsync();
        }

        ipc.Position = 0;
        using var reader = new ArrowStreamReader(ipc);
        using var result = await reader.ReadNextRecordBatchAsync();
        Assert.That(result!.Length, Is.EqualTo(4));
        Assert.That(((FloatArray)result.Column(3)).GetValue(0), Is.EqualTo(18.5f));
        Assert.That(result.Schema.FieldsList[3].Metadata["units"], Is.EqualTo("degC"));
    }

    [Test]
    public void EncodesCoordinateRunsWithinEachBatch()
    {
        const string cdl = """
            netcdf grid {
            dimensions: x = 3, y = 2;
            variables: int x(x), value(x,y);
            data: x = 12, _, 12; value = 1, 2, 3, 4, 5, 6;
            }
            """;
        using var input = Input(cdl);
        var reader = new NetCdfCdlReader(new() { BatchSize = 3, RunEndEncodeCoordinates = true });
        using var stream = reader.ToArrowStream(input);
        Assert.That(stream.Schema.FieldsList[0].DataType, Is.TypeOf<RunEndEncodedType>());
        using var first = Read(stream)!;
        using var second = Read(stream)!;
        var x = (RunEndEncodedArray)first.Column(0);
        var nextX = (RunEndEncodedArray)second.Column(0);
        var y = (RunEndEncodedArray)first.Column(1);
        Assert.That(x.Length, Is.EqualTo(first.Length));
        Assert.That(((Int32Array)x.RunEnds).Values.ToArray(), Is.EqualTo(new[] { 2, 3 }));
        Assert.That(((Int32Array)x.Values).GetValue(0), Is.EqualTo(12));
        Assert.That(x.Values.IsNull(1), Is.True);
        Assert.That(((Int32Array)nextX.RunEnds).Values.ToArray(), Is.EqualTo(new[] { 1, 3 }));
        Assert.That(nextX.Values.IsNull(0), Is.True);
        Assert.That(((Int64Array)y.Values).Values.ToArray(), Is.EqualTo(new long[] { 0, 1, 0 }));
        Assert.That(first.Column(2), Is.TypeOf<Int32Array>());
    }

    [Test]
    public async Task ReadsRunEndEncodedIpcWithPinnedArrowVersion()
    {
        using var input = Input(Weather);
        var reader = new NetCdfCdlReader(new() { RunEndEncodeCoordinates = true });
        using var stream = reader.ToArrowStream(input);
        using var batch = Read(stream)!;
        using var ipc = new MemoryStream();
        using (var writer = new ArrowStreamWriter(ipc, stream.Schema, leaveOpen: true))
        {
            await writer.WriteRecordBatchAsync(batch);
            await writer.WriteEndAsync();
        }

        ipc.Position = 0;
        using var downstream = new ArrowStreamReader(ipc);
        using var result = await downstream.ReadNextRecordBatchAsync();
        Assert.That(result!.Length, Is.EqualTo(4));
        var time = (RunEndEncodedArray)result.Column(0);
        Assert.That(((Int32Array)time.RunEnds).Values.ToArray(), Is.EqualTo(new[] { 4 }));
        Assert.That(((Int32Array)time.Values).GetValue(0), Is.EqualTo(12));
    }

    [TestCase(false)]
    [TestCase(true)]
    [NonParallelizable]
    public async Task NumericStagingDoesNotAllocatePerCell(bool asynchronous)
    {
        const int cells = 64 * 1024;
        var document = Encoding.UTF8.GetBytes("netcdf allocation { dimensions: x="
            + cells.ToString(CultureInfo.InvariantCulture) + "; variables: int a(x); data: a="
            + string.Concat(Enumerable.Repeat("12345,", cells - 1)) + "12345; }");
        var reader = new NetCdfCdlReader(new() { BatchSize = 1025 });
        for (var warmup = 0; warmup < 2; warmup++)
        {
            using var input = new MemoryStream(document);
            using var stream = asynchronous ? await reader.ToArrowStreamAsync(input) : reader.ToArrowStream(input);
        }

        using var measuredInput = new MemoryStream(document);
        var start = GC.GetTotalAllocatedBytes(precise: true);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        using var measured = asynchronous ? await reader.ToArrowStreamAsync(measuredInput) : reader.ToArrowStream(measuredInput);
        var allocated = GC.GetTotalAllocatedBytes(precise: true) - start;
        TestContext.Out.WriteLine($"Staged {cells} cells in {timer.Elapsed.TotalMilliseconds:F1} ms using {allocated:N0} allocated bytes.");
        // Fixed transport buffers and staging I/O get 256 KiB. One string per cell would exceed 2 MiB.
        Assert.That(allocated, Is.LessThan(256 * 1024L));
        using var first = await measured.ReadNextRecordBatchAsync();
        Assert.That(((Int32Array)first!.Column(1)).GetValue(1024), Is.EqualTo(12345));
    }

    private static MemoryStream Input(string text) => new(Encoding.UTF8.GetBytes(text));

    private static RecordBatch? Read(IArrowArrayStream stream) => stream.ReadNextRecordBatchAsync().GetAwaiter().GetResult();

    private sealed class ForwardInput(
        byte[] bytes, int maxRead = 128, CancellationTokenSource? cancel = null, bool allowSync = false) : Stream
    {
        private int _offset;

        public bool Disposed { get; private set; }
        public override bool CanRead => !Disposed;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancel?.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(ReadChunk(buffer.Span));
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
            => allowSync ? ReadChunk(buffer) : throw new NotSupportedException("This input requires async I/O.");

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }

        private int ReadChunk(Span<byte> buffer)
        {
            var count = Math.Min(maxRead, Math.Min(buffer.Length, bytes.Length - _offset));
            bytes.AsSpan(_offset, count).CopyTo(buffer);
            _offset += count;
            return count;
        }
    }
}
