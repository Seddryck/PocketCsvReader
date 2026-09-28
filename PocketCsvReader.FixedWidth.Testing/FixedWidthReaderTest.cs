using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;
using PocketCsvReader.FixedWidth.Configuration;

namespace PocketCsvReader.FixedWidth.Testing;

public class FixedWidthReaderTest
{
    [Test]
    public void Builder_CommonConfiguration_PreservesFixedWidthFluentType()
    {
        var builder = new FixedWidthReaderBuilder()
            .WithSchema(schema => schema.Indexed())
            .WithResource(resource => resource)
            .WithParsers(parsers => parsers)
            .WithParserOptimizations(new ParserOptimizationOptions(BufferSize: 32, RowCountAtStart: true))
            .WithField("Value", 0, 1);

        var reader = builder.Build();

        Assert.Multiple(() =>
        {
            Assert.That(builder, Is.TypeOf<FixedWidthReaderBuilder>());
            Assert.That(reader, Is.TypeOf<FixedWidthReader>());
        });
    }

    [Test]
    public void DataReader_FieldsWithGapAndPadding_ReturnsRawAndParsedValues()
    {
        using var stream = Text("00042|Ada       1815-12-10\n");
        using var reader = new FixedWidthReaderBuilder()
            .WithLineTerminator("\n")
            .WithField("Id", 0, 5, FixedWidthPadding.Left, '0')
            .WithField("Name", 6, 10)
            .WithField("BirthDate", 16, 10, FixedWidthPadding.None)
            .WithSchema(schema => schema.Indexed()
                .WithField<int>()
                .WithField<string>()
                .WithTemporalField<DateOnly>(field => field.WithFormat("yyyy-MM-dd")))
            .Build()
            .ToDataReader(stream);

        Assert.That(reader.Read(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.FieldCount, Is.EqualTo(3));
            Assert.That(reader.GetRawString(0), Is.EqualTo("00042"));
            Assert.That(reader.GetInt32(0), Is.EqualTo(42));
            Assert.That(reader.GetString(1), Is.EqualTo("Ada"));
            Assert.That(reader.GetFieldValue<DateOnly>(2), Is.EqualTo(new DateOnly(1815, 12, 10)));
            Assert.That(reader.Read(), Is.False);
        });
    }

    [Test]
    public void DataReader_CustomParsers_AreInvokedOnlyForRequestedField()
    {
        var firstCalls = 0;
        var secondCalls = 0;
        using var stream = Text("123BAD\n");
        using var reader = new FixedWidthReaderBuilder()
            .WithLineTerminator("\n")
            .WithField("First", 0, 3, FixedWidthPadding.None)
            .WithField("Second", 3, 3, FixedWidthPadding.None)
            .WithSchema(schema => schema.Indexed()
                .WithField<int>(field => field.WithParser(value =>
                {
                    firstCalls++;
                    return int.Parse(value);
                }))
                .WithField<int>(field => field.WithParser(value =>
                {
                    secondCalls++;
                    return int.Parse(value);
                })))
            .Build()
            .ToDataReader(stream);

        Assert.That(reader.Read(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.FieldCount, Is.EqualTo(2));
            Assert.That(reader.GetName(0), Is.EqualTo("First"));
            Assert.That(reader.GetOrdinal("Second"), Is.EqualTo(1));
            Assert.That(reader.GetRawString(1), Is.EqualTo("BAD"));
            Assert.That(firstCalls, Is.Zero);
            Assert.That(secondCalls, Is.Zero);
        });

        Assert.That(reader.GetValue(0), Is.EqualTo(123));
        Assert.Multiple(() =>
        {
            Assert.That(firstCalls, Is.EqualTo(1));
            Assert.That(secondCalls, Is.Zero);
        });

        Assert.That(() => reader.GetValue(1), Throws.TypeOf<FormatException>());
        Assert.That(secondCalls, Is.EqualTo(1));
    }

    [Test]
    public void DataReader_NonSeekableStream_IsConsumedOnceWithoutSeeking()
    {
        var bytes = Encoding.UTF8.GetBytes("01Ada  \n02Grace\n");
        using var stream = new NonSeekableReadStream(bytes);
        using var reader = new FixedWidthReaderBuilder()
            .WithLineTerminator("\n")
            .WithField("Id", 0, 2, FixedWidthPadding.None)
            .WithField("Name", 2, 5)
            .Build()
            .ToDataReader(stream);

        var names = new List<string>();
        while (reader.Read())
            names.Add(reader.GetString(1));

        Assert.Multiple(() =>
        {
            Assert.That(names, Is.EqualTo(new[] { "Ada", "Grace" }));
            Assert.That(stream.SeekCalls, Is.Zero);
            Assert.That(stream.BytesRead, Is.EqualTo(bytes.Length));
        });
    }

    [Test]
    public void DataReader_SeekableStream_IsNotProbedOrRewound()
    {
        var bytes = Encoding.UTF8.GetBytes("01Ada  \n02Grace\n");
        using var stream = new TrackingSeekableStream(bytes);
        using var reader = new FixedWidthReaderBuilder()
            .WithLineTerminator("\n")
            .WithField("Id", 0, 2, FixedWidthPadding.None)
            .WithField("Name", 2, 5)
            .Build()
            .ToDataReader(stream);

        while (reader.Read())
            _ = reader.GetString(0);

        Assert.Multiple(() =>
        {
            Assert.That(stream.BackwardSeeks, Is.Zero);
            Assert.That(stream.BytesRead, Is.EqualTo(bytes.Length));
        });
    }

    [Test]
    public void DataReader_HeaderAndNonAsciiText_UsesHeaderNames()
    {
        using var stream = Text("CodeName      \r\n0001Élodie    \r\n");
        using var reader = new FixedWidthReaderBuilder()
            .WithLineTerminator("\r\n")
            .WithHeader()
            .WithField("Field1", 0, 4)
            .WithField("Field2", 4, 10)
            .Build()
            .ToDataReader(stream);

        Assert.That(reader.Read(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetName(0), Is.EqualTo("Code"));
            Assert.That(reader.GetName(1), Is.EqualTo("Name"));
            Assert.That(reader.GetString(1), Is.EqualTo("Élodie"));
        });
    }

    [TestCase("1234\n", "too short", 6, 4)]
    [TestCase("1234567\n", "too long", 6, 7)]
    public void DataReader_InvalidRecordLength_ThrowsDiagnostic(string content, string reason, int expected, int actual)
    {
        using var stream = Text(content);
        using var reader = CreateTwoFieldReader().ToDataReader(stream);

        var exception = Assert.Throws<InvalidDataException>(() => reader.Read());
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("record 1"));
            Assert.That(exception.Message, Does.Contain(reason));
            Assert.That(exception.Message, Does.Contain($"expected {expected}"));
            Assert.That(exception.Message, Does.Contain($"found {actual}"));
        });
    }

    [Test]
    public void DataReader_AllowTrailingCharacters_IgnoresTail()
    {
        using var stream = Text("123456tail\n");
        using var reader = new FixedWidthReaderBuilder()
            .WithLineTerminator("\n")
            .AllowTrailingCharacters()
            .WithField("First", 0, 3, FixedWidthPadding.None)
            .WithField("Second", 3, 3, FixedWidthPadding.None)
            .Build()
            .ToDataReader(stream);

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(1), Is.EqualTo("456"));
    }

    [Test]
    public void OutputModels_ReturnExpectedValues()
    {
        var fixedWidth = new FixedWidthReaderBuilder()
            .WithLineTerminator("\n")
            .WithField("Id", 0, 2, FixedWidthPadding.Left, '0')
            .WithField("Name", 2, 5)
            .Build();

        var arrays = fixedWidth.ToArrayString(Text("01Ada  \n02Grace")).ToArray();
        var table = fixedWidth.ToDataTable(Text("01Ada  \n02Grace"));
        var objects = fixedWidth.To<Person>(Text("01Ada  \n02Grace")).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(arrays, Is.EqualTo(new[] { new string?[] { "1", "Ada" }, new string?[] { "2", "Grace" } }));
            Assert.That(table.Rows.Count, Is.EqualTo(2));
            Assert.That(table.Rows[1]["Name"], Is.EqualTo("Grace"));
            Assert.That(objects, Is.EqualTo(new[] { new Person(1, "Ada"), new Person(2, "Grace") }));
        });
    }

    [Test]
    public void DataReader_NullSequence_IsAppliedOnAccess()
    {
        using var reader = new FixedWidthReaderBuilder()
            .WithLineTerminator("\n")
            .WithField("Value", 0, 4)
            .WithResource(resource => resource.WithSequence("NULL", null))
            .Build()
            .ToDataReader(Text("NULL\n"));

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.IsDBNull(0), Is.True);
    }

    [Test]
    public void DataReader_RecordLargerThanBuffer_IsReadAcrossBufferBoundaries()
    {
        var value = new string('x', 5_000);
        using var reader = new FixedWidthReaderBuilder()
            .WithLineTerminator("\r\n")
            .WithField("Value", 0, value.Length, FixedWidthPadding.None)
            .WithParserOptimizations(new ParserOptimizationOptions(BufferSize: 32))
            .Build()
            .ToDataReader(Text($"{value}\r\n"));

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo(value));
    }

    [Test]
    public void Profile_InvalidLayouts_AreRejected()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => new FixedWidthReaderBuilder().Build(), Throws.ArgumentException);
            Assert.That(() => new FixedWidthReaderBuilder().WithField("A", -1, 1).Build(), Throws.ArgumentException);
            Assert.That(() => new FixedWidthReaderBuilder().WithField("A", 0, 0).Build(), Throws.ArgumentException);
            Assert.That(() => new FixedWidthReaderBuilder().WithField("A", 0, 3).WithField("B", 2, 2).Build(), Throws.ArgumentException);
            Assert.That(() => new FixedWidthReaderBuilder().WithField("A", 0, 1).WithField("A", 2, 1).Build(), Throws.ArgumentException);
            Assert.That(() => new FixedWidthReaderBuilder().WithLineTerminator("").WithField("A", 0, 1).Build(), Throws.ArgumentException);
        });
    }

    private static FixedWidthReader CreateTwoFieldReader()
        => new FixedWidthReaderBuilder()
            .WithLineTerminator("\n")
            .WithField("First", 0, 3, FixedWidthPadding.None)
            .WithField("Second", 3, 3, FixedWidthPadding.None)
            .Build();

    private static MemoryStream Text(string value)
        => new(Encoding.UTF8.GetBytes(value));

    public sealed record Person(int Id, string Name);

    private sealed class NonSeekableReadStream : Stream
    {
        private readonly MemoryStream _inner;
        public int SeekCalls { get; private set; }
        public long BytesRead { get; private set; }

        public NonSeekableReadStream(byte[] bytes)
            => _inner = new MemoryStream(bytes);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _inner.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            var read = _inner.Read(buffer);
            BytesRead += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            SeekCalls++;
            throw new NotSupportedException();
        }

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _inner.Dispose();
            base.Dispose(disposing);
        }
    }

    private sealed class TrackingSeekableStream : Stream
    {
        private readonly MemoryStream _inner;
        public int BackwardSeeks { get; private set; }
        public long BytesRead { get; private set; }

        public TrackingSeekableStream(byte[] bytes)
            => _inner = new MemoryStream(bytes);

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position
        {
            get => _inner.Position;
            set
            {
                if (value < _inner.Position)
                    BackwardSeeks++;
                _inner.Position = value;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _inner.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            var read = _inner.Read(buffer);
            BytesRead += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            var previous = _inner.Position;
            var position = _inner.Seek(offset, origin);
            if (position < previous)
                BackwardSeeks++;
            return position;
        }

        public override void Flush() { }
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
