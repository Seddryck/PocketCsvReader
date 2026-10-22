using System.Data;
using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing;

[TestFixture]
public class RowNumberTest
{
    private static MemoryStream StreamOf(string text) => new(Encoding.UTF8.GetBytes(text));

    private static CsvProfile Profile(bool header = true, CsvInputMode mode = CsvInputMode.DecodedChars)
        => new(new DialectDescriptorBuilder().WithHeader(header).WithLineTerminator("\n").Build())
        {
            RowNumber = new(),
            StreamInitialization = new(false, mode),
            ParserOptimizations = new(BufferSize: 32)
        };

    [Test]
    public void Builder_DefaultAndLastCallWins()
    {
        Assert.That(new CsvReaderBuilder().Build().Profile.RowNumber, Is.Null);
        Assert.That(new CsvReaderBuilder().WithRowNumber().Build().Profile.RowNumber!.ColumnName,
            Is.EqualTo("rowNumber"));
        Assert.That(new CsvReaderBuilder().WithRowNumber("id").WithoutRowNumber().Build().Profile.RowNumber,
            Is.Null);
        Assert.That(new CsvReaderBuilder().WithoutRowNumber().WithRowNumber("id").Build().Profile.RowNumber!.ColumnName,
            Is.EqualTo("id"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" \t")]
    public void InvalidNames_AreRejectedByBuilderAndProfile(string? name)
    {
        Assert.That(() => new CsvReaderBuilder().WithRowNumber(name!), Throws.InstanceOf<ArgumentException>());
        var profile = Profile();
        Assert.That(() => profile.RowNumber = new(name!), Throws.InstanceOf<ArgumentException>());
    }

    [TestCase(true, CsvInputMode.DecodedChars)]
    [TestCase(false, CsvInputMode.DecodedChars)]
    [TestCase(true, CsvInputMode.Utf8Bytes)]
    [TestCase(false, CsvInputMode.Utf8Bytes)]
    [TestCase(true, CsvInputMode.Auto)]
    public void Reader_PrependsTypedNumbersAndPreservesSourceValues(bool header, CsvInputMode mode)
    {
        var profile = Profile(header, mode);
        profile.RowNumber = new("recordId");
        using var reader = new CsvDataReader(StreamOf((header ? "name,city\n" : "") + "Alice,Brüssels\nBob,Ghent"), profile);
        Assert.That(reader.Read(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.FieldCount, Is.EqualTo(3));
            Assert.That(reader.GetName(0), Is.EqualTo("recordId"));
            Assert.That(reader.GetFieldType(0), Is.EqualTo(typeof(int)));
            Assert.That(reader.GetDataTypeName(0), Is.EqualTo("integer"));
            Assert.That(reader["recordId"], Is.TypeOf<int>().And.EqualTo(1));
            Assert.That(reader.GetInt32(0), Is.EqualTo(1));
            Assert.That(reader.GetInt64(0), Is.EqualTo(1L));
            Assert.That(reader.GetFieldValue<int>(0), Is.EqualTo(1));
            Assert.That(reader.GetString(0), Is.EqualTo("1"));
            Assert.That(reader.GetRawString(0), Is.EqualTo("1"));
            Assert.That(reader.IsDBNull(0), Is.False);
            Assert.That(reader.GetString(1), Is.EqualTo("Alice"));
            Assert.That(reader.GetString(2), Is.EqualTo("Brüssels"));
        });
        Assert.That(reader.Read(), Is.True);
        var values = new object[3];
        Assert.That(reader.GetValues(values), Is.EqualTo(3));
        Assert.That(values, Is.EqualTo(new object[] { 2, "Bob", "Ghent" }));
        Assert.That(reader.Read(), Is.False);
    }

