using System.Globalization;
using System.Text;
using NUnit.Framework;

namespace PocketCsvReader.WebLogs.Testing;

public class CombinedLogTests
{
    private const string ValidLine = "127.0.0.1 - frank [10/Oct/2000:13:55:36 -0700] \"GET /apache_pb.gif HTTP/1.0\" 200 4294967296 \"https://example.com/start\" \"Example Browser\"\n";

    [TestCase("en-US", "-0700", -420)]
    [TestCase("fr-FR", "+0530", 330)]
    [TestCase("ar-SA", "+0000", 0)]
    public void ToCombinedLog_ValidRecord_ExposesTypedValuesRegardlessOfCulture(string culture, string offset, int minutes)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var entry = new CombinedLogReader().ToCombinedLog(Text(ValidLine.Replace("-0700", offset))).Single();

            Assert.That(entry, Is.EqualTo(new CombinedLog(
                "127.0.0.1", null, "frank",
                new DateTimeOffset(2000, 10, 10, 13, 55, 36, TimeSpan.FromMinutes(minutes)),
                "GET /apache_pb.gif HTTP/1.0", 200, 4294967296L, "https://example.com/start", "Example Browser")));
            Assert.That(entry.Timestamp!.Value.Offset, Is.EqualTo(TimeSpan.FromMinutes(minutes)));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Test]
    public void ToCombinedLog_MissingFields_ReturnsNulls()
    {
        var entry = new CombinedLogReader().ToCombinedLog(Text("- - - [-] \"-\" - - \"-\" \"-\"\n")).Single();

        Assert.That(entry, Is.EqualTo(new CombinedLog(null, null, null, null, null, null, null, null, null)));
    }

    [Test]
    public void ExistingOutputs_KeepTimestampTextAndQuotedHyphen()
    {
        var common = new CombinedLogReader();
        var line = ValidLine.Replace("GET /apache_pb.gif HTTP/1.0", "-");

        using var reader = common.ToDataReader(Text(line));
        Assert.That(reader.Read(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetString(3), Is.EqualTo("10/Oct/2000:13:55:36 -0700"));
            Assert.That(reader.GetString(4), Is.EqualTo("-"));
            Assert.That(common.ToArrayString(Text(line)).Single()[4], Is.EqualTo("-"));
            Assert.That(common.ToDataTable(Text(line)).Rows[0]["Request"], Is.EqualTo("-"));
            Assert.That(common.To<string?>(Text(line), row => row.GetString(4)).Single(), Is.EqualTo("-"));
        });
    }

    [TestCase("invalid", "200", "42", typeof(FormatException))]
    [TestCase("31/Feb/2000:13:55:36 -0700", "200", "42", typeof(FormatException))]
    [TestCase("10/Oct/2000:13:55:36 -0700", "bad", "42", typeof(FormatException))]
    [TestCase("10/Oct/2000:13:55:36 -0700", "2147483648", "42", typeof(OverflowException))]
    [TestCase("10/Oct/2000:13:55:36 -0700", "200", "bad", typeof(FormatException))]
    [TestCase("10/Oct/2000:13:55:36 -0700", "200", "9223372036854775808", typeof(OverflowException))]
    public void ToCombinedLog_InvalidTypedValue_ThrowsAndDisposesStream(string timestamp, string status, string bytes, Type exceptionType)
    {
        var stream = Text($"127.0.0.1 - frank [{timestamp}] \"GET / HTTP/1.0\" {status} {bytes} \"-\" \"-\"\n");
        var entries = new CombinedLogReader().ToCombinedLog(stream);

        Assert.That(stream.Position, Is.Zero);
        Assert.Throws(exceptionType, () => entries.ToList());
        Assert.That(stream.CanRead, Is.False);
    }

    [Test]
    public void ToCombinedLog_FilenameOverload_ReadsAndClosesFile()
    {
        var filename = Path.GetTempFileName();
        try
        {
            File.WriteAllText(filename, ValidLine);
            var entry = new CombinedLogReader().ToCombinedLog(filename).Single();

            Assert.That(entry.StatusCode, Is.EqualTo(200));
            using var exclusive = File.Open(filename, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            Assert.That(exclusive.Length, Is.GreaterThan(0));
        }
        finally
        {
            File.Delete(filename);
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ToCombinedLog_CompletedEnumeration_DisposesStream(bool empty)
    {
        var stream = Text(empty ? "" : ValidLine);

        var entries = new CombinedLogReader().ToCombinedLog(stream).ToList();

        Assert.That(entries.Count, Is.EqualTo(empty ? 0 : 1));
        Assert.That(stream.CanRead, Is.False);
    }

    [Test]
    public void ToCombinedLog_LargeInput_StreamsAndDisposesOnEarlyExit()
    {
        var stream = Text(string.Concat(Enumerable.Repeat(ValidLine, 10000)));
        var entries = new CombinedLogReader().ToCombinedLog(stream);
        Assert.That(stream.Position, Is.Zero);

        using (var enumerator = entries.GetEnumerator())
        {
            Assert.That(enumerator.MoveNext(), Is.True);
            var first = enumerator.Current;
            Assert.That(stream.Position, Is.LessThan(stream.Length));
            Assert.That(enumerator.MoveNext(), Is.True);
            Assert.That(first.Request, Is.EqualTo("GET /apache_pb.gif HTTP/1.0"));
            Assert.That(stream.CanRead, Is.True);
        }

        Assert.That(stream.CanRead, Is.False);
    }

    [Test]
    public void ToCombinedLog_MalformedLaterRecord_IsParsedOnlyWhenReached()
    {
        var stream = Text(ValidLine + "broken record\n");
        using var enumerator = new CombinedLogReader().ToCombinedLog(stream).GetEnumerator();

        Assert.That(enumerator.MoveNext(), Is.True);
        var exception = Assert.Throws<InvalidDataException>(() => enumerator.MoveNext());
        Assert.That(exception!.Message, Does.Contain("line 2"));
        Assert.That(stream.CanRead, Is.False);
    }

    [Test]
    public void ToCombinedLog_NonSeekableStream_IsSupported()
    {
        var stream = new NonSeekableStream(Encoding.UTF8.GetBytes(ValidLine));

        var entry = new CombinedLogReader().ToCombinedLog(stream).Single();

        Assert.That(entry.ResponseBytes, Is.EqualTo(4294967296L));
        Assert.That(stream.CanRead, Is.False);
    }

    private static MemoryStream Text(string value) => new(Encoding.UTF8.GetBytes(value));

    private sealed class NonSeekableStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override long Seek(long offset, SeekOrigin loc) => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
    }
}
