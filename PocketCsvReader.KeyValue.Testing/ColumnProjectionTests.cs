using System.Data;
using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;
using PocketCsvReader.KeyValue.Configuration;

namespace PocketCsvReader.KeyValue.Testing;

public class ColumnProjectionTests
{
    [Test]
    public async Task ProjectionPreservesOrderAliasesAndValueSemantics(
        [Values] bool logfmt, [Values] bool async)
    {
        var text = logfmt ? "id=1 unused=a user=\"Ada\\n\" user=ignored\nuser= id=2\nid=3\n\n"
            : "id:1\tunused:a\tuser:Ada\tuser:ignored\nuser:\tid:2\nid:3\n\n";
        using var reader = Build(logfmt).ToDataReader(Stream(text));
        IDataReader data = reader;

        Assert.That(await Read(), Is.True);
        Assert.That(data.FieldCount, Is.EqualTo(2));
        Assert.That(data.GetName(0), Is.EqualTo("Name"));
        Assert.That(data.GetOrdinal("Id"), Is.EqualTo(1));
        Assert.That(reader.TryGetOrdinal("unused", out _), Is.False);
        Assert.That(reader.TryGetOrdinal("user", out _), Is.False);
        Assert.That(data.GetString(0), Is.EqualTo(logfmt ? "Ada\n" : "Ada"));
        Assert.That(reader.GetRawString(0), Is.EqualTo(logfmt ? "\"Ada\\n\"" : "Ada"));
        Assert.That(data.GetValue(1), Is.EqualTo(1));

        Assert.That(await Read(), Is.True);
        Assert.That(data.GetString(0), Is.Empty);
        Assert.That(data.IsDBNull(0), Is.False);
        Assert.That(data.GetInt32(1), Is.EqualTo(2));

        Assert.That(await Read(), Is.True);
        Assert.That(data.IsDBNull(0), Is.True);
        Assert.That(data.GetValue(0), Is.SameAs(DBNull.Value));
        Assert.That(data.GetInt32(1), Is.EqualTo(3));

        Assert.That(await Read(), Is.True);
        Assert.That(data.FieldCount, Is.EqualTo(2));
        Assert.That(data.IsDBNull(0), Is.True);
        Assert.That(data.IsDBNull(1), Is.True);
        Assert.That(await Read(), Is.False);

        async Task<bool> Read() => async ? await reader.ReadAsync() : reader.Read();
    }

    [TestCase(false, "id:1\tuser:Ada\nuser:\tid:2\nid:3")]
    [TestCase(true, "id=1 user=Ada\nuser= id=2\nid=3")]
    public void SharedMaterializersUseProjectedSchema(bool logfmt, string text)
    {
        var format = Build(logfmt);
        var table = format.ToDataTable(Stream(text));
        var arrays = format.ToObjectArrays(Stream(text)).ToArray();
        var dictionaries = format.ToDictionaries(Stream(text)).ToArray();

        Assert.That(table.Columns.Cast<DataColumn>().Select(c => c.ColumnName), Is.EqualTo(new[] { "Name", "Id" }));
        Assert.That(table.Rows[0]["Name"], Is.EqualTo("Ada"));
        Assert.That(table.Rows[1]["Name"], Is.Empty);
        Assert.That(table.Rows[2].IsNull("Name"), Is.True);
        Assert.That(arrays[0], Is.EqualTo(new object[] { "Ada", 1 }));
        Assert.That(arrays[2], Is.EqualTo(new object?[] { null, 3 }));
        Assert.That(dictionaries[1]["Id"], Is.TypeOf<int>().And.EqualTo(2));
    }

    [Test]
    public void ProjectionRejectsEmptyConfigurationsAndDuplicateOutputNames()
    {
        var builder = new LogfmtReaderBuilder();
        Assert.Throws<InvalidOperationException>(() => builder.WithProjection(columns => columns));
        Assert.Throws<ArgumentException>(() => builder.WithProjection(columns => columns.Column("id").Column("user", "id")));
    }

    [TestCase(false, "id:1\tbad label:ignored")]
    [TestCase(true, "id=1 unused=\"bad\\q\"")]
    public void ProjectionStillValidatesUnselectedFields(bool logfmt, string text)
    {
        using var reader = Build(logfmt).ToDataReader(Stream(text));
        Assert.Throws<InvalidDataException>(() => reader.Read());
    }

    private static KeyValueReader Build(bool logfmt) => logfmt
        ? Configure(new LogfmtReaderBuilder()).Build()
        : Configure(new LtsvReaderBuilder()).Build();

    private static TBuilder Configure<TBuilder>(KeyValueReaderBuilder<TBuilder> builder)
        where TBuilder : KeyValueReaderBuilder<TBuilder>
        => builder.WithProjection(columns => columns.Column("user", "Name").Column("id", "Id"))
            .WithParserOptimizations(new(BufferSize: 3))
            .WithSchema(schema => schema.Named().WithField<int>("Id"));

    private static MemoryStream Stream(string text) => new(Encoding.UTF8.GetBytes(text));
}
