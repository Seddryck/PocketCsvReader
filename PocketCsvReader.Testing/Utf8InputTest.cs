using System.IO.Compression;
using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing;

public class Utf8InputTest
{
    private static CsvProfile Profile(CsvInputMode mode, DialectDescriptor? dialect = null, int bufferSize = 3,
        ResourceDescriptor? resource = null)
        => new(dialect ?? new(Header: false, LineTerminator: "\r\n"), resource: resource)
        {
            StreamInitialization = new(InputMode: mode),
            ParserOptimizations = new(BufferSize: bufferSize, ReadAhead: false)
        };

    private static string?[][] Read(byte[] bytes, CsvProfile profile)
        => new CsvReader(profile).ToArrayString(new MemoryStream(bytes)).ToArray();

    private static IEnumerable<TestCaseData> ParityCases()
    {
        var dialect = new DialectDescriptor(Header: false, LineTerminator: "\r\n");
        yield return new TestCaseData("é,東京,😀\r\nlast,,", dialect);
        yield return new TestCaseData("\"a,b\",\"a\"\"b\",\"東京\r\n😀\"\r\nnext,x,y", dialect);
        yield return new TestCaseData("#comment\r\n1,NULL,\"NULL\"\r\n#trailing", dialect with { CommentChar = '#', NullSequence = "NULL" });
        yield return new TestCaseData("1,  \"a\\\"b\",c\\,d\r\n2,東京,end", dialect with { EscapeChar = '\\', SkipInitialSpace = true });
        yield return new TestCaseData("nom,名前\r\né,東京\r\n", dialect with { Header = true });
        yield return new TestCaseData("a,b,c\r\n1,2\r\n", dialect with { Header = true, MissingCell = "missing" });
        yield return new TestCaseData("a;b\n東京;é\n", dialect with { Delimiter = ';', QuoteChar = null, LineTerminator = "\n" });
        yield return new TestCaseData(",\r\n\"\",\r\n,\r\n", dialect);
        yield return new TestCaseData("", dialect);
        yield return new TestCaseData("\"" + new string('é', 20000) + "\r\n😀\",end", dialect);
    }

    [TestCaseSource(nameof(ParityCases))]
    public void ByteAndAutomaticModes_MatchDecodedFields(string csv, DialectDescriptor dialect)
    {
        var bytes = Encoding.UTF8.GetBytes(csv);
        var expected = Read(bytes, Profile(CsvInputMode.DecodedChars, dialect));
        foreach (int size in new[] { 1, 2, 3, 7, 4096 })
        foreach (var mode in new[] { CsvInputMode.Utf8Bytes, CsvInputMode.Auto })
            Assert.That(Read(bytes, Profile(mode, dialect, size)), Is.EqualTo(expected), $"{mode}, buffer {size}");
    }

