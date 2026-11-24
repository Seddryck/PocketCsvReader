using System.Text;
using NUnit.Framework;
using PocketCsvReader.Json.Configuration;

namespace PocketCsvReader.Json.Testing;

public class JsonTableReaderBuilderTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void ReadsTheHeaderRowByDefaultOrWhenExplicitlySelected(bool explicitHeader)
    {
        var builder = new JsonTableReaderBuilder();
        if (explicitHeader)
            builder.WithHeaderRow();

        using var reader = builder.Build().ToDataReader(Input("""[["id","name"],[1,"apple"]]"""));

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(reader.GetOrdinal("id")), Is.EqualTo(1));
        Assert.That(reader.GetString(reader.GetOrdinal("name")), Is.EqualTo("apple"));
        Assert.That(reader.Read(), Is.False);
    }

    [Test]
    public void ReadsTheFirstHeaderlessRowAsData()
    {
        string[] names = ["id", "name"];
        var builder = new JsonTableReaderBuilder().WithoutHeaderRow(names);
        names[0] = "changed";

        using var reader = builder.Build().ToDataReader(Input("""[[1,"apple"]]"""));

        Assert.That(reader.GetOrdinal("id"), Is.Zero);
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetInt32(0), Is.EqualTo(1));
        Assert.That(reader.Read(), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RejectsConflictingHeaderModesInEitherOrder(bool headerFirst)
    {
        var builder = new JsonTableReaderBuilder();
        if (headerFirst)
            builder.WithHeaderRow().WithoutHeaderRow(["id"]);
        else
            builder.WithoutHeaderRow(["id"]).WithHeaderRow();

        Assert.That(() => builder.Build(), Throws.InvalidOperationException.With.Message.Contain("WithHeaderRow"));
    }

    [Test]
    public void CombinesHeaderlessRowsWithJsoncSchemaAndRowFilters()
    {
        var format = new JsonTableReaderBuilder()
            .WithoutHeaderRow(["id"])
            .WithJsonc()
            .WithSchema(schema => schema.Named().WithField<int>("id"))
            .WithFilter<int>("id", id => id > 1)
            .WithSkip(1)
            .WithTake(1)
            .Build();

        using var reader = format.ToDataReader(Input("[/* rows */ [1,], [2], [3], [4],]"));

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetValue(0), Is.EqualTo(3));
        Assert.That(reader.Read(), Is.False);
    }

    private static MemoryStream Input(string content) => new(Encoding.UTF8.GetBytes(content));
}
