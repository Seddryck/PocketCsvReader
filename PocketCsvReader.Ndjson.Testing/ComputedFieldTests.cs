using System.Text;
using NUnit.Framework;
using PocketCsvReader.Ndjson.Configuration;

namespace PocketCsvReader.Ndjson.Testing;

public class ComputedFieldTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task NamedComputedFieldsFollowCurrentRowShape(bool stable)
    {
        var calls = 0;
        var builder = new NdjsonReaderBuilder().WithDialect(d => d.WithLineTerminator("\n"))
            .WithSchema(schema => schema.Named()
                .WithComputedField<string>("label", ["name", "id"], fields =>
                {
                    calls++;
                    var id = fields.GetFieldValue<int>("id");
                    var name = fields.GetFieldValue<string>("name");
                    var nameOrdinal = id == 2 || stable ? 0 : 1;
                    Assert.That(fields.GetSpan(nameOrdinal).Value.ToString(), Is.EqualTo(name));
                    return $"{id}:{name}";
                }));

        if (stable)
            builder.WithStableFieldLayout();

        var text = stable
            ? """
                {"name":"A\u0064a","id":2}
                {"name":"Grace","id":3}
                {"name":null,"id":4}
                {"name":null,"id":5}
                """
            : """
                {"name":"A\u0064a","id":2}
                {"id":3,"name":"Grace"}
                {"id":4}
                {"name":null,"id":5}
                """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(text));
        using var reader = builder.Build().ToDataReader(stream);

        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.Fields, Is.EqualTo(new[] { "name", "id", "label" }));
        Assert.That(reader.GetFieldType(2), Is.EqualTo(typeof(string)));
        Assert.That(reader.GetString(2), Is.EqualTo("2:Ada"));

        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetFieldValue<string>("label"), Is.EqualTo("3:Grace"));

        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.GetValue("label"), Is.SameAs(DBNull.Value));

        Assert.That(await reader.ReadAsync(), Is.True);
        Assert.That(reader.IsDBNull("label"), Is.True);
        Assert.That(calls, Is.EqualTo(2));
        Assert.That(await reader.ReadAsync(), Is.False);
    }
}
