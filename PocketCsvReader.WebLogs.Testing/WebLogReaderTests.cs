using System.Text;
using NUnit.Framework;
using PocketCsvReader.WebLogs.Configuration;

namespace PocketCsvReader.WebLogs.Testing;

public class WebLogReaderTests
{
    [Test]
    public async Task CommonLogReader_ReadAsync_AdvancesAndExposesTypedValues()
    {
        await using var reader = new CommonLogReader().ToDataReader(Text(
            "127.0.0.1 - frank [10/Oct/2000:13:55:36 -0700] \"GET / HTTP/1.0\" 200 42\n"));

        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetInt32(reader.GetOrdinal("StatusCode")), Is.EqualTo(200));
        Assert.That(await reader.ReadAsync(), Is.False);
    }

    [Test]
    public void CommonLogReader_ValidRecord_ExposesNamedFieldsAndNulls()
    {
        using var reader = new CommonLogReader().ToDataReader(Text(
            "127.0.0.1 - frank [10/Oct/2000:13:55:36 -0700] \"GET /apache_pb.gif HTTP/1.0\" 200 2326\n"));

        Assert.That(reader.Read(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.FieldCount, Is.EqualTo(7));
            Assert.That(reader.GetName(0), Is.EqualTo("RemoteHost"));
            Assert.That(reader.GetString(reader.GetOrdinal("AuthenticatedUser")), Is.EqualTo("frank"));
            Assert.That(reader.GetString(reader.GetOrdinal("Timestamp")), Is.EqualTo("10/Oct/2000:13:55:36 -0700"));
            Assert.That(reader.GetString(reader.GetOrdinal("Request")), Is.EqualTo("GET /apache_pb.gif HTTP/1.0"));
            Assert.That(reader.GetInt32(reader.GetOrdinal("StatusCode")), Is.EqualTo(200));
            Assert.That(reader.GetInt64(reader.GetOrdinal("ResponseBytes")), Is.EqualTo(2326));
            Assert.That(reader.IsDBNull(reader.GetOrdinal("Identity")), Is.True);
        });
        Assert.That(reader.Read(), Is.False);
    }

    [Test]
    public void CommonLogReader_MalformedRecord_ReportsLineNumber()
    {
        using var reader = new CommonLogReader().ToDataReader(Text("broken record\n"));

        var exception = Assert.Throws<InvalidDataException>(() => reader.Read());
        Assert.That(exception!.Message, Does.Contain("line 1"));
    }

    [Test]
    public void W3cReader_DirectivesAndRecords_ExposeDynamicSchema()
    {
        const string content = """
            #Software: Microsoft Internet Information Services 10.0
            #Version: 1.0
            #Fields: date time c-ip cs-method cs-uri-stem sc-status sc-bytes
            2026-09-30 12:34:56 192.0.2.1 GET /index.html 200 1234
            2026-09-30 12:35:01 192.0.2.2 GET /missing - -
            """;
        using var reader = new W3cExtendedLogReader().ToDataReader(Text(content));

        Assert.That(reader.Read(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.FieldCount, Is.EqualTo(7));
            Assert.That(reader.GetName(2), Is.EqualTo("c-ip"));
            Assert.That(reader.GetString(reader.GetOrdinal("cs-uri-stem")), Is.EqualTo("/index.html"));
            Assert.That(reader.GetInt32(reader.GetOrdinal("sc-status")), Is.EqualTo(200));
            Assert.That(reader.Directives.Select(item => item.Name), Is.EqualTo(new[] { "Software", "Version", "Fields" }));
            Assert.That(reader.Directives[0].Value, Is.EqualTo("Microsoft Internet Information Services 10.0"));
        });

        Assert.That(reader.Read(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.IsDBNull(reader.GetOrdinal("sc-status")), Is.True);
            Assert.That(reader.IsDBNull(reader.GetOrdinal("sc-bytes")), Is.True);
        });
        Assert.That(reader.Read(), Is.False);
    }

    [Test]
    public void W3cReader_DataBeforeFields_ThrowsDiagnostic()
    {
        using var reader = new W3cExtendedLogReader().ToDataReader(Text("2026-09-30 12:34:56\n"));

        var exception = Assert.Throws<InvalidDataException>(() => reader.Read());
        Assert.That(exception!.Message, Does.Contain("#Fields"));
    }

    [Test]
    public void W3cReader_ChangedFieldsDirective_ThrowsDiagnostic()
    {
        using var reader = new W3cExtendedLogReader().ToDataReader(Text(
            "#Fields: date time\n2026-09-30 12:34:56\n#Fields: date c-ip\n2026-09-30 192.0.2.1\n"));

        Assert.That(reader.Read(), Is.True);
        var exception = Assert.Throws<InvalidDataException>(() => reader.Read());
        Assert.That(exception!.Message, Does.Contain("changes the schema"));
    }

    [Test]
    public void W3cReader_RepeatedFieldsDirectiveWithSameSchema_IsAccepted()
    {
        using var reader = new W3cExtendedLogReader().ToDataReader(Text(
            "#Fields: date time\n2026-09-30 12:34:56\n#Fields: date time\n2026-10-01 01:02:03\n"));

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.Read(), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(reader.GetString(0), Is.EqualTo("2026-10-01"));
            Assert.That(reader.Directives.Count(item => item.Name == "Fields"), Is.EqualTo(2));
        });
    }

    [Test]
    public void W3cReader_MismatchedFieldCount_ReportsExpectedAndActualCounts()
    {
        using var reader = new W3cExtendedLogReader().ToDataReader(Text(
            "#Fields: date time c-ip\n2026-09-30 12:34:56\n"));

        var exception = Assert.Throws<InvalidDataException>(() => reader.Read());
        Assert.Multiple(() =>
        {
            Assert.That(exception!.Message, Does.Contain("expected 3 fields"));
            Assert.That(exception.Message, Does.Contain("found 2"));
        });
    }

    [Test]
    public void Builders_CommonConfiguration_PreserveConcreteTypes()
    {
        var common = new CommonLogReaderBuilder()
            .WithParserOptimizations(new ParserOptimizationOptions(BufferSize: 32, RowCountAtStart: true))
            .Build();
        var w3c = new W3cExtendedLogReaderBuilder()
            .WithResource(resource => resource.WithEncoding("utf-8"))
            .Build();

        Assert.Multiple(() =>
        {
            Assert.That(common, Is.TypeOf<CommonLogReader>());
            Assert.That(w3c, Is.TypeOf<W3cExtendedLogReader>());
        });
    }

    [Test]
    public void Readers_OutputArraysTablesAndObjects()
    {
        var common = new CommonLogReader();
        const string line = "127.0.0.1 - frank [10/Oct/2000:13:55:36 -0700] \"GET / HTTP/1.1\" 200 42\n";

        var array = common.ToArrayString(Text(line)).Single();
        var table = common.ToDataTable(Text(line));
        var entry = common.To<CommonEntry>(Text(line)).Single();

        Assert.Multiple(() =>
        {
            Assert.That(array[4], Is.EqualTo("GET / HTTP/1.1"));
            Assert.That(table.Rows[0]["StatusCode"], Is.EqualTo("200"));
            Assert.That(entry, Is.EqualTo(new CommonEntry("127.0.0.1", null, "frank", "10/Oct/2000:13:55:36 -0700", "GET / HTTP/1.1", 200, 42)));
        });
    }

    private static MemoryStream Text(string value)
        => new(Encoding.UTF8.GetBytes(value));

    public sealed record CommonEntry(
        string RemoteHost,
        string? Identity,
        string AuthenticatedUser,
        string Timestamp,
        string Request,
        int StatusCode,
        long ResponseBytes);
}