    [TestCase(CsvInputMode.Auto, false)]
    [TestCase(CsvInputMode.Auto, true)]
    [TestCase(CsvInputMode.Utf8Bytes, false)]
    [TestCase(CsvInputMode.Utf8Bytes, true)]
    public void BomAndShortForwardOnlyReads(CsvInputMode mode, bool bom)
    {
        byte[] bytes = [.. bom ? Encoding.UTF8.Preamble.ToArray() : [], .. Encoding.UTF8.GetBytes("é,東京\r\n")];
        using var stream = new ShortStream(bytes);
        using var reader = new InspectingReader(stream, Profile(mode));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.SourceName, Is.EqualTo("Utf8RecordSource"));
        Assert.That(reader.GetString(0), Is.EqualTo("é"));
        Assert.That(reader.GetString(1), Is.EqualTo("東京"));
        Assert.That(reader.Read(), Is.False);
    }

    [TestCase("utf-16")]
    [TestCase("utf-16BE")]
    [TestCase("utf-32")]
    [TestCase("iso-8859-1")]
    public void AutoUsesCharactersForOtherEncodings_ForcedModeRejects(string name)
    {
        var encoding = Encoding.GetEncoding(name);
        byte[] bytes = [.. encoding.Preamble, .. encoding.GetBytes("é,ok\r\n")];
        var resource = new ResourceDescriptor(Encoding: name);
        using var reader = new InspectingReader(new ShortStream(bytes), Profile(CsvInputMode.Auto, resource: resource));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.SourceName, Is.Not.EqualTo("Utf8RecordSource"));
        Assert.That(reader.GetString(0), Is.EqualTo("é"));
        using var forced = new CsvDataReader(new MemoryStream(bytes), Profile(CsvInputMode.Utf8Bytes, resource: resource));
        Assert.That(() => forced.Read(), Throws.TypeOf<NotSupportedException>().With.Message.Contains("UTF-8"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ProbedNonUtf8Bom_SelectsCharacters(bool async)
    {
        byte[] bytes = [.. Encoding.Unicode.Preamble, .. Encoding.Unicode.GetBytes("é,ok\r\n")];
        using var reader = new InspectingReader(new ShortStream(bytes, asyncOnly: async), Profile(CsvInputMode.Auto));
        Assert.That(async ? reader.ReadAsync().GetAwaiter().GetResult() : reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("é"));
        Assert.That(reader.SourceName, Is.Not.EqualTo("Utf8RecordSource"));
        using var forced = new CsvDataReader(new ShortStream(bytes), Profile(CsvInputMode.Utf8Bytes));
        Assert.That(() => forced.Read(), Throws.TypeOf<NotSupportedException>());
    }

    [TestCase(CsvInputMode.Auto)]
    [TestCase(CsvInputMode.Utf8Bytes)]
    [TestCase(CsvInputMode.DecodedChars)]
    public void ExplicitEncodingWinsOverConflictingBom(CsvInputMode mode)
    {
        byte[] bytes = [.. Encoding.Unicode.Preamble, .. Encoding.ASCII.GetBytes(",ok\r\n")];
        using var reader = new InspectingReader(new MemoryStream(bytes), Profile(mode, resource: new(Encoding: "utf-8")));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.SourceName == "Utf8RecordSource", Is.EqualTo(mode != CsvInputMode.DecodedChars));
        Assert.That(reader.GetString(0), Is.EqualTo(Encoding.UTF8.GetString(bytes[..2])));
    }

    [Test]
    public void InvalidUtf8UsesTheSameReplacementFallbackAsStreamReader()
    {
        byte[] bytes = [0xE2, 0x82, (byte)',', 0xFF, (byte)'\r', (byte)'\n'];
        Assert.That(Read(bytes, Profile(CsvInputMode.Utf8Bytes)),
            Is.EqualTo(Read(bytes, Profile(CsvInputMode.DecodedChars))));
        using var reader = new InspectingReader(new MemoryStream(bytes), Profile(CsvInputMode.Utf8Bytes));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.CurrentRecord.Slice(0).ToString(), Is.EqualTo(Encoding.UTF8.GetString(bytes[..2])));
        Assert.That(reader.CurrentRecord.Slice(1).ToString(), Is.EqualTo("�"));
    }

    [TestCase(CsvInputMode.Auto)]
    [TestCase(CsvInputMode.Utf8Bytes)]
    public void CompressedUtf8Input(CsvInputMode mode)
    {
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionMode.Compress, leaveOpen: true))
            gzip.Write(Encoding.UTF8.GetBytes("é,東京\r\n"));
        Assert.That(Read(compressed.ToArray(), Profile(mode, resource: new(Compression: "gzip"))),
            Is.EqualTo(new[] { new[] { "é", "東京" } }));
    }

    [Test]
    public void UnsupportedSyntaxFallsBackInAutoAndFailsInForcedMode()
    {
        var dialect = new DialectDescriptor(Header: false, Delimiter: '§', LineTerminator: "\r\n");
        var bytes = Encoding.UTF8.GetBytes("é§東京\r\n");
        Assert.That(Read(bytes, Profile(CsvInputMode.Auto, dialect, 4096)), Is.EqualTo(new[] { new[] { "é", "東京" } }));
        Assert.That(() => Read(bytes, Profile(CsvInputMode.Utf8Bytes, dialect)), Throws.TypeOf<NotSupportedException>());
    }

    [TestCase(CsvInputMode.Auto)]
    [TestCase(CsvInputMode.Utf8Bytes)]
    public async Task AsyncSourceUsesAsyncIo(CsvInputMode mode)
    {
        using var stream = new ShortStream(Encoding.UTF8.GetBytes("nom,n\r\né,42\r\n東京,43\r\n"), asyncOnly: true);
        using var reader = new InspectingReader(stream, Profile(mode, new(Header: true)));
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.SourceName, Is.EqualTo("Utf8RecordSource"));
        Assert.That(reader.GetName(0), Is.EqualTo("nom"));
        Assert.That(reader.GetString(0), Is.EqualTo("é"));
        Assert.That(reader.GetInt32(1), Is.EqualTo(42));
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("東京"));
        Assert.That(await reader.ReadAsync(), Is.False);
    }

    [Test]
    public async Task MixedReadsAndCancellation()
    {
        using var reader = new CsvDataReader(new ShortStream(Encoding.UTF8.GetBytes("42,true\r\n-43,false\r\n44,true")), Profile(CsvInputMode.Utf8Bytes));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(42));
        Assert.That(reader.GetBoolean(1), Is.True);
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetFieldValue<long>(0), Is.EqualTo(-43));
        Assert.That(reader.GetBoolean(1), Is.False);
        Assert.That(() => reader.ReadAsync(new CancellationToken(true)), Throws.InstanceOf<OperationCanceledException>());
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt16(0), Is.EqualTo(44));
        Assert.That(await reader.ReadAsync(), Is.False);
    }

    [Test]
    public void ResourceSanitizationAndSchemaParsers()
    {
        var resource = new ResourceDescriptorBuilder().WithSequence("NA", null).WithSequence("one", "1").Build();
        var schema = new SchemaDescriptorBuilder().Indexed().WithField<int>().WithField<string>().Build();
        var profile = new CsvProfile(new(Header: true), schema, resource) { StreamInitialization = StreamInitializationOptions.Utf8 };
        using var reader = new CsvDataReader(new MemoryStream(Encoding.UTF8.GetBytes("number,text\r\none,NA\r\n42,東京\r\n")), profile);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetValue(0), Is.EqualTo(1));
        Assert.That(reader.IsDBNull(1), Is.True);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetValue(0), Is.EqualTo(42));
        Assert.That(reader.GetValue(1), Is.EqualTo("東京"));
    }

    [Test]
    public void TypedAccessAndCustomSpanParsersPreserveSemantics()
    {
        var parsers = new RuntimeParsersDescriptorBuilder().WithParser<int>((ReadOnlySpan<char> text) => int.Parse(text) + 1).Build();
        var profile = new CsvProfile(new(Header: false), parsers: parsers) { StreamInitialization = StreamInitializationOptions.Utf8 };
        using var reader = new CsvDataReader(new MemoryStream(Encoding.UTF8.GetBytes("42,東京\r\n")), profile);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(43));
        Assert.That(reader.GetFieldValue(1, (ReadOnlySpan<char> text) => text.Length), Is.EqualTo(2));
        Assert.That(reader.GetRawString(1), Is.EqualTo("東京"));
    }

    [Test]
    public void BuilderAndObjectMappingUseTheSelectedModeAndLeaveStreamOpen()
    {
        var csv = new CsvReaderBuilder().WithStreamInitialization(StreamInitializationOptions.Utf8)
            .WithDialect(d => d.WithoutHeader()).Build();
        using var stream = new ShortStream(Encoding.UTF8.GetBytes("é,東京\r\n"));
        var result = csv.To(stream, new SpanMapper<string>((text, fields) => text.Slice(fields.ElementAt(1).Value.Start, fields.ElementAt(1).Value.Length).ToString())).ToArray();
        Assert.That(result, Is.EqualTo(new[] { "東京" }));
        Assert.That(stream.CanRead, Is.True);
    }

    [Test]
    public async Task AsyncObjectMappingUsesCharacterOffsetsForUnicode()
    {
        using var stream = new ShortStream(Encoding.UTF8.GetBytes("é,東京\r\n"), asyncOnly: true);
        var csv = new CsvReader(Profile(CsvInputMode.Utf8Bytes));
        var values = new List<string>();
        await foreach (var value in csv.ToAsync(stream, new SpanMapper<string>((text, fields) => text.Slice(fields.ElementAt(1).Value.Start, fields.ElementAt(1).Value.Length).ToString())))
            values.Add(value);
        Assert.That(values, Is.EqualTo(new[] { "東京" }));
        Assert.That(stream.CanRead, Is.True);
    }

    [Test]
    public void CharacterRecordViewsRemainConsistentWithFieldSlices()
    {
        using var reader = new InspectingReader(new MemoryStream(Encoding.UTF8.GetBytes("é,東京\r\n")), Profile(CsvInputMode.Utf8Bytes));
        Assert.That(reader.Read(), Is.True);
        var record = reader.CurrentRecord;
        var field = record.FieldSpans[1].Value;
        Assert.That(record.Span.Slice(field.Start, field.Length).ToString(), Is.EqualTo("東京"));
        Assert.That(record.Slice(1).ToString(), Is.EqualTo("東京"));
        Assert.That(reader.GetString(0), Is.EqualTo("é"));
    }

    [Test]
    public void ArrayAndBadDataProfilesFallBackToCharacters()
    {
        var dialect = new DialectDescriptor(Header: false, ArrayDelimiter: '|', ArrayPrefix: '[', ArraySuffix: ']');
        using var reader = new InspectingReader(new MemoryStream(Encoding.UTF8.GetBytes("[1|2],ok\r\n")), Profile(CsvInputMode.Auto, dialect, 4096));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.SourceName, Is.Not.EqualTo("Utf8RecordSource"));
        Assert.That(reader.GetArray<int>(0), Is.EqualTo(new[] { 1, 2 }));

        var profile = Profile(CsvInputMode.Auto);
        profile.BadDataPolicy = new();
        using var policyReader = new InspectingReader(new MemoryStream(Encoding.UTF8.GetBytes("1,ok\r\n")), profile);
        Assert.That(policyReader.Read(), Is.True);
        Assert.That(policyReader.SourceName, Is.Not.EqualTo("Utf8RecordSource"));
    }

    [TestCase(" 42 ", 42)]
    [TestCase("-43", -43)]
    [TestCase("+44", 44)]
    public void IntegerFastPathRetainsCharacterParsingSemantics(string text, int expected)
    {
        using var reader = new CsvDataReader(new MemoryStream(Encoding.UTF8.GetBytes(text)), Profile(CsvInputMode.Utf8Bytes));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(expected));
        Assert.That(reader.GetString(0), Is.EqualTo(text));
    }

    [TestCase("42x", typeof(FormatException))]
    [TestCase("2147483648", typeof(OverflowException))]
    public void IntegerFastPathDoesNotAcceptPartialOrOverflowingValues(string text, Type exception)
    {
        using var reader = new CsvDataReader(new MemoryStream(Encoding.UTF8.GetBytes(text)), Profile(CsvInputMode.Utf8Bytes));
        Assert.That(reader.Read(), Is.True);
        Assert.That(() => reader.GetInt32(0), Throws.TypeOf(exception));
    }

    [Test]
    public async Task PooledFieldDecodingResetsAcrossRecordsAndPreservesReturnedStrings()
    {
        var longText = string.Concat(Enumerable.Repeat("東京😀", 5000));
        var csv = $"é,x,\"\"\r\n{longText},second,z\r\na,b,\r\n";
        using var reader = new InspectingReader(new MemoryStream(Encoding.UTF8.GetBytes(csv)), Profile(CsvInputMode.Utf8Bytes, bufferSize: 17));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(2), Is.Empty);
        var first = reader.GetString(0);
        var firstField = reader.CurrentRecord.Slice(0);
        Assert.That(reader.CurrentRecord.Slice(0).Equals(firstField), Is.True, "Repeated access should reuse the decoded field memory.");
        Assert.That(reader.GetString(1), Is.EqualTo("x"));
        Assert.That(reader.CurrentRecord.Slice(1).ToString(), Is.EqualTo("x"));
        Assert.That(firstField.ToString(), Is.EqualTo("é"));
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetString(2), Is.EqualTo("z"));
        Assert.That(reader.GetString(0), Is.EqualTo(longText));
        Assert.That(reader.CurrentRecord.Slice(0).ToString(), Is.EqualTo(longText));
        Assert.That(reader.GetString(1), Is.EqualTo("second"));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(1), Is.EqualTo("b"));
        Assert.That(reader.GetString(0), Is.EqualTo("a"));
        Assert.That(reader.GetString(2), Is.Empty);
        Assert.That(first, Is.EqualTo("é"));
    }

    [TestCase(17)]
    [TestCase(128)]
    [TestCase(4096)]
    public void BulkScanningMatchesCharactersForGeneratedQuotedRecords(int bufferSize)
    {
        var random = new Random(326);
        string[] fragments = ["abc", "é", "東京", "😀", "\"", "\\", ",", "\r\n", "#", "  "];
        var csv = new StringBuilder();
        for (int row = 0; row < 100; row++)
        {
            for (int column = 0; column < 8; column++)
            {
                if (column > 0) csv.Append(',');
                var value = string.Concat(Enumerable.Range(0, random.Next(30)).Select(_ => fragments[random.Next(fragments.Length)]));
                csv.Append('"').Append(value.Replace("\\", "\\\\").Replace("\"", "\"\"")).Append('"');
            }
            csv.Append("\r\n");
        }
        var bytes = Encoding.UTF8.GetBytes(csv.ToString());
        var dialect = new DialectDescriptor(Header: false, EscapeChar: '\\');
        Assert.That(Read(bytes, Profile(CsvInputMode.Utf8Bytes, dialect, bufferSize)),
            Is.EqualTo(Read(bytes, Profile(CsvInputMode.DecodedChars, dialect, bufferSize))));
    }

    [TestCase("ordinary|text,more||last,row||", "||", true)]
    [TestCase("abcENx,defENDghi,jklEND", "END", true)]
    [TestCase("abc\\,def,ghi\r\n", "\r\n", false)]
    public void BulkScanningRetainsPartialTerminatorsAndEscapes(string csv, string terminator, bool quoted)
    {
        var bytes = Encoding.UTF8.GetBytes(csv);
        var dialect = new DialectDescriptor(Header: false, LineTerminator: terminator, EscapeChar: '\\', QuoteChar: quoted ? '"' : null);
        foreach (int size in new[] { 1, 7, 4096 })
            Assert.That(Read(bytes, Profile(CsvInputMode.Utf8Bytes, dialect, size)),
                Is.EqualTo(Read(bytes, Profile(CsvInputMode.DecodedChars, dialect, size))));
    }

    [TestCase("\"a\"ordinary,b\r\n")]
    [TestCase("\"a\"東京,b\r\n")]
    [TestCase("\"unterminated")]
    public void BulkScanningDoesNotHideMalformedQuotes(string csv)
    {
        var bytes = Encoding.UTF8.GetBytes(csv);
        Assert.That(() => Read(bytes, Profile(CsvInputMode.Utf8Bytes)), Throws.TypeOf<InvalidDataException>());
        Assert.That(() => Read(bytes, Profile(CsvInputMode.DecodedChars)), Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void DirectStringDecodingHonorsCustomPoolsAndUnescaping()
    {
        int calls = 0;
        var profile = Profile(CsvInputMode.Utf8Bytes);
        profile.ParserOptimizations = profile.ParserOptimizations with
        {
            PoolString = text => { calls++; return text.ToString().ToUpperInvariant(); }
        };
        using var reader = new CsvDataReader(new MemoryStream(Encoding.UTF8.GetBytes("é,\"a\"\"b\"\r\n")), profile);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("É"));
        Assert.That(reader.GetString(1), Is.EqualTo("A\"B"));
        Assert.That(calls, Is.EqualTo(2));
    }

    [Test]
    public void WideCharacterViewsTranslateOffsetsIncludingEmptyAndQuotedFields()
    {
        var csv = string.Concat(Enumerable.Repeat("東京,\"😀\",,\"\",é,", 20)) + "last\r\n";
        using var reader = new InspectingReader(new MemoryStream(Encoding.UTF8.GetBytes(csv)), Profile(CsvInputMode.Utf8Bytes));
        Assert.That(reader.Read(), Is.True);
        var record = reader.CurrentRecord;
        var text = record.Span;
        for (int i = 0; i < record.FieldSpans.Length; i++)
        {
            var field = record.FieldSpans[i].Value;
            Assert.That(text.Slice(field.Start, field.Length).ToString(), Is.EqualTo(reader.GetRawString(i).Trim('"')));
        }
    }

    [Test]
    public void DirectStringDecodingPreservesDerivedReaderValueOverrides()
    {
        using var reader = new TransformingReader(new MemoryStream(Encoding.UTF8.GetBytes("original\r\n")), Profile(CsvInputMode.Utf8Bytes));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("transformed"));
    }

    private sealed class TransformingReader(Stream stream, CsvProfile profile) : CsvDataReader(stream, profile)
    {
        protected override NullableSpan GetValueOrThrow(int ordinal) => new("transformed".AsSpan());
    }

    private sealed class InspectingReader(Stream stream, CsvProfile profile) : CsvDataReader(stream, profile)
    {
        public string SourceName => RecordSource!.GetType().Name;
        public RecordMemory CurrentRecord => Record!;
    }

    private sealed class ShortStream(byte[] bytes, bool asyncOnly = false) : Stream
    {
        private readonly MemoryStream _inner = new(bytes);
        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (asyncOnly) throw new InvalidOperationException("Synchronous I/O was used.");
            return _inner.Read(buffer, offset, Math.Min(1, count));
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => _inner.ReadAsync(buffer[..Math.Min(1, buffer.Length)], cancellationToken);
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}




