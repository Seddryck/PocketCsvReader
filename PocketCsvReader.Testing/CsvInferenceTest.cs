using System.Globalization;
using System.IO.Compression;
using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing;

public class CsvInferenceTest
{
    [Test]
    public void Infer_ShouldDetectHeaderTypesNullabilityAndLeadingZeroIdentifiers()
    {
        var guid1 = Guid.NewGuid();
        var guid2 = Guid.NewGuid();
        var csv = $"Name,Age,Active,Amount,Date,Id,Postal,Optional\nAlice,12,true,12.5,2026-01-02,{guid1},00123,\nBob,40000,false,17.25,2026-02-03,{guid2},00999,";
        using var stream = Utf8(csv);

        var result = CsvInference.Infer(stream, new CsvInferenceOptions
        {
            Dialect = NoHeader(LineTerminator.LineFeed)
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Dialect.Header, Is.True);
            Assert.That(result.SampledRecordCount, Is.EqualTo(3));
            Assert.That(result.Columns.Select(column => column.Name), Is.EqualTo(new[] { "Name", "Age", "Active", "Amount", "Date", "Id", "Postal", "Optional" }));
            Assert.That(result.Columns.Select(column => column.RuntimeType), Is.EqualTo(new[]
            {
                typeof(string), typeof(int), typeof(bool), typeof(decimal), typeof(DateOnly), typeof(Guid), typeof(string), typeof(string)
            }));
            Assert.That(result.Columns[7].IsNullable, Is.True);
            Assert.That(result.Columns[7].EmptyCount, Is.EqualTo(2));
            Assert.That(result.Columns[7].NullCount, Is.Zero);
            Assert.That(result.Schema.Fields[7].IsNullable, Is.True);
            Assert.That(result.Diagnostics.Any(diagnostic => diagnostic.Code == "column.leading-zero"), Is.True);
            Assert.That(stream.Position, Is.Zero);
        });
    }

    [Test]
    public void Infer_WithHeaderAbsent_ShouldIncludeFirstRecordAndPromoteIntegers()
    {
        using var stream = Utf8("1,10\n40000,20\n3000000000,30");

        var result = CsvInference.Infer(stream, new CsvInferenceOptions
        {
            Header = CsvHeaderMode.Absent,
            Dialect = NoHeader(LineTerminator.LineFeed)
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Dialect.Header, Is.False);
            Assert.That(result.Columns[0].RuntimeType, Is.EqualTo(typeof(long)));
            Assert.That(result.Columns[1].RuntimeType, Is.EqualTo(typeof(short)));
            Assert.That(result.Columns.Select(column => column.Name), Is.EqualTo(new[] { "Column1", "Column2" }));
        });
    }

    [Test]
    public void InferDialect_ShouldDetectSemicolonLfAndMultilineQuotedFields()
    {
        using var stream = Utf8("Name;Notes;Value\nAlice;\"first\nsecond\";1,5\nBob;plain;2,5");

        var result = CsvInference.Infer(stream, new CsvInferenceOptions
        {
            InferDialect = true,
            Culture = CultureInfo.GetCultureInfo("fr-FR")
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Dialect.Delimiter, Is.EqualTo(';'));
            Assert.That(result.Dialect.LineTerminator, Is.EqualTo("\n"));
            Assert.That(result.Dialect.QuoteChar, Is.EqualTo('"'));
            Assert.That(result.Dialect.Header, Is.True);
            Assert.That(result.Columns[2].RuntimeType, Is.EqualTo(typeof(decimal)));
        });
    }

    [Test]
    public void Infer_ShouldReportMissingAndInconsistentFields()
    {
        using var stream = Utf8("A,B,C\n1,2,3\n4,5\n6,7,8,9");

        var result = CsvInference.Infer(stream, new CsvInferenceOptions
        {
            Header = CsvHeaderMode.Present,
            Dialect = NoHeader(LineTerminator.LineFeed)
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Columns, Has.Count.EqualTo(4));
            Assert.That(result.Columns[2].MissingCount, Is.EqualTo(1));
            Assert.That(result.Columns[3].MissingCount, Is.EqualTo(2));
            Assert.That(result.Diagnostics.Any(diagnostic => diagnostic.Code == "records.inconsistent-width"), Is.True);
        });
    }

    [Test]
    public void Infer_ShouldDistinguishConfiguredNullsFromEmptyCells()
    {
        using var stream = Utf8("A,B\n1,NULL\n2,");
        var result = CsvInference.Infer(stream, new CsvInferenceOptions
        {
            Header = CsvHeaderMode.Present,
            Dialect = NoHeader(LineTerminator.LineFeed) with { NullSequence = "NULL" }
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.Columns[1].NullCount, Is.EqualTo(1));
            Assert.That(result.Columns[1].EmptyCount, Is.EqualTo(1));
            Assert.That(result.Columns[1].IsNullable, Is.True);
        });
    }

    [Test]
    public void Infer_ShouldRespectByteLimit()
    {
        const string sampled = "A,B\n1,2\n";
        using var stream = Utf8(sampled + "3,4\n5,6");
        var result = CsvInference.Infer(stream, new CsvInferenceOptions
        {
            MaximumBytes = Encoding.UTF8.GetByteCount(sampled),
            Header = CsvHeaderMode.Present,
            Dialect = NoHeader(LineTerminator.LineFeed)
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.SampledRecordCount, Is.EqualTo(2));
            Assert.That(result.Diagnostics.Any(diagnostic => diagnostic.Code == "sample.byte-limit"), Is.True);
            Assert.That(stream.Position, Is.Zero);
        });
    }

    [Test]
    public void Infer_ShouldReportAmbiguousAllStringHeader()
    {
        using var stream = Utf8("Name,Place\nAlice,Brussels\nBob,Paris");
        var result = CsvInference.Infer(stream, new CsvInferenceOptions
        {
            Dialect = NoHeader(LineTerminator.LineFeed)
        });

        Assert.That(result.Diagnostics.Any(diagnostic => diagnostic.Code == "header.ambiguous"), Is.True);
    }

    [Test]
    public void Infer_ShouldRespectRecordLimitAndRestoreOriginalPosition()
    {
        using var stream = Utf8("ignored\nA,B\n1,2\n3,4\n5,6");
        stream.Position = "ignored\n"u8.Length;

        var result = CsvInference.Infer(stream, new CsvInferenceOptions
        {
            MaximumRecords = 2,
            Header = CsvHeaderMode.Present,
            Dialect = NoHeader(LineTerminator.LineFeed)
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.SampledRecordCount, Is.EqualTo(2));
            Assert.That(result.Columns.Select(column => column.Name), Is.EqualTo(new[] { "A", "B" }));
            Assert.That(stream.Position, Is.EqualTo("ignored\n"u8.Length));
        });
    }

    [Test]
    public void Infer_ShouldConsumeNonSeekableStreamExplicitly()
    {
        using var stream = new NonSeekableStream(Utf8("A,B\n1,2"));

        var result = CsvInference.Infer(stream, new CsvInferenceOptions
        {
            Header = CsvHeaderMode.Present,
            Dialect = NoHeader(LineTerminator.LineFeed)
        });

        Assert.That(result.Columns.Select(column => column.RuntimeType), Is.EqualTo(new[] { typeof(short), typeof(short) }));
        Assert.That(stream.ReadByte(), Is.EqualTo(-1));
    }

    [Test]
    public void Infer_ShouldUseNormalPipelineForCommentsBufferCrossingsBomAndCompression()
    {
        var longValue = new string('x', 5000);
        var content = $"#comment\nName,Text\nAlice,{longValue}";
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionMode.Compress, leaveOpen: true))
        {
            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetBytes(content);
            gzip.Write(bytes);
        }
        compressed.Position = 0;

        var result = CsvInference.Infer(compressed, new CsvInferenceOptions
        {
            Header = CsvHeaderMode.Present,
            Dialect = NoHeader(LineTerminator.LineFeed) with { CommentChar = '#' },
            Resource = new ResourceDescriptor(Compression: "gz")
        });

        Assert.Multiple(() =>
        {
            Assert.That(result.SampledRecordCount, Is.EqualTo(2));
            Assert.That(result.Columns.Select(column => column.Name), Is.EqualTo(new[] { "Name", "Text" }));
            Assert.That(result.Columns.All(column => column.RuntimeType == typeof(string)), Is.True);
        });
    }

    [Test]
    public void InferredDescriptors_ShouldBeAcceptedByBuilderAndParseTypedValues()
    {
        const string csv = "Name,Age\nAlice,42";
        using var inferenceStream = Utf8(csv);
        var result = CsvInference.Infer(inferenceStream, new CsvInferenceOptions
        {
            Header = CsvHeaderMode.Present,
            Dialect = NoHeader(LineTerminator.LineFeed)
        });
        var reader = new CsvReaderBuilder()
            .WithDialect(result.Dialect)
            .WithSchema(result.Schema)
            .Build();
        using var dataStream = Utf8(csv);
        using var dataReader = reader.ToDataReader(dataStream);

        Assert.That(dataReader.Read(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(dataReader.GetString(0), Is.EqualTo("Alice"));
            Assert.That(dataReader.GetInt16(1), Is.EqualTo(42));
            Assert.That(dataReader.GetFieldType(1), Is.EqualTo(typeof(short)));
        });
    }

    private static DialectDescriptor NoHeader(LineTerminator terminator) => new DialectDescriptorBuilder()
        .WithLineTerminator(terminator)
        .WithoutHeader()
        .Build();

    private static MemoryStream Utf8(string value) => new(Encoding.UTF8.GetBytes(value));

    private sealed class NonSeekableStream(Stream inner) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => inner.Read(buffer);
        public override int ReadByte() => inner.ReadByte();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
