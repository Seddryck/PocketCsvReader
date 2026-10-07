using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;
using PocketCsvReader.Json.Configuration;

namespace PocketCsvReader.Json.Testing;

public class JsonReaderTests
{
    [Test]
    public void RootObject_ProducesSingleLabeledRow()
    {
        using var reader = CreateReader("{\"id\":1,\"name\":\"Ada\",\"active\":true,\"missing\":null}");

        Assert.That(reader.Read(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.FieldCount, Is.EqualTo(4));
            Assert.That(reader.GetInt32(reader.GetOrdinal("id")), Is.EqualTo(1));
            Assert.That(reader.GetString(reader.GetOrdinal("name")), Is.EqualTo("Ada"));
            Assert.That(reader.GetBoolean(reader.GetOrdinal("active")), Is.True);
            Assert.That(reader.IsDBNull(reader.GetOrdinal("missing")), Is.True);
        });
        Assert.That(reader.Read(), Is.False);
    }

    [Test]
    public void TopLevelArray_ProducesOneRowPerElement()
    {
        const string content = """
            [
              { "id": 1, "name": "Ada" },
              { "name": "Grace", "enabled": false },
              42,
              [1, {"nested":true}],
              null
            ]
            """;
        using var reader = CreateReader(content);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(reader.GetOrdinal("id")), Is.EqualTo(1));
        Assert.That(reader.Read(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetString(reader.GetOrdinal("name")), Is.EqualTo("Grace"));
            Assert.That(reader.GetBoolean(reader.GetOrdinal("enabled")), Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() => reader.GetOrdinal("id"));
        });
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(42));
        Assert.That(reader.GetName(0), Is.Empty);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetRawString(0), Is.EqualTo("[1, {\"nested\":true}]") );
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.IsDBNull(0), Is.True);
        Assert.That(reader.Read(), Is.False);
    }

    [Test]
    public void TopLevelArray_ReusesLabelsForRepeatedRowShapes()
    {
        const string content = """
            [
              { "id": 1, "name": "Ada" },
              { "id": 2, "name": "Grace" },
              { "name": "Linus", "id": 3 },
              { "id": 4, "name": "Margaret" }
            ]
            """;
        using var reader = CreateReader(content);

        Assert.That(reader.Read(), Is.True);
        var id = reader.GetName(0);
        var name = reader.GetName(1);

        Assert.That(reader.Read(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetName(0), Is.SameAs(id));
            Assert.That(reader.GetName(1), Is.SameAs(name));
        });

        Assert.That(reader.Read(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetOrdinal("name"), Is.Zero);
            Assert.That(reader.GetOrdinal("id"), Is.EqualTo(1));
        });

        Assert.That(reader.Read(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetName(0), Is.SameAs(id));
            Assert.That(reader.GetName(1), Is.SameAs(name));
            Assert.That(reader.GetOrdinal("id"), Is.Zero);
            Assert.That(reader.GetOrdinal("name"), Is.EqualTo(1));
        });
    }

    [TestCase("[]", 0)]
    [TestCase("{}", 1)]
    [TestCase("[{}, []]", 2)]
    public void EmptyComposites_AreAccepted(string content, int expectedRows)
    {
        using var reader = CreateReader(content);

        var rows = 0;
        while (reader.Read())
            rows++;

        Assert.That(rows, Is.EqualTo(expectedRows));
    }

    [Test]
    public void NestedValuesAndEscapes_PreserveSpanSemantics()
    {
        const string content = "{\"na\\u006De\":\"pair: \\uD83D\\uDE00\",\"items\":[null,\"line\\nfeed\",-12.34e+2,{\"ok\":true},[]]}";
        using var reader = CreateReader(content);

        Assert.That(reader.Read(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetString(reader.GetOrdinal("name")), Is.EqualTo("pair: 😀"));
            Assert.That(reader.GetArray(reader.GetOrdinal("items")), Is.EqualTo(new object?[]
            {
                null,
                "line\nfeed",
                "-12.34e+2",
                "{\"ok\":true}",
                "[]"
            }));
        });
    }

    [Test]
    public void Schema_BindsObjectPropertiesByName()
    {
        var schema = new SchemaDescriptorBuilder().Named()
            .WithIntegerField<int>("id")
            .WithField<string>("name")
            .Build();
        var profile = new JsonProfile(schema: schema);
        using var reader = CreateReader("[{\"name\":\"Ada\",\"id\":7}]", profile);

        Assert.That(reader.Read(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetFieldValue<int>("id"), Is.EqualTo(7));
            Assert.That(reader.GetFieldValue<string>("name"), Is.EqualTo("Ada"));
        });
    }

    [Test]
    public void Projection_ExposesOnlySelectedPropertiesInProjectionOrder()
    {
        const string content = """
            [
              { "ignored": { "nested": [1, true, "value"] }, "count": 7, "na\u006de": "Ada", "amount": 12.50 },
              { "amount": 25.75, "other": [1, { "deep": false }], "name": "Grace", "count": 8 }
            ]
            """;
        var json = new JsonReaderBuilder()
            .WithProjection(projection => projection
                .Property("name")
                .Property("amount")
                .Property("count"))
            .Build();
        using var reader = json.ToDataReader(StreamFor(content));

        Assert.That(reader.Read(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.FieldCount, Is.EqualTo(3));
            Assert.That(reader.GetName(0), Is.EqualTo("name"));
            Assert.That(reader.GetName(1), Is.EqualTo("amount"));
            Assert.That(reader.GetName(2), Is.EqualTo("count"));
            Assert.That(reader.GetString(reader.GetOrdinal("name")), Is.EqualTo("Ada"));
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
        var json = new JsonReaderBuilder()
            .WithProjection(projection => projection.Property("name").Property("count"))
            .Build();
        using var reader = json.ToDataReader(StreamFor("[{\"name\":\"Ada\",\"count\":7},{\"name\":\"Grace\"}]"));

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(1), Is.EqualTo(7));

        var exception = Assert.Throws<InvalidDataException>(() => reader.Read());

        Assert.That(exception!.Message, Does.Contain("count"));
    }

    [Test]
    public void Projection_MalformedSkippedProperty_Throws()
    {
        var json = new JsonReaderBuilder()
            .WithProjection(projection => projection.Property("name"))
            .Build();
        using var reader = json.ToDataReader(StreamFor("[{\"name\":\"Ada\",\"ignored\":01}]"));

        Assert.Throws<InvalidDataException>(() => reader.Read());
    }

    [Test]
    public void Projection_CrossBufferScalarFields_AreParsedDuringFraming()
    {
        const string content = "[{\"ignored\":{\"nested\":[true,null,-1.5e2]},\"na\\u006de\":\"Ada\",\"amount\":12.50,\"count\":7}]";
        var json = new JsonReaderBuilder()
            .WithProjection(projection => projection
                .Property("name")
                .Property("amount")
                .Property("count"))
            .WithParserOptimizations(new ParserOptimizationOptions(BufferSize: 2, ReadAhead: false))
            .Build();
        using var reader = json.ToDataReader(StreamFor(content));

        Assert.That(reader.Read(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetString(0), Is.EqualTo("Ada"));
            Assert.That(reader.GetDecimal(1), Is.EqualTo(12.50m));
            Assert.That(reader.GetInt32(2), Is.EqualTo(7));
        });
        Assert.That(reader.Read(), Is.False);
    }

    [Test]
    public void Projection_SkippedScalarFastPaths_CrossBufferBoundaries()
    {
        const string content = """
            [{"ignoredString":"escaped\nvalue","ignoredTrue":true,"ignoredFalse":false,
              "ignoredNull":null,"ignoredInteger":-12345,"ignoredDecimal":12.5e+2,"name":"Ada"}]
            """;
        var json = new JsonReaderBuilder()
            .WithProjection(projection => projection.Property("name"))
            .WithParserOptimizations(new ParserOptimizationOptions(BufferSize: 1, ReadAhead: false))
            .Build();
        using var reader = json.ToDataReader(StreamFor(content));

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("Ada"));
        Assert.That(reader.Read(), Is.False);
    }

    [Test]
    public void Projection_SelectedLiteralFastPaths_CrossBufferBoundaries()
    {
        const string content = "[{\"enabled\":true,\"missing\":null,\"disabled\":false}]";
        var json = new JsonReaderBuilder()
            .WithProjection(projection => projection
                .Property("enabled")
                .Property("missing")
                .Property("disabled"))
            .WithParserOptimizations(new ParserOptimizationOptions(BufferSize: 1, ReadAhead: false))
            .Build();
        using var reader = json.ToDataReader(StreamFor(content));

        Assert.That(reader.Read(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetBoolean(0), Is.True);
            Assert.That(reader.IsDBNull(1), Is.True);
            Assert.That(reader.GetBoolean(2), Is.False);
        });
        Assert.That(reader.Read(), Is.False);
    }

    [Test]
    public void Projection_EscapedAndCompositeSelectedValues_UseMaterializationFallback()
    {
        const string content = "[{\"text\":\"line\\nfeed\",\"items\":[null,2,\"three\"]}]";
        var json = new JsonReaderBuilder()
            .WithProjection(projection => projection.Property("text").Property("items"))
            .Build();
        using var reader = json.ToDataReader(StreamFor(content));

        Assert.That(reader.Read(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetString(0), Is.EqualTo("line\nfeed"));
            Assert.That(reader.GetArray(1), Is.EqualTo(new object?[] { null, "2", "three" }));
        });
    }

    [TestCase("[{\"name\":\"Ada\",\"ignored\":[1,]}]")]
    [TestCase("[{\"name\":\"Ada\",\"ignored\":\"bad\\x\"}]")]
    [TestCase("[{\"name\":\"Ada\",\"ignored\":truex}]")]
    [TestCase("[{\"name\":\"Ada\",\"ignored\":{\"nested\" 1}}]")]
    public void Projection_MalformedSkippedStructures_Throw(string content)
    {
        var json = new JsonReaderBuilder()
            .WithProjection(projection => projection.Property("name"))
            .Build();
        using var reader = json.ToDataReader(StreamFor(content));

        Assert.Throws<InvalidDataException>(() => reader.Read());
    }

    [TestCase("[{\"ignored\":\"bad\nx\",\"name\":\"Ada\"}]")]
    [TestCase("[{\"ignored\":truex,\"name\":\"Ada\"}]")]
    [TestCase("[{\"ignored\":1.,\"name\":\"Ada\"}]")]
    [TestCase("[{\"ignored\":1e+,\"name\":\"Ada\"}]")]
    [TestCase("[{\"ignored\":-,\"name\":\"Ada\"}]")]
    [TestCase("[{\"ignored\":00,\"name\":\"Ada\"}]")]
    public void Projection_MalformedSkippedScalarsAcrossBuffers_Throw(string content)
    {
        var json = new JsonReaderBuilder()
            .WithProjection(projection => projection.Property("name"))
            .WithParserOptimizations(new ParserOptimizationOptions(BufferSize: 1, ReadAhead: false))
            .Build();
        using var reader = json.ToDataReader(StreamFor(content));

        Assert.Throws<InvalidDataException>(() => reader.Read());
    }

    [Test]
    public async Task Projection_ReadAsync_UsesProjectedOrdinals()
    {
        var json = new JsonReaderBuilder()
            .WithProjection(projection => projection.Property("name").Property("count"))
            .Build();
        await using var stream = new AsyncOnlyStream(StreamFor("[{\"ignored\":[1,2],\"count\":3,\"name\":\"Ada\"}]"));
        await using var reader = json.ToDataReader(stream);

        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetString(0), Is.EqualTo("Ada"));
            Assert.That(reader.GetInt32(1), Is.EqualTo(3));
        });
        Assert.That(await reader.ReadAsync(), Is.False);
    }

    [Test]
    public void SharedMaterializers_ReturnArraysAndDataTable()
    {
        const string content = "[{\"id\":1,\"name\":\"Ada\"},{\"id\":2,\"name\":\"Grace\"}]";
        var json = new JsonReader();

        var arrays = json.ToArrayString(StreamFor(content)).ToArray();
        var table = json.ToDataTable(StreamFor(content));

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
    public void ReaderBuilder_AppliesSharedConfigurationAndSmallBuffer()
    {
        var json = new JsonReaderBuilder()
            .WithSchema(schema => schema.Named())
            .WithParserOptimizations(new ParserOptimizationOptions(BufferSize: 1, ReadAhead: false))
            .Build();
        using var reader = json.ToDataReader(StreamFor("[ { \"value\" : \"across-buffer\" } ]"));

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(reader.GetOrdinal("value")), Is.EqualTo("across-buffer"));
    }

    [Test]
    public void RootValue_RemainsValidWhenTrailingWhitespaceRefillsBuffer()
    {
        var profile = new JsonProfile(
            parserOptimizations: new ParserOptimizationOptions(BufferSize: 32, ReadAhead: false));
        using var reader = CreateReader("{\"value\":42}" + new string(' ', 96), profile);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(reader.GetOrdinal("value")), Is.EqualTo(42));
        Assert.That(reader.Read(), Is.False);
    }

    [Test]
    public void NonSeekableStream_IsSupported()
    {
        using var stream = new NonSeekableStream(StreamFor("[{\"id\":1},{\"id\":2}]"));
        using var reader = new JsonReader().ToDataReader(stream);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(1));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(2));
        Assert.That(reader.Read(), Is.False);
    }

    [Test]
    public void LargeArray_DoesNotReadTheCompleteDocumentForFirstRow()
    {
        var content = "[0," + string.Join(',', Enumerable.Range(1, 20_000)) + "]";
        using var stream = new TrackingStream(StreamFor(content), maxReadSize: 64);
        using var reader = new JsonReader(bufferSize: 8).ToDataReader(stream);

        Assert.That(reader.Read(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetInt32(0), Is.Zero);
            Assert.That(stream.BytesRead, Is.LessThan(content.Length));
        });
    }

    [TestCase("")]
    [TestCase("[1,]")]
    [TestCase("[1 2]")]
    [TestCase("[1")]
    [TestCase("{\"value\":1} trailing")]
    [TestCase("// comment\n{\"value\":1}")]
    [TestCase("[1] # comment")]
    [TestCase("[\"unterminated]")]
    [TestCase("[01]")]
    public void MalformedDocument_ThrowsWithPosition(string content)
    {
        using var reader = CreateReader(content);

        var exception = Assert.Throws<InvalidDataException>(() =>
        {
            while (reader.Read()) { }
        });
        Assert.That(exception!.Message, Does.Contain("position"));
    }

    [Test]
    public async Task ReadAsync_PrettyPrintedArray_ReturnsRows()
    {
        await using var reader = CreateReader("[\n {\"id\":1},\n {\"id\":2}\n]");

        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(1));
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(2));
        Assert.That(await reader.ReadAsync(), Is.False);
    }

    [Test]
    public async Task ReadAsync_UsesAsynchronousStreamOperations()
    {
        await using var stream = new AsyncOnlyStream(StreamFor("[{\"id\":1},{\"id\":2}]"));
        await using var reader = new JsonReader().ToDataReader(stream);

        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(1));
        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(2));
        Assert.That(await reader.ReadAsync(), Is.False);
    }

    [Test]
    public async Task ReadAsync_ValueCrossingBuffers_RemainsValid()
    {
        var profile = new JsonProfile(
            parserOptimizations: new ParserOptimizationOptions(BufferSize: 3, ReadAhead: false));
        await using var reader = CreateReader("[{\"message\":\"cross-buffer\"}]", profile);

        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetString(reader.GetOrdinal("message")), Is.EqualTo("cross-buffer"));
        Assert.That(await reader.ReadAsync(), Is.False);
    }

    private static JsonDataReader CreateReader(string content, JsonProfile? profile = null)
        => new JsonReader(profile ?? JsonProfile.Default).ToDataReader(StreamFor(content));

    private static MemoryStream StreamFor(string content)
        => new(Encoding.UTF8.GetBytes(content));

    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => inner.Read(buffer);
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
