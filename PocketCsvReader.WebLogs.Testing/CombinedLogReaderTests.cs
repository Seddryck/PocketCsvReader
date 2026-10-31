using System.Text;
using NUnit.Framework;
using PocketCsvReader.WebLogs.Configuration;

namespace PocketCsvReader.WebLogs.Testing;

public class CombinedLogReaderTests
{
    private const string CommonLine = "127.0.0.1 - frank [10/Oct/2000:13:55:36 -0700] \"GET / HTTP/1.1\" 200 42";
    private const string Line = CommonLine + " \"https://example.com/start\" \"Example Browser\"";

    [Test]
    public void Outputs_ExposeNineNamedFieldsAndCustomObjects()
    {
        var combined = new CombinedLogReader();
        using var reader = combined.ToDataReader(Text(Line));
        Assert.That(reader.FieldCount, Is.EqualTo(9));
        Assert.That(reader.Read(), Is.True);
        Assert.That(Enumerable.Range(0, 9).Select(reader.GetName), Is.EqualTo(new[]
        {
            "RemoteHost", "Identity", "AuthenticatedUser", "Timestamp", "Request", "StatusCode",
            "ResponseBytes", "Referrer", "UserAgent"
        }));
        Assert.That(reader.GetString(reader.GetOrdinal("Referrer")), Is.EqualTo("https://example.com/start"));
        Assert.That(reader.GetString(reader.GetOrdinal("UserAgent")), Is.EqualTo("Example Browser"));
        Assert.That(reader.IsDBNull(1), Is.True);
        Assert.That(reader.Read(), Is.False);

        var array = combined.ToArrayString(Text(Line)).Single();
        var table = combined.ToDataTable(Text(Line));
        var entry = combined.To<CombinedEntry>(Text(Line)).Single();
        Assert.That(array, Is.EqualTo(new string?[]
        {
            "127.0.0.1", null, "frank", "10/Oct/2000:13:55:36 -0700", "GET / HTTP/1.1",
            "200", "42", "https://example.com/start", "Example Browser"
        }));
        Assert.That(table.Rows[0]["Referrer"], Is.EqualTo(array[7]));
        Assert.That(table.Rows[0]["UserAgent"], Is.EqualTo(array[8]));
        Assert.That(entry, Is.EqualTo(new CombinedEntry(array[0], null, array[2], array[3], array[4], 200, 42, array[7], array[8])));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task QuotedFields_AcrossTinyBuffers_PreserveSpacesAndEscapedQuoteSyntax(bool asynchronous)
    {
        const string request = "GET /a\\\"b HTTP/1.1";
        const string referrer = "https://example.com/a\\\"b with spaces";
        var agent = "Browser \\\"Example\\\" café 雪 " + new string('x', 9000);
        var line = $"127.0.0.1 - frank [10/Oct/2000:13:55:36 -0700] \"{request}\" 200 42 \"{referrer}\" \"{agent}\"";
        var combined = new CombinedLogReaderBuilder()
            .WithParserOptimizations(new ParserOptimizationOptions(BufferSize: 1, RowCountAtStart: true))
            .Build();
        await using var reader = combined.ToDataReader(Text(line + "\r\n" + Line));
        Assert.That(asynchronous ? await reader.ReadAsync() : reader.Read(), Is.True);
        Assert.That(reader.GetString(4), Is.EqualTo(request));
        Assert.That(reader.GetString(7), Is.EqualTo(referrer));
        var saved = reader.GetString(8);
        Assert.That(saved, Is.EqualTo(agent));
        Assert.That(asynchronous ? await reader.ReadAsync() : reader.Read(), Is.True);
        Assert.That(reader.GetString(8), Is.EqualTo("Example Browser"));
        Assert.That(saved, Is.EqualTo(agent));
        Assert.That(asynchronous ? await reader.ReadAsync() : reader.Read(), Is.False);

        var typed = combined.ToCombinedLog(Text(line)).Single();
        Assert.That(typed.Request, Is.EqualTo(request));
        Assert.That(typed.Referrer, Is.EqualTo(referrer));
        Assert.That(typed.UserAgent, Is.EqualTo(agent));
    }

    [TestCase("")]
    [TestCase(" \"referrer\"")]
    [TestCase(" \"referrer\" \"unfinished")]
    [TestCase(" referrer \"agent\"")]
    [TestCase(" \"referrer\" agent")]
    [TestCase(" \"referrer\"\"agent\"")]
    [TestCase(" \"referrer\" \"agent\" extra")]
    public void MalformedCombinedRecord_ReportsFormatAndPhysicalLine(string suffix)
    {
        using var reader = new CombinedLogReader().ToDataReader(Text(Line + "\n\n" + CommonLine + suffix));
        Assert.That(reader.Read(), Is.True);
        var exception = Assert.Throws<InvalidDataException>(() => reader.Read());
        Assert.That(exception!.Message, Does.Contain("Combined Log Format").And.Contain("line 3"));
    }

    [Test]
    public void CommonReader_RemainsStrictlySevenFields()
    {
        Assert.That(new CommonLogReader().ToCommonLog(Text(CommonLine)).Single().StatusCode, Is.EqualTo(200));
        using var reader = new CommonLogReader().ToDataReader(Text(Line));
        Assert.That(reader.FieldCount, Is.EqualTo(7));
        Assert.That(Assert.Throws<InvalidDataException>(() => reader.Read())!.Message, Does.Contain("7-field Common Log Format"));
    }

    [Test]
    public void Profile_RequiresCombinedFormat()
    {
        Assert.Throws<ArgumentNullException>(() => new CombinedLogReader(null!));
        Assert.Throws<ArgumentException>(() => new CombinedLogReader(new WebLogProfile(WebLogFormat.Common)));
        Assert.Throws<ArgumentException>(() => new CombinedLogReader(new WebLogProfile(WebLogFormat.W3cExtended)));
    }

    private static MemoryStream Text(string value) => new(Encoding.UTF8.GetBytes(value));

    public sealed record CombinedEntry(string? RemoteHost, string? Identity, string? AuthenticatedUser,
        string? Timestamp, string? Request, int StatusCode, long ResponseBytes, string? Referrer, string? UserAgent);
}
