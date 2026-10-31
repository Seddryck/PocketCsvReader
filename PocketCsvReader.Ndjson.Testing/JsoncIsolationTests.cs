using System.Text;
using NUnit.Framework;
using PocketCsvReader.Ndjson.Configuration;

namespace PocketCsvReader.Ndjson.Testing;

public class JsoncIsolationTests
{
    [Test]
    public async Task JsonValuesRemainStrict(
        [Values("{\"id\":1,}", "{/* comment */\"id\":1}", "{\"id\":1}// comment")] string invalid,
        [Values(0, 1, 2, 3, 4)] int mode,
        [Values(false, true)] bool async)
    {
        var builder = new NdjsonReaderBuilder().WithDialect(d => d.WithLineTerminator("\n"));
        if (mode == 1) builder.WithProjection(p => p.Property("id"));
        if (mode == 2) builder.WithOrderedProjection(p => p.Property("id"));
        if (mode == 3) builder.WithStableObjectShape();
        if (mode == 4) builder.WithRecordPath("$");
        using var reader = builder.Build().ToDataReader(new MemoryStream(
            Encoding.UTF8.GetBytes("{\"id\":1}\n" + invalid + "\n")));
        Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.True);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            while (async ? await reader.ReadAsync() : reader.Read()) { }
        });
    }
}
