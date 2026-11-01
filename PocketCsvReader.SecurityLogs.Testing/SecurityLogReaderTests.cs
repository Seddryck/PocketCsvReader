using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using NUnit.Framework;
using PocketCsvReader.SecurityLogs.Configuration;

namespace PocketCsvReader.SecurityLogs.Testing;

public class SecurityLogReaderTests
{
    private const string Cef = "CEF:0|Trend Micro|Deep Security Manager||600|User Signed In|3|src=10.52.116.160 suser=admin msg=User signed in from 2001:db8::5";
    private const string Leef = "LEEF:1.0|Microsoft|MSExchange|4.0 SP1|15345|src=192.0.2.1\tsev=5\tmsg=User signed in";
    private static MemoryStream Text(string value) => new(Encoding.UTF8.GetBytes(value));
    private static SecurityLogReader Reader(SecurityLogFormat format, int buffer = 4096)
        => format == SecurityLogFormat.Cef
            ? new CefReaderBuilder().WithParserOptimizations(new(BufferSize: buffer)).Build()
            : new LeefReaderBuilder().WithParserOptimizations(new(BufferSize: buffer)).Build();
    private static string Sample(SecurityLogFormat format) => format == SecurityLogFormat.Cef ? Cef : Leef;

    [Test]
    public void Cef_SpecificationExample_ExposesHeaderAndAttributes()
    {
        using var reader = new CefReader().ToDataReader(Text(Cef));
        Assert.That(reader.Read(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.FieldCount, Is.EqualTo(10));
            Assert.That(reader.GetString(reader.GetOrdinal("DeviceVendor")), Is.EqualTo("Trend Micro"));
            Assert.That(reader.GetString(reader.GetOrdinal("DeviceVersion")), Is.Empty);
            Assert.That(reader.GetInt32(reader.GetOrdinal("Severity")), Is.EqualTo(3));
            Assert.That(reader.GetString(reader.GetOrdinal("msg")), Is.EqualTo("User signed in from 2001:db8::5"));
        });
        Assert.That(reader.Read(), Is.False);
        Assert.Throws<InvalidOperationException>(() => reader.GetString(0));
    }

    [Test]
    public void Cef_EscapesAndUnicode_DecodeWithoutChangingRawValues()
    {
        const string line = @"CEF:1|Ven\|dor|Pro\\duct|1|id|é event|High|msg=a\=b\nsecond\rline \\ path | literal empty= next=z";
        using var reader = new CefReaderBuilder().WithParserOptimizations(new(BufferSize: 3)).Build().ToDataReader(Text(line));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(1), Is.EqualTo("Ven|dor"));
        Assert.That(reader.GetString(2), Is.EqualTo(@"Pro\duct"));
        Assert.That(reader.GetString(5), Is.EqualTo("é event"));
        int msg = reader.GetOrdinal("msg");
        Assert.That(reader.GetRawString(msg), Is.EqualTo(@"a\=b\nsecond\rline \\ path | literal"));
        Assert.That(reader.GetString(msg), Is.EqualTo("a=b\nsecond\rline \\ path | literal"));
        Assert.That(reader.GetString(reader.GetOrdinal("empty")), Is.Empty);
    }

    [TestCase("^", "^")]
    [TestCase("x5E", "^")]
    [TestCase("0x005e", "^")]
    [TestCase("x09", "\t")]
    [TestCase("x7c", "|")]
    [TestCase("x00a6", "¦")]
    [TestCase(" ", " ")]
    public void Leef2_CustomDelimiters_PreserveValueSpacesAndEquals(string declaration, string delimiter)
    {
        var line = $"LEEF:2.0|Lancope|StealthWatch|1.0|41|{declaration}|src=192.0.2.1{delimiter}msg=a=b{delimiter}sev=5";
        using var reader = new LeefReaderBuilder().WithParserOptimizations(new(BufferSize: 2)).Build().ToDataReader(Text(line));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(reader.GetOrdinal("msg")), Is.EqualTo("a=b"));
        Assert.That(reader.GetString(reader.GetOrdinal("EventId")), Is.EqualTo("41"));
        Assert.That(reader.GetInt32(reader.GetOrdinal("sev")), Is.EqualTo(5));
    }

    [Test]
    public void Leef1_Tabs_KeepSpacesBackslashesAndEqualsLiteral()
    {
        using var reader = new LeefReader().ToDataReader(Text(Leef + "\tpath=C:\\new\\test=x\tempty="));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(reader.GetOrdinal("msg")), Is.EqualTo("User signed in"));
        Assert.That(reader.GetString(reader.GetOrdinal("path")), Is.EqualTo(@"C:\new\test=x"));
        Assert.That(reader.GetString(reader.GetOrdinal("empty")), Is.Empty);
    }

    [TestCase(SecurityLogFormat.Cef)]
    [TestCase(SecurityLogFormat.Leef)]
    public void SyslogEnvelopes_StripOnlyTheEnvelope(SecurityLogFormat format)
    {
        string[] prefixes = ["", "Jan 18 11:07:53 host ", "Feb 29 11:07:53 host ", "<13>Jan  8 11:07:53 host app[12]: ",
            "<133>1 2019-01-18T11:07:53.520+07:00 host app 12 ID - ",
            "<13>1 - host app - - [meta text=\"CEF:fake \\\" ] LEEF:fake\"][next x=\"ok\"] \uFEFF"];
        foreach (var prefix in prefixes)
        {
            using var reader = Reader(format, 7).ToDataReader(Text(prefix + Sample(format)));
            Assert.That(reader.Read(), Is.True, prefix);
            Assert.That(reader.GetString(reader.GetOrdinal("src")), Is.EqualTo(format == SecurityLogFormat.Cef ? "10.52.116.160" : "192.0.2.1"));
        }
    }

    [TestCase(SecurityLogFormat.Cef)]
    [TestCase(SecurityLogFormat.Leef)]
    public void StableColumns_MissingAttributesAreNullAndNewKeysAreIgnored(SecurityLogFormat format)
    {
        var prefix = format == SecurityLogFormat.Cef ? "CEF:0|V|P|1|id|event|3|" : "LEEF:1.0|V|P|1|id|";
        var separator = format == SecurityLogFormat.Cef ? " " : "\t";
        using var reader = Reader(format).ToDataReader(Text(prefix + "src=first" + separator + "msg=hello\n" + prefix + "other=ignored" + separator + "src=second"));
        Assert.That(reader.Read(), Is.True);
        int width = reader.FieldCount, src = reader.GetOrdinal("src"), msg = reader.GetOrdinal("msg");
        string retained = reader.GetString(src);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.FieldCount, Is.EqualTo(width));
        Assert.That(reader.GetString(src), Is.EqualTo("second"));
        Assert.That(reader.IsDBNull(msg), Is.True);
        Assert.That(reader.GetValue(msg), Is.EqualTo(DBNull.Value));
        Assert.That(reader.TryGetOrdinal("other", out _), Is.False);
        Assert.That(retained, Is.EqualTo("first"));
    }

    [TestCase(SecurityLogFormat.Cef)]
    [TestCase(SecurityLogFormat.Leef)]
    public void AttributeSelection_SchemaAndAllOutputApis(SecurityLogFormat format)
    {
        SecurityLogReader reader = format == SecurityLogFormat.Cef
            ? new CefReaderBuilder().WithAttributes("msg", "src", "missing")
                .WithSchema(s => s.Named().WithField<int>("Severity")).Build()
            : new LeefReaderBuilder().WithAttributes("msg", "src", "missing")
                .WithSchema(s => s.Named().WithField<int>("EventId")).Build();
        string line = Sample(format);
        int headerCount = format == SecurityLogFormat.Cef ? 7 : 5;
        var array = reader.ToArrayString(Text(line)).Single();
        var table = reader.ToDataTable(Text(line));
        var mapped = reader.To<string>(Text(line), r => r.GetString(r.GetOrdinal("src"))).Single();
        using var dataReader = reader.ToDataReader(Text(line));
        Assert.That(dataReader.FieldCount, Is.EqualTo(headerCount + 3));
        Assert.That(dataReader.Read(), Is.True);
        Assert.That(dataReader.GetValue(format == SecurityLogFormat.Cef ? 6 : 4), Is.TypeOf<int>());
        Assert.That(array[headerCount + 2], Is.Null);
        Assert.That(table.Rows[0]["missing"], Is.EqualTo(DBNull.Value));
        Assert.That(mapped, Is.EqualTo(array[headerCount + 1]));
        using var adapter = reader.ToDbDataReader(Text(line));
        Assert.That(adapter.Read(), Is.True);
        Assert.That(reader.ToDynamic(Text(line)).Count(), Is.EqualTo(1));
        Assert.That(reader.ToDictionaries(Text(line)).Single()["src"], Is.EqualTo(mapped));
        Assert.That(reader.ToObjectArrays(Text(line)).Single().Length, Is.EqualTo(headerCount + 3));
    }

    public sealed record CefHeader(string Version, string Vendor, string Product, string ProductVersion, string Id, string Name, int Severity);
    public sealed record LeefHeader(string Version, string Vendor, string Product, string ProductVersion, int Id);

    [Test]
    public void ConstructorMapping_UsesTypedFields()
    {
        var cef = new CefReaderBuilder().WithAttributes().Build().To<CefHeader>(Text(Cef)).Single();
        var leef = new LeefReaderBuilder().WithAttributes().Build().To<LeefHeader>(Text(Leef)).Single();
        Assert.That(cef.Severity, Is.EqualTo(3));
        Assert.That(leef.Id, Is.EqualTo(15345));
    }

    [TestCase(SecurityLogFormat.Cef, "CEF:2|V|P|1|id|event|3|")]
    [TestCase(SecurityLogFormat.Cef, "CEF:0|V|P|1|id|event|3")]
    [TestCase(SecurityLogFormat.Cef, "CEF:0|V\\q|P|1|id|event|3|")]
    [TestCase(SecurityLogFormat.Cef, "CEF:0|V|P|1|id|event|3|msg=a=b")]
    [TestCase(SecurityLogFormat.Cef, "CEF:0|V|P|1|id|event|3|msg=\\q")]
    [TestCase(SecurityLogFormat.Cef, "CEF:0|V|P|1|id|event|3|msg=\\")]
    [TestCase(SecurityLogFormat.Cef, "CEF:0|V|P|1|id|event|3|msg=x msg=y")]
    [TestCase(SecurityLogFormat.Cef, "CEF:0|V|P|1|id|event|3|Name=x")]
    [TestCase(SecurityLogFormat.Cef, "garbage CEF:0|V|P|1|id|event|3|")]
    [TestCase(SecurityLogFormat.Cef, "<999>Jan 18 11:07:53 host CEF:0|V|P|1|id|event|3|")]
    [TestCase(SecurityLogFormat.Cef, "<13>1 - host app - - [meta x=\"CEF:fake\" CEF:0|V|P|1|id|event|3|")]
    [TestCase(SecurityLogFormat.Cef, "LEEF:1.0|V|P|1|id|")]
    [TestCase(SecurityLogFormat.Leef, "LEEF:3.0|V|P|1|id|")]
    [TestCase(SecurityLogFormat.Leef, "LEEF:2.0|V|P|1|id|bad|src=x")]
    [TestCase(SecurityLogFormat.Leef, "LEEF:2.0|V|P|1|id|xGG|src=x")]
    [TestCase(SecurityLogFormat.Leef, "LEEF:2.0|V|P|1|id|x00000|src=x")]
    [TestCase(SecurityLogFormat.Leef, "LEEF:2.0|V|P|1|id|x0a|src=x")]
    [TestCase(SecurityLogFormat.Leef, "LEEF:2.0|V|P|1|id|xD800|src=x")]
    [TestCase(SecurityLogFormat.Leef, "LEEF:2.0|V|P|1|id|^src=x")]
    [TestCase(SecurityLogFormat.Leef, "LEEF:1.0|V|P|1|id|src=x\tsrc=y")]
    [TestCase(SecurityLogFormat.Leef, "LEEF:1.0|V|P|1|id|=x")]
    [TestCase(SecurityLogFormat.Leef, "LEEF:1.0|V|P|1|id|bad")]
    [TestCase(SecurityLogFormat.Leef, "")]
    public async Task MalformedRecords_ReportFormatAndAbsoluteLocation(SecurityLogFormat format, string invalid)
    {
        string data = Sample(format) + "\n" + invalid + "\n";
        using var sync = Reader(format, 3).ToDataReader(Text(data));
        Assert.That(sync.Read(), Is.True);
        var error = Assert.Throws<InvalidDataException>(() => sync.Read());
        Assert.That(error!.Message, Does.Contain("line 2").And.Contain("offset " + (Sample(format).Length + 1)));
        using var asyncReader = Reader(format, 3).ToDataReader(new AsyncOnlyStream(Encoding.UTF8.GetBytes(data)));
        Assert.That(await asyncReader.ReadAsync(), Is.True);
        var asyncError = await Assert.ThrowsAsync<InvalidDataException>(async () => await asyncReader.ReadAsync());
        Assert.That(asyncError!.Message, Is.EqualTo(error.Message));
    }

    [TestCase(SecurityLogFormat.Cef)]
    [TestCase(SecurityLogFormat.Leef)]
    public async Task BufferBoundaries_LongRecords_AndAsyncOnlyInput(SecurityLogFormat format)
    {
        string line = Sample(format) + new string('é', 10000);
        foreach (int size in new[] { 1, 2, 7, 4096 })
        {
            foreach (string terminator in new[] { "\r\n", "\n", "\r" })
        {
            string data = line + terminator + Sample(format);
            using var sync = Reader(format, size).ToDataReader(Text(data));
            using var asyncReader = Reader(format, size).ToDataReader(new AsyncOnlyStream(Encoding.UTF8.GetBytes(data)));
            for (int i = 0; i < 2; i++)
            {
                Assert.That(sync.Read(), Is.True);
                Assert.That(await asyncReader.ReadAsync(), Is.True);
                int msg = sync.GetOrdinal("msg");
                Assert.That(asyncReader.GetString(msg), Is.EqualTo(sync.GetString(msg)));
            }
            Assert.That(sync.Read(), Is.False);
            Assert.That(await asyncReader.ReadAsync(), Is.False);
        }
        }
    }

    [TestCase(SecurityLogFormat.Cef)]
    [TestCase(SecurityLogFormat.Leef)]
    public async Task EmptyInput_Cancellation_AndStreaming(SecurityLogFormat format)
    {
        using var empty = Reader(format).ToDataReader(Text(""));
        Assert.That(empty.Read(), Is.False);
        using var stream = new AsyncOnlyStream(Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(Sample(format) + "\n", 10000))));
        using var reader = Reader(format, 128).ToDataReader(stream);
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await reader.ReadAsync(new CancellationToken(true)));
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(stream.BytesRead, Is.LessThan(10000));
        string current = reader.GetString(0);
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await reader.ReadAsync(new CancellationToken(true)));
        Assert.That(reader.GetString(0), Is.EqualTo(current));
    }

    [TestCase(SecurityLogFormat.Cef)]
    [TestCase(SecurityLogFormat.Leef)]
    public void RawViews_ReuseMetadataAndBuffer_OwnedStringsSurviveAdvance(SecurityLogFormat format)
    {
        using var text = new StreamReader(Text(Sample(format) + "\n" + Sample(format) + "\n"));
        using var source = new SecurityLogRecordSource(text, new(format));
        source.IsEndOfFile(out var first, out _);
        var memory = first.AsMemory();
        var fields = first.FieldSpans;
        var retained = first.Slice(1).ToString();
        Assert.That(MemoryMarshal.TryGetArray(memory.Span, out var firstArray), Is.True);
        source.IsEndOfFile(out var second, out _);
        Assert.That(MemoryMarshal.TryGetArray(second.AsMemory().Span, out var secondArray), Is.True);
        Assert.That(secondArray.Array, Is.SameAs(firstArray.Array));
        Assert.That(second.FieldSpans, Is.SameAs(fields));
        Assert.That(retained, Is.EqualTo(format == SecurityLogFormat.Cef ? "Trend Micro" : "Microsoft"));
    }

    [TestCase(SecurityLogFormat.Cef)]
    [TestCase(SecurityLogFormat.Leef)]
    public void RawParsing_AllocationBudgetAndRepresentativeTimings(SecurityLogFormat format)
    {
        const int rows = 1000;
        var data = Encoding.UTF8.GetBytes(string.Concat(Enumerable.Repeat(Sample(format) + new string('x', 128) + "\n", rows)));
        ReadAll(data, 4096);
        long before = GC.GetAllocatedBytesForCurrentThread();
        Assert.That(ReadAll(data, 4096), Is.EqualTo(rows));
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        // Includes reader setup and cross-buffer bookkeeping; a per-row full line or metadata array exceeds 80 bytes/row.
        Assert.That(allocated, Is.LessThan(80L * rows), $"{allocated} raw parsing bytes");
        foreach (var (size, bytes) in new[] { (4096, data), (32, data), (4096, Encoding.UTF8.GetBytes(Sample(format) + new string('x', 100000) + "\n")) })
        {
            ReadAll(bytes, size);
            var watch = Stopwatch.StartNew();
            int count = ReadAll(bytes, size);
            watch.Stop();
            TestContext.Out.WriteLine($"{format}: {count} records, {bytes.Length} bytes, buffer {size}, {watch.Elapsed.TotalMilliseconds:F3} ms; warmed raw allocation={allocated}/{rows} rows");
        }

        int ReadAll(byte[] bytes, int bufferSize)
        {
            using var text = new StreamReader(new MemoryStream(bytes));
            using var source = new SecurityLogRecordSource(text, new(format, parserOptimizations: new(BufferSize: bufferSize)));
            int count = 0;
            while (!source.IsEndOfFile(out _, out _)) count++;
            return count;
        }
    }

    [Test]
    public void Configuration_RejectsInvalidOrCollidingNamesAndWrongFormats()
    {
        Assert.Throws<ArgumentException>(() => new CefReaderBuilder().WithAttributes("src", "src").Build());
        Assert.Throws<ArgumentException>(() => new CefReaderBuilder().WithAttributes("Name").Build());
        Assert.Throws<ArgumentException>(() => new LeefReaderBuilder().WithAttributes(" ").Build());
        Assert.Throws<ArgumentOutOfRangeException>(() => new CefReaderBuilder().WithParserOptimizations(new(BufferSize: 0)).Build());
        Assert.Throws<ArgumentException>(() => new CefReader(new(SecurityLogFormat.Leef)));
        Assert.Throws<ArgumentException>(() => new LeefReader(new(SecurityLogFormat.Cef)));
    }

    [Test]
    public void PositionTracking_ExposesRecordStart()
    {
        using var reader = new CefReaderBuilder().WithPositionTracking().Build().ToDataReader(Text(Cef + "\r\n" + Cef));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.CurrentPosition!.Value.PhysicalLine, Is.EqualTo(1));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.CurrentPosition!.Value.Offset, Is.EqualTo(Cef.Length + 2));
    }

    [Test]
    public void ResourceSequences_FieldSequences_RuntimeParsers_AndIndexedSchema()
    {
        var configured = new CefReaderBuilder().WithAttributes("src", "msg")
            .WithResource(r => r.WithEncoding("utf-8").WithSequence("none", null))
            .WithSchema(s => s.Named().WithField<int>("Severity", f => f.WithSequence("High", "8")))
            .WithParsers(p => p.WithParser<int>((ReadOnlySpan<char> value) => int.Parse(value) + 1))
            .Build();
        using var reader = configured.ToDataReader(Text("CEF:0|V|P|1|id|event|High|src=none msg=hello"));
        Assert.That(reader.Read(), Is.True);
        // A schema format takes precedence over a type-wide runtime parser.
        Assert.That(reader.GetValue(6), Is.EqualTo(8));
        Assert.That(reader.IsDBNull(reader.GetOrdinal("src")), Is.True);
        Assert.That(reader.GetString(reader.GetOrdinal("msg")), Is.EqualTo("hello"));

        using var runtime = new CefReaderBuilder()
            .WithParsers(p => p.WithParser<int>((ReadOnlySpan<char> value) => int.Parse(value) + 1))
            .Build().ToDataReader(Text(Cef));
        Assert.That(runtime.Read(), Is.True);
        Assert.That(runtime.GetInt32(6), Is.EqualTo(4));

        var indexed = new LeefReaderBuilder().WithAttributes()
            .WithSchema(s => s.Indexed().WithField<string>().WithField<string>().WithField<string>().WithField<string>().WithField<int>())
            .Build().To<LeefHeader>(Text(Leef)).Single();
        Assert.That(indexed.Id, Is.EqualTo(15345));
    }

    [Test]
    public void DuplicateUnselectedKeys_AreRejected_AndSelectionIsCopied()
    {
        string[] attributes = ["src"];
        var builder = new CefReaderBuilder().WithAttributes(attributes);
        attributes[0] = "changed";
        var configured = builder.Build();
        using var reader = configured.ToDataReader(Text(Cef));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetName(7), Is.EqualTo("src"));
        using var duplicate = configured.ToDataReader(Text(Cef + " hidden=1 hidden=2"));
        Assert.Throws<InvalidDataException>(() => duplicate.Read());
    }

    private sealed class AsyncOnlyStream(byte[] data) : MemoryStream
    {
        public int BytesRead { get; private set; }
        public override bool CanSeek => false;
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Sync I/O forbidden");
        public override int Read(Span<byte> buffer) => throw new NotSupportedException("Sync I/O forbidden");
        public override int ReadByte() => throw new NotSupportedException("Sync I/O forbidden");
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = Math.Min(buffer.Length, data.Length - BytesRead);
            data.AsMemory(BytesRead, count).CopyTo(buffer);
            BytesRead += count;
            return ValueTask.FromResult(count);
        }
    }
}

