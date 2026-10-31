using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;
using PocketCsvReader.Json.Configuration;

namespace PocketCsvReader.Json.Testing;

public class JsonDialectOptionsTests
{
    [Test]
    public async Task IndependentOptions_AcceptOnlyEnabledSyntax(
        [Values(false, true)] bool allowComments,
        [Values(false, true)] bool allowTrailingCommas,
        [Values(false, true)] bool inputComments,
        [Values(false, true)] bool inputTrailingCommas,
        [Values(0, 1, 2)] int projection,
        [Values(0, 1, 2)] int root,
        [Values(false, true)] bool async,
        [Values(false, true)] bool useProfile)
    {
        var comment = inputComments ? "/* delimiters } ] */ // line\r\n" : "";
        var comma = inputTrailingCommas ? "," : "";
        var row = "{\"id\":" + comment + "1,\"items\":[1,2" + comma + "],"
            + "\"ignored\":{\"nested\":[true" + comma + "]" + comma + "}" + comma + "}";
        var rows = "[" + comment + row + "," + comment + row + comma + "]";
        var content = comment + (root switch
        {
            0 => row,
            1 => rows,
            _ => "{\"rows\":" + rows + comma + "}"
        }) + comment;
        var path = root == 2 ? "$.rows[*]" : null;
        JsonReader json;
        if (useProfile)
        {
            var profile = new JsonProfile(
                projectedProperties: projection == 0 ? null : ["id", "items"],
                orderedProjection: projection == 2,
                recordPath: path,
                allowComments: allowComments,
                allowTrailingCommas: allowTrailingCommas);
            // The buffer-size constructor must retain both independent settings.
            json = new JsonReader(profile, bufferSize: 1);
        }
        else
        {
            var builder = new JsonReaderBuilder()
                .WithComments(allowComments).WithTrailingCommas(allowTrailingCommas)
                .WithParserOptimizations(new ParserOptimizationOptions(BufferSize: 1, ReadAhead: false));
            if (projection == 1) builder.WithProjection(p => p.Property("items").Property("id"));
            if (projection == 2) builder.WithOrderedProjection(p => p.Property("id").Property("items"));
            if (path is not null) builder.WithRecordPath(path);
            json = builder.Build();
        }
        using var reader = json.ToDataReader(Input(content));
        if ((inputComments && !allowComments) || (inputTrailingCommas && !allowTrailingCommas))
        {
            var exception = await Assert.ThrowsAsync<InvalidDataException>(async () =>
            {
                while (async ? await reader.ReadAsync() : reader.Read()) { }
            });
            Assert.That(exception!.Message, Does.Contain("position"));
            return;
        }
        var count = 0;
        while (async ? await reader.ReadAsync() : reader.Read())
        {
            Assert.That(reader.GetInt32(reader.GetOrdinal("id")), Is.EqualTo(1));
            Assert.That(reader.GetArray(reader.GetOrdinal("items")), Is.EqualTo(new[] { "1", "2" }));
            count++;
        }
        Assert.That(count, Is.EqualTo(root == 0 ? 1 : 2));
    }

    [Test]
    public void Profile_DefaultsAndOriginalConstructorRemainStrict()
    {
        var profiles = new[]
        {
            new JsonProfile(),
            new JsonProfile(null, null, null, null, null, false, null)
        };
        foreach (var profile in profiles)
            Assert.Multiple(() =>
            {
                Assert.That(profile.AllowComments, Is.False);
                Assert.That(profile.AllowTrailingCommas, Is.False);
            });
        var commentsOnly = new JsonProfile(allowComments: true);
        var trailingOnly = new JsonProfile(allowTrailingCommas: true);
        Assert.Multiple(() =>
        {
            Assert.That(commentsOnly.AllowComments, Is.True);
            Assert.That(commentsOnly.AllowTrailingCommas, Is.False);
            Assert.That(trailingOnly.AllowComments, Is.False);
            Assert.That(trailingOnly.AllowTrailingCommas, Is.True);
        });
    }

    [Test]
    public async Task JsoncShorthand_CanBeOverriddenIndependently(
        [Values(false, true)] bool allowComments, [Values(false, true)] bool allowTrailingCommas,
        [Values(false, true)] bool async)
    {
        var builder = new JsonReaderBuilder().WithJsonc()
            .WithComments(allowComments).WithTrailingCommas(allowTrailingCommas);
        foreach (var (text, comments, trailing) in new[]
        {
            ("{\"id\":1}", false, false),
            ("{\"id\":1/* comment */}", true, false),
            ("{\"id\":1,}", false, true),
            ("{\"id\":1/* comment */,}", true, true)
        })
        {
            using var reader = builder.Build().ToDataReader(Input(text));
            if ((comments && !allowComments) || (trailing && !allowTrailingCommas))
                await Assert.ThrowsAsync<InvalidDataException>(async () =>
                {
                    while (async ? await reader.ReadAsync() : reader.Read()) { }
                });
            else
            {
                Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.True);
                Assert.That(reader.GetInt32(0), Is.EqualTo(1));
                Assert.That(async ? await reader.ReadAsync() : reader.Read(), Is.False);
            }
        }
        using var both = builder.WithJsonc().Build().ToDataReader(Input("{/* comment */\"id\":1,}"));
        Assert.That(async ? await both.ReadAsync() : both.Read(), Is.True);
        Assert.That(both.GetInt32(0), Is.EqualTo(1));
        Assert.That(async ? await both.ReadAsync() : both.Read(), Is.False);
    }

    private static MemoryStream Input(string content) => new(Encoding.UTF8.GetBytes(content));
}
