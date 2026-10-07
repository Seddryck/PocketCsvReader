using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using PocketCsvReader.Configuration;
using PocketCsvReader.Ndjson.Configuration;

namespace PocketCsvReader.Ndjson.Testing;

[TestFixture]
public class NdjsonReaderTest
{
    [Test]
    public async Task ToDbDataReader_ExposesNdjsonRecords()
    {
        await using var reader = new NdjsonReader(new NdjsonProfile("\n"))
            .ToDbDataReader(new MemoryStream(Encoding.UTF8.GetBytes("{\"id\":1}")));

        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetInt32(reader.GetOrdinal("id")), Is.EqualTo(1));
    }

    [Test]
    public async Task ToDataReader_ReadAsync_AdvancesAndExposesValues()
    {
        await using var reader = new NdjsonReader(new NdjsonProfile("\n"))
            .ToDataReader(new MemoryStream(Encoding.UTF8.GetBytes("{\"id\":1}\n{\"id\":2}")));

        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetInt32(reader.GetOrdinal("id")), Is.EqualTo(1));
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetInt32(reader.GetOrdinal("id")), Is.EqualTo(2));
        Assert.That(await reader.ReadAsync(), Is.False);
    }

    [Test]
    public async Task ToDataReader_ReadAsync_UsesOnlyAsynchronousStreamOperations()
    {
        await using var stream = new AsyncOnlyStream(
            new MemoryStream(Encoding.UTF8.GetBytes("{\"id\":1}\n{\"id\":2}")));
        await using var reader = new NdjsonReader(new NdjsonProfile("\n")).ToDataReader(stream);

        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetInt32(reader.GetOrdinal("id")), Is.EqualTo(1));
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetInt32(reader.GetOrdinal("id")), Is.EqualTo(2));
        Assert.That(await reader.ReadAsync(), Is.False);
    }

    [Test]
    public void ToDataReader_LargeDocument_ReturnsFirstRecordBeforeReadingCompleteInput()
    {
        var content = string.Join("\n", Enumerable.Range(0, 10_000).Select(index => $"{{\"id\":{index}}}"));
        var bytes = Encoding.UTF8.GetBytes(content);
        using var stream = new TrackingStream(new MemoryStream(bytes), maxReadSize: 64);
        using var reader = new NdjsonReader(new NdjsonProfile("\n")).ToDataReader(stream);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(reader.GetOrdinal("id")), Is.Zero);
        Assert.That(stream.BytesRead, Is.LessThan(bytes.Length));
    }

    [Test]
    [TestCase(@"Resources\metrics.ndjson")]
    public void ToDataReader_Metrics_Successful(string filename)
    {
        var rowCount = 0;
        var profile = new NdjsonProfile(Environment.NewLine);
        var reader = new NdjsonReader(profile).ToDataReader(filename);
        while (reader.Read())
        {
            rowCount++;
            for (var i = 0; i < reader.FieldCount; i++)
                reader.GetString(i);
        }
        Assert.That(rowCount, Is.EqualTo(7));
    }

    [Test]
    [TestCase(@"Resources\metrics.ndjson")]
    public void ToDataReader_MetricsStream_Successful(string filename)
    {
        using var stream = File.OpenRead(filename);
        var profile = new NdjsonProfile(Environment.NewLine);
        var reader = new NdjsonReader(profile).ToDataReader(filename);
        var rowCount = 0;
        while (reader.Read())
        {
            rowCount++;
            for (var i = 0; i < reader.FieldCount; i++)
                reader.GetString(i);
        }
        Assert.That(rowCount, Is.EqualTo(7));
    }

    [Test]
    public void ToDataReader_ArrayField_ReturnsTypedArray()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"values\": [10, 25, 36]}"));
        using var reader = new NdjsonReader().ToDataReader(stream);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetArray<int>(0), Is.EqualTo(new[] { 10, 25, 36 }));
    }

    [Test]
    public void ToDataReader_QuotedAndEmptyArrays_ReturnsArrays()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"values\": [\"a]b\", \"qrz\"], \"empty\": []}"));
        using var reader = new NdjsonReader().ToDataReader(stream);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetArray<string>(0), Is.EqualTo(new[] { "a]b", "qrz" }));
        Assert.That(reader.GetArray<string>(1), Is.Empty);
    }

    [Test]
    public void ToDataReader_RootValues_ExposesOrdinalZero()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("[1,2,3]\n\"Ada\"\n42\ntrue\nnull"));
        using var reader = new NdjsonReader(new NdjsonProfile("\n")).ToDataReader(stream);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetArray<int>(0), Is.EqualTo(new[] { 1, 2, 3 }));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("Ada"));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(42));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetBoolean(0), Is.True);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.IsDBNull(0), Is.True);
        Assert.That(reader.GetValue(0), Is.SameAs(DBNull.Value));
        Assert.That(reader.Read(), Is.False);
    }

    [Test]
    public void ToDataReader_EmptyObjects_ReturnsZeroFieldRecords()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{}\n{ }\n{}"));
        using var reader = new NdjsonReader(new NdjsonProfile("\n")).ToDataReader(stream);

        for (var i = 0; i < 3; i++)
        {
            Assert.That(reader.Read(), Is.True);
            Assert.That(reader.FieldCount, Is.Zero);
        }

        Assert.That(reader.Read(), Is.False);
        Assert.That(reader.RowCount, Is.EqualTo(3));
    }

    [Test]
    public void ToDataReader_JsonWhitespaceAroundTokens_ReturnsValues()
    {
        const string content = "\t{\t\"value\"\t:\t42\t,\t\"items\"\t:\t[\ttrue\t,\tfalse\t]\t}\t";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        using var reader = new NdjsonReader().ToDataReader(stream);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(reader.GetOrdinal("value")), Is.EqualTo(42));
        Assert.That(reader.GetArray<bool>(reader.GetOrdinal("items")), Is.EqualTo(new[] { true, false }));
    }

    [TestCase("\n")]
    [TestCase("\r")]
    [TestCase("\r\n")]
    [TestCase("|")]
    [TestCase("<END>")]
    public void ToDataReader_ConfiguredLineTerminator_SplitsRecords(string lineTerminator)
    {
        var content = $"{{\"value\":1}}{lineTerminator}{{\"value\":2}}{lineTerminator}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        using var reader = new NdjsonReader(new NdjsonProfile(lineTerminator)).ToDataReader(stream);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(reader.GetOrdinal("value")), Is.EqualTo(1));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(reader.GetOrdinal("value")), Is.EqualTo(2));
        Assert.That(reader.Read(), Is.False);
    }

    [Test]
    public void ToDataReader_CustomTerminatorInsideString_PreservesStringContent()
    {
        const string content = "{\"value\":\"left|quoted \\\"| right\"}|{\"value\":\"next\"}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        using var reader = new NdjsonReader(new NdjsonProfile("|")).ToDataReader(stream);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(reader.GetOrdinal("value")), Is.EqualTo("left|quoted \"| right"));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(reader.GetOrdinal("value")), Is.EqualTo("next"));
        Assert.That(reader.Read(), Is.False);
    }

    [Test]
    public void ToDataReader_CustomTerminator_PreservesJsonNewlineWhitespace()
    {
        const string content = "{\n\"value\": 1\n}<END>\r\n{\n\"value\": 2\n}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        using var reader = new NdjsonReader(new NdjsonProfile("<END>")).ToDataReader(stream);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(reader.GetOrdinal("value")), Is.EqualTo(1));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(reader.GetOrdinal("value")), Is.EqualTo(2));
        Assert.That(reader.Read(), Is.False);
    }

    [Test]
    public void ToDataReader_CrLfTerminator_DoesNotAcceptLoneLineFeed()
    {
        const string content = "{\"value\":1}\n{\"value\":2}\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        using var reader = new NdjsonReader(new NdjsonProfile("\r\n")).ToDataReader(stream);

        Assert.Throws<InvalidDataException>(() => reader.Read());
    }

    [Test]
    public void ToDataReader_MultiCharacterTerminatorAcrossBufferBoundary_SplitsRecords()
    {
        var value = new string('x', 70 * 1024);
        var content = $"{{\"value\":\"{value}\"}}<END>{{\"value\":\"done\"}}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        using var reader = new NdjsonReader(new NdjsonProfile("<END>")).ToDataReader(stream);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(reader.GetOrdinal("value")), Has.Length.EqualTo(value.Length));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(reader.GetOrdinal("value")), Is.EqualTo("done"));
        Assert.That(reader.Read(), Is.False);
    }

    [Test]
    public void ToDataReader_CustomTerminator_SkipsBlankRecordsAndReturnsFinalUnterminatedRecord()
    {
        const string content = "|  |{\"value\":1}||{\"value\":2}";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        using var reader = new NdjsonReader(new NdjsonProfile("|")).ToDataReader(stream);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(reader.GetOrdinal("value")), Is.EqualTo(1));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(reader.GetOrdinal("value")), Is.EqualTo(2));
        Assert.That(reader.Read(), Is.False);
    }

    [TestCase("\n")]
    [TestCase("\r")]
    [TestCase("\r\n")]
    [TestCase("|")]
    [TestCase("<END>")]
    public void ToDataReader_ConfiguredComments_SkipsFullLineAndTrailingComments(string lineTerminator)
    {
        var content = string.Join(lineTerminator,
            "# before \"unterminated quote",
            "{\"value\":1,\"text\":\"# retained\"} # trailing \"unterminated quote",
            "# between",
            "42# trailing primitive",
            "# after");
        var profile = new NdjsonProfile(
            new NdjsonDialectDescriptorBuilder()
                .WithLineTerminator(lineTerminator)
                .WithCommentChar('#')
                .Build());
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        using var reader = new NdjsonReader(profile).ToDataReader(stream);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(reader.GetOrdinal("value")), Is.EqualTo(1));
        Assert.That(reader.GetString(reader.GetOrdinal("text")), Is.EqualTo("# retained"));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(42));
        Assert.That(reader.Read(), Is.False);
        Assert.That(reader.RowCount, Is.EqualTo(2));
    }

    [Test]
    public void ToDataReader_CommentWithoutConfiguration_IsRejected()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("# comment"));
        using var reader = new NdjsonReader().ToDataReader(stream);

        Assert.Throws<InvalidDataException>(() => reader.Read());
    }

    [Test]
    public void ToDataReader_InvalidPrefixBeforeFirstCommentMarker_IsRejected()
    {
        const string content = "{\"value\":# first # second}";
        var profile = new NdjsonProfile(
            new NdjsonDialectDescriptorBuilder()
                .WithCommentChar('#')
                .Build());
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        using var reader = new NdjsonReader(profile).ToDataReader(stream);

        Assert.Throws<InvalidDataException>(() => reader.Read());
    }

    [Test]
    public void ReaderBuilder_CommentConfiguration_IsApplied()
    {
        var reader = new NdjsonReaderBuilder()
            .WithDialect(dialect => dialect.WithCommentChar('#'))
            .Build();

        Assert.That(reader.Dialect.CommentChar, Is.EqualTo('#'));
    }

    [Test]
    public void Projection_ExposesOnlySelectedPropertiesInProjectionOrder()
    {
        const string content = """
            {"ignored":{"nested":[1,true,"value"]},"count":7,"na\u006de":"Ada","amount":12.50}
            {"amount":25.75,"other":[1,{"deep":false}],"name":"Grace","count":8}
            """;
        var ndjson = new NdjsonReaderBuilder()
            .WithDialect(dialect => dialect.WithLineTerminator("\n"))
            .WithProjection(projection => projection
                .Property("name")
                .Property("amount")
                .Property("count"))
            .Build();
        using var reader = ndjson.ToDataReader(
            new MemoryStream(Encoding.UTF8.GetBytes(content)));

        Assert.That(reader.Read(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.FieldCount, Is.EqualTo(3));
            Assert.That(reader.GetName(0), Is.EqualTo("name"));
            Assert.That(reader.GetName(1), Is.EqualTo("amount"));
            Assert.That(reader.GetName(2), Is.EqualTo("count"));
            Assert.That(reader.GetString(0), Is.EqualTo("Ada"));
            Assert.That(reader.GetDecimal(1), Is.EqualTo(12.50m));
            Assert.That(reader.GetInt32(2), Is.EqualTo(7));
            Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetOrdinal("ignored"));
        });

        Assert.That(reader.Read(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetString(0), Is.EqualTo("Grace"));
            Assert.That(reader.GetDecimal(1), Is.EqualTo(25.75m));
            Assert.That(reader.GetInt32(2), Is.EqualTo(8));
        });
        Assert.That(reader.Read(), Is.False);
    }

    [Test]
    public void Projection_MissingSelectedProperty_Throws()
    {
        var ndjson = new NdjsonReaderBuilder()
            .WithDialect(dialect => dialect.WithLineTerminator("\n"))
            .WithProjection(projection => projection.Property("name").Property("count"))
            .Build();
        using var reader = ndjson.ToDataReader(
            new MemoryStream(Encoding.UTF8.GetBytes("{\"name\":\"Ada\",\"count\":7}\n{\"name\":\"Grace\"}")));

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(1), Is.EqualTo(7));

        var exception = Assert.Throws<InvalidDataException>(() => reader.Read());

        Assert.That(exception!.Message, Does.Contain("count"));
    }

    [Test]
    public void Projection_SkipsUnselectedValuesAcrossBufferBoundaries()
    {
        const string content =
            "{\"ignored\":{\"nested\":[true,null,-1.5e2]},\"name\":\"Ada\",\"tail\":\"escaped\\nvalue\"}";
        var ndjson = new NdjsonReaderBuilder()
            .WithProjection(projection => projection.Property("name"))
            .WithParserOptimizations(new ParserOptimizationOptions(BufferSize: 2, ReadAhead: false))
            .Build();
        using var reader = ndjson.ToDataReader(
            new MemoryStream(Encoding.UTF8.GetBytes(content)));

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("Ada"));
        Assert.That(reader.Read(), Is.False);
    }

    [Test]
    public void Projection_MalformedSkippedProperty_Throws()
    {
        var ndjson = new NdjsonReaderBuilder()
            .WithProjection(projection => projection.Property("name"))
            .Build();
        using var reader = ndjson.ToDataReader(
            new MemoryStream(Encoding.UTF8.GetBytes("{\"name\":\"Ada\",\"ignored\":01}")));

        Assert.Throws<InvalidDataException>(() => reader.Read());
    }

    [Test]
    public async Task Projection_ReadAsync_UsesProjectedOrdinals()
    {
        var ndjson = new NdjsonReaderBuilder()
            .WithProjection(projection => projection.Property("name").Property("count"))
            .Build();
        await using var stream = new AsyncOnlyStream(
            new MemoryStream(Encoding.UTF8.GetBytes("{\"ignored\":[1,2],\"count\":3,\"name\":\"Ada\"}")));
        await using var reader = ndjson.ToDataReader(stream);

        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetString(0), Is.EqualTo("Ada"));
            Assert.That(reader.GetInt32(1), Is.EqualTo(3));
        });
        Assert.That(await reader.ReadAsync(), Is.False);
    }

    [Test]
    public void ReaderBuilder_CommonConfiguration_PreservesFluentTypeAndBuffering()
    {
        var builder = new NdjsonReaderBuilder()
            .WithSchema(schema => schema.Named())
            .WithResource(resource => resource)
            .WithParsers(parsers => parsers)
            .WithParserOptimizations(new ParserOptimizationOptions(BufferSize: 8, ReadAhead: false))
            .WithDialect(dialect => dialect.WithLineTerminator("\n"));

        using var reader = builder.Build().ToDataReader(
            new MemoryStream(Encoding.UTF8.GetBytes("{\"value\":\"across-buffer\"}\n")));

        Assert.That(builder, Is.TypeOf<NdjsonReaderBuilder>());
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(reader.GetOrdinal("value")), Is.EqualTo("across-buffer"));
    }

    [Test]
    public void SharedMaterializers_ReturnExpectedValues()
    {
        const string content = "{\"id\":1,\"name\":\"Ada\"}\n{\"id\":2,\"name\":\"Grace\"}";
        var ndjson = new NdjsonReader(new NdjsonProfile("\n"));

        var arrays = ndjson.ToArrayString(new MemoryStream(Encoding.UTF8.GetBytes(content))).ToArray();
        var table = ndjson.ToDataTable(new MemoryStream(Encoding.UTF8.GetBytes(content)));

        Assert.Multiple(() =>
        {
            Assert.That(arrays, Is.EqualTo(new[]
            {
                new string?[] { "1", "Ada" },
                new string?[] { "2", "Grace" }
            }));
            Assert.That(table.Rows.Count, Is.EqualTo(2));
            Assert.That(table.Rows[1]["name"], Is.EqualTo("Grace"));
        });
    }

    [Test]
    public void DataReader_DoesNotAllocateUnusedSanitizerCache()
    {
        using var reader = new NdjsonReader(new NdjsonProfile("\n")).ToDataReader(
            new MemoryStream(Encoding.UTF8.GetBytes("{\"value\":1}")));
        var cache = typeof(BaseDataRecord<NdjsonProfile>)
            .GetField("_sanitizers", BindingFlags.Instance | BindingFlags.NonPublic)!;

        Assert.That(cache.GetValue(reader), Is.Null);
        Assert.That(reader.Read(), Is.True);
        _ = reader.GetString(0);
        Assert.That(cache.GetValue(reader), Is.Null);
    }

    private sealed class TrackingStream(Stream inner, int maxReadSize) : Stream
    {
        public long BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, Math.Min(count, maxReadSize));
            BytesRead += read;
            return read;
        }
        public override int Read(Span<byte> buffer)
        {
            var read = inner.Read(buffer[..Math.Min(buffer.Length, maxReadSize)]);
            BytesRead += read;
            return read;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => ReadTrackedAsync(buffer, cancellationToken);
        private async ValueTask<int> ReadTrackedAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            var read = await inner.ReadAsync(buffer[..Math.Min(buffer.Length, maxReadSize)], cancellationToken);
            BytesRead += read;
            return read;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class AsyncOnlyStream(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
            => throw new InvalidOperationException("Synchronous reads are not allowed.");
        public override int Read(Span<byte> buffer)
            => throw new InvalidOperationException("Synchronous reads are not allowed.");
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => inner.ReadAsync(buffer, cancellationToken);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }
        public override async ValueTask DisposeAsync()
        {
            await inner.DisposeAsync();
            GC.SuppressFinalize(this);
        }
    }
}