    [TestCase(CsvInputMode.DecodedChars)]
    [TestCase(CsvInputMode.Utf8Bytes)]
    public async Task CommentsSkippedRowsAndMultilineRecords_DoNotConsumeNumbers(CsvInputMode mode)
    {
        var profile = Profile(mode: mode);
        profile = new CsvProfile(profile.Dialect with { CommentChar = '#', CommentRows = [3] })
        {
            RowNumber = new(), StreamInitialization = profile.StreamInitialization,
            ParserOptimizations = profile.ParserOptimizations
        };
        using var reader = new CsvDataReader(StreamOf("name\n# comment\n\"Alice\nSmith\"\nskip\nBob\n"), profile);
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetString(1), Is.EqualTo("Alice\nSmith"));
        Assert.That(reader.GetInt32(0), Is.EqualTo(1));
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetString(1), Is.EqualTo("Bob"));
        Assert.That(reader.GetInt32(0), Is.EqualTo(2));
        Assert.That(await reader.ReadAsync(), Is.False);
    }

    [TestCase("")]
    [TestCase("name\n")]
    public void EmptyInput_ReturnsNoGeneratedDataRows(string text)
    {
        var profile = Profile();
        var reader = new CsvReader(profile);
        Assert.That(reader.ToObjectArrays(StreamOf(text)), Is.Empty);
        Assert.That(reader.ToDataTable(StreamOf(text)).Rows, Is.Empty);
    }

    [TestCase("rowNumber", "rowNumber")]
    [TestCase("recordId", "recordId")]
    [TestCase("field_0", null)]
    public async Task Collisions_ThrowActionableErrorBeforeReturningARow(string generatedName, string? header)
    {
        var profile = Profile(header is not null);
        profile.RowNumber = new(generatedName);
        var content = (header is null ? "" : header + "\n") + "Alice\n";
        using var reader = new CsvDataReader(StreamOf(content), profile);
        var error = Assert.Throws<InvalidDataException>(() => reader.Read());
        Assert.That(error!.Message, Does.Contain(generatedName).And.Contain("Choose another name"));
        await using var asyncReader = new CsvDataReader(StreamOf(content), profile);
        var asyncError = await Assert.ThrowsAsync<InvalidDataException>(async () => await asyncReader.ReadAsync());
        Assert.That(asyncError!.Message, Is.EqualTo(error.Message));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Schema_SourceOrdinalsAndNamesRemainUnchanged(bool named)
    {
        var profile = Profile();
        profile = new CsvProfile(profile.Dialect,
            named ? new SchemaDescriptorBuilder().Named().WithField<int>("age").Build()
                : new SchemaDescriptorBuilder().Indexed().WithField<int>().Build())
        {
            RowNumber = new()
        };
        using var reader = new CsvDataReader(StreamOf("age\n42\n"), profile);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetFieldType(1), Is.EqualTo(typeof(int)));
        Assert.That(reader.GetValue(1), Is.TypeOf<int>().And.EqualTo(42));
        Assert.That(reader.GetInt32(1), Is.EqualTo(42));
        Assert.That(reader.GetValue(0), Is.TypeOf<int>().And.EqualTo(1));
    }

    [TestCase(true, true)]
    [TestCase(true, false)]
    [TestCase(false, false)]
    public async Task Batch_NumberingContinuesAcrossEmptyFilesAndMixedReads(bool header, bool repeat)
    {
        var profile = Profile(header);
        profile = new CsvProfile(profile.Dialect with { HeaderRepeat = repeat }) { RowNumber = new() };
        await using var reader = new CsvBatchDataReader(
            [StreamOf((header ? "name\n" : "") + "Alice\n"), StreamOf(""),
                StreamOf((header && repeat ? "name\n" : "") + "Bob\n")], profile);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(1));
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(2));
        Assert.That(reader.GetString(1), Is.EqualTo("Bob"));
        Assert.That(reader.FieldCount, Is.EqualTo(2));
        Assert.That(await reader.ReadAsync(), Is.False);
    }

    [Test]
    public void Materializers_ExposeTheGeneratedColumnWithAppropriateTypes()
    {
        var reader = new CsvReader(Profile());
        var table = reader.ToDataTable(StreamOf("name\nAlice\n"));
        Assert.That(table.Columns[0].ColumnName, Is.EqualTo("rowNumber"));
        Assert.That(table.Columns[0].DataType, Is.EqualTo(typeof(int)));
        Assert.That(table.Rows[0].ItemArray, Is.EqualTo(new object[] { 1, "Alice" }));
        Assert.That(reader.ToArrayString(StreamOf("name\nAlice\n")).Single(),
            Is.EqualTo(new[] { "1", "Alice" }));
        Assert.That(reader.ToObjectArrays(StreamOf("name\nAlice\n")).Single(),
            Is.EqualTo(new object[] { 1, "Alice" }));
        Assert.That(reader.ToDictionaries(StreamOf("name\nAlice\n")).Single()["rowNumber"],
            Is.TypeOf<int>().And.EqualTo(1));
    }

    public record NumberedPerson(int RowNumber, string Name);

    [TestCase(CsvInputMode.DecodedChars)]
    [TestCase(CsvInputMode.Utf8Bytes)]
    public void ObjectMapping_IncludesGeneratedFieldInConstructorAndCustomMapper(CsvInputMode mode)
    {
        var profile = Profile(mode: mode);
        using var reader = new CsvObjectReader<NumberedPerson>(StreamOf("name\nAlice\nBob"), profile);
        Assert.That(reader.Read(), Is.EqualTo(new[] { new NumberedPerson(1, "Alice"), new NumberedPerson(2, "Bob") }));
        using var custom = new CsvObjectReader<string>(StreamOf("name\nAlice"), profile,
            (span, fields) => span.Slice(fields.First().Value.Start, fields.First().Value.Length).ToString()
                + "-" + span.Slice(fields.Last().Value.Start, fields.Last().Value.Length).ToString());
        Assert.That(custom.Read().Single(), Is.EqualTo("1-Alice"));
    }

    [Test]
    public async Task DbDataReader_ExposesGeneratedMetadataAndAsyncValues()
    {
        await using var reader = new CsvReader(Profile()).ToDbDataReader(StreamOf("name\nAlice\n"));
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetFieldType(0), Is.EqualTo(typeof(int)));
        Assert.That(reader.GetValue(0), Is.TypeOf<int>().And.EqualTo(1));
        Assert.That(await reader.GetFieldValueAsync<int>(0), Is.EqualTo(1));
    }

    [Test]
    public void BufferSizeConstructor_PreservesConfigurationAndNumberingRestartsPerReader()
    {
        var reader = new CsvReader(Profile(), 64);
        foreach (var name in new[] { "Alice", "Bob" })
        {
            using var data = reader.ToDataReader(StreamOf("name\n" + name));
            Assert.That(data.Read(), Is.True);
            Assert.That(data.GetValue(0), Is.EqualTo(1));
            Assert.That(data.GetString(1), Is.EqualTo(name));
        }
    }

    [Test]
    public void ArraysAndQuotedFields_KeepCorrectOffsets()
    {
        var profile = new CsvProfile(new DialectDescriptorBuilder().WithLineTerminator("\n")
            .WithArray(a => a.WithDelimiter('|').WithPrefix('[').WithSuffix(']')).Build()) { RowNumber = new() };
        using var reader = new CsvDataReader(StreamOf("numbers,text\n[2|3],\"hello, world\"\n"), profile);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetArray<int>(1), Is.EqualTo(new[] { 2, 3 }));
        Assert.That(reader.GetRawString(2), Is.EqualTo("\"hello, world\""));
    }

    [TestCase(CsvInputMode.DecodedChars)]
    [TestCase(CsvInputMode.Utf8Bytes)]
    public async Task AsyncObjectMapping_HandlesLargeUnicodeRecordsAndDigitBoundaries(CsvInputMode mode)
    {
        var name = string.Concat(Enumerable.Repeat("Brüssels東京", 100));
        using var stream = StreamOf("name\n" + string.Join("\n", Enumerable.Repeat(name, 105)));
        var people = new List<NumberedPerson>();
        await foreach (var person in new CsvReader(Profile(mode: mode)).ToAsync<NumberedPerson>(stream))
            people.Add(person);
        Assert.That(people.Select(p => p.RowNumber), Is.EqualTo(Enumerable.Range(1, 105)));
        Assert.That(people.All(p => p.Name == name), Is.True);
    }

    [TestCase(CsvInputMode.DecodedChars)]
    [TestCase(CsvInputMode.Auto)]
    public async Task SkippedBadRecords_DoNotConsumeNumbersOrChangeSourcePositions(CsvInputMode mode)
    {
        var profile = Profile(false, mode);
        profile.BadDataPolicy = new(_ => BadDataAction.SkipRecord);
        profile.ParserOptimizations = profile.ParserOptimizations with { TrackPosition = true };
        await using var reader = new CsvDataReader(StreamOf("Alice\n\"bad\"x\nBob"), profile);
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(1));
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(2));
        Assert.That(reader.GetString(1), Is.EqualTo("Bob"));
        Assert.That(reader.CurrentPosition!.Value.PhysicalLine, Is.EqualTo(3));
        Assert.That(await reader.ReadAsync(), Is.False);
    }

    [Test]
    public void Collisions_AreCheckedInLaterBatchFilesAndObjectMapping()
    {
        var profile = Profile();
        using var batch = new CsvBatchDataReader([StreamOf("name\nAlice"), StreamOf("rowNumber\nBob")], profile);
        Assert.That(batch.Read(), Is.True);
        Assert.That(() => batch.Read(), Throws.TypeOf<InvalidDataException>().With.Message.Contains("Choose another name"));
        using var objects = new CsvObjectReader<NumberedPerson>(StreamOf("rowNumber\nAlice"), profile);
        Assert.That(() => objects.Read().ToArray(), Throws.TypeOf<InvalidDataException>().With.Message.Contains("Choose another name"));
    }

    [Test]
    public void SyntheticValue_IsNotTransformedBySourceSequencesOrParsers()
    {
        var profile = new CsvProfile(Profile().Dialect,
            new SchemaDescriptorBuilder().Indexed().WithField<int>().Build(),
            new ResourceDescriptorBuilder().WithSequence("1", "42").Build(),
            new RuntimeParsersDescriptorBuilder().WithParser<int>((string value) => int.Parse(value) + 10).Build())
        { RowNumber = new() };
        using var reader = new CsvDataReader(StreamOf("age\n1"), profile);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetValue(0), Is.TypeOf<int>().And.EqualTo(1));
        Assert.That(reader.GetInt32(0), Is.EqualTo(1));
        Assert.That(reader.GetFieldValue<int>(0), Is.EqualTo(1));
        Assert.That(reader.GetString(0), Is.EqualTo("1"));
        var sourceProfile = new CsvProfile(profile.Dialect, profile.Schema, profile.Resource, profile.Parsers);
        using var source = new CsvDataReader(StreamOf("age\n1"), sourceProfile);
        Assert.That(source.Read(), Is.True);
        Assert.That(reader.GetValue(1), Is.EqualTo(source.GetValue(0)));
    }

    [Test]
    public async Task AsyncOnlyStream_NumberingDoesNotRequireSynchronousIo()
    {
        await using var reader = new CsvDataReader(new AsyncOnlyStream("name\nAlice\nBob"), Profile());
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(1));
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(2));
        Assert.That(await reader.ReadAsync(), Is.False);
    }

    private sealed class AsyncOnlyStream(string content) : Stream
    {
        private readonly MemoryStream _inner = StreamOf(content);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Synchronous read used.");
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => _inner.ReadAsync(buffer, cancellationToken);
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
