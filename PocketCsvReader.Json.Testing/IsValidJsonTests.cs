using System.Collections;
using System.Data;
using System.Globalization;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;
using PocketCsvReader.Json.Configuration;

namespace PocketCsvReader.Json.Testing;

public class IsValidJsonTests
{
    private static MemoryStream Input(string text) => new(Encoding.UTF8.GetBytes(text));

    [TestCase(false)]
    [TestCase(true)]
    public void JsonNull_IsValidWhenGetValueReturnsDbNullRegardlessOfNullableFlag(bool nullable)
    {
        var schema = new SchemaDescriptor.NamedSchemaDescriptor();
        schema.Fields.Add(new FieldDescriptor(typeof(int), Name: "value", IsNullable: nullable));
        using var reader = new JsonDataReader(Input("[{\"value\":null}]"), new JsonProfile(schema));
        Assert.That(() => reader.IsValid("value"), Throws.TypeOf<InvalidOperationException>());
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.IsValid(0), Is.True);
        Assert.That(reader.IsValid("value"), Is.True);
        Assert.That(reader.GetValue(0), Is.SameAs(DBNull.Value));
        Assert.That(reader.Read(), Is.False);
        Assert.That(() => reader.IsValid(0), Throws.TypeOf<InvalidOperationException>());
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ChangingRowShapes_ReuseValidationEntriesAndKeepFormatsSeparate(bool asynchronous)
    {
        var schema = new SchemaDescriptor.NamedSchemaDescriptor();
        schema.Fields.Add(new FieldDescriptor(typeof(DateTime), Name: "iso", Format: new TemporalFormatDescriptor("yyyy-MM-dd", CultureInfo.InvariantCulture)));
        schema.Fields.Add(new FieldDescriptor(typeof(DateTime), Name: "eu", Format: new TemporalFormatDescriptor("dd/MM/yyyy", CultureInfo.InvariantCulture)));
        await using var reader = new JsonDataReader(Input(
            "[{\"iso\":\"2026-02-28\",\"eu\":\"28/02/2026\"}," +
            "{\"eu\":\"31/02/2026\",\"iso\":\"2026-02-31\"}," +
            "{\"iso\":\"2026-03-01\",\"eu\":\"01/03/2026\"}]"), new JsonProfile(schema));
        object[]? originalEntries = null;
        for (var row = 0; row < 3; row++)
        {
            Assert.That(asynchronous ? await reader.ReadAsync() : reader.Read(), Is.True);
            Assert.That(reader.IsValid("iso"), Is.EqualTo(row != 1));
            Assert.That(reader.IsValid("eu"), Is.EqualTo(row != 1));
            Assert.That(reader.IsValid(reader.GetOrdinal("iso")), Is.EqualTo(row != 1));
            Assert.That(reader.IsValid(reader.GetOrdinal("eu")), Is.EqualTo(row != 1));
            var entries = (IDictionary)typeof(BaseDataRecord<JsonProfile>).GetField("_schemaParsers", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(reader)!;
            var currentEntries = entries.Values.Cast<object>().ToArray();
            Assert.That(currentEntries, Has.Length.EqualTo(2));
            if (originalEntries is null) originalEntries = currentEntries;
            else
                for (var index = 0; index < originalEntries.Length; index++)
                    Assert.That(currentEntries[index], Is.SameAs(originalEntries[index]));
            if (row == 1) Assert.That(() => reader.GetValue(reader.GetOrdinal("iso")), Throws.TypeOf<FormatException>());
            else Assert.That(reader.GetValue(reader.GetOrdinal("eu")), Is.EqualTo(row == 0 ? new DateTime(2026, 2, 28) : new DateTime(2026, 3, 1)));
        }
    }

    [Test]
    public void UndeclaredJsonProperty_RemainsReadableText()
    {
        using var reader = new JsonReaderBuilder().WithSchema(s => s.Named().WithField<int>("count")).Build()
            .ToDataReader(Input("[{\"count\":42,\"date\":\"2026-02-31\"}]"));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetFieldType(reader.GetOrdinal("date")), Is.EqualTo(typeof(object)));
        Assert.That(reader.IsValid("date"), Is.True);
        Assert.That(reader.GetValue(reader.GetOrdinal("date")), Is.EqualTo("2026-02-31"));
    }

    [TestCase("[1,null,2]", true)]
    [TestCase("[1,\"bad\",2]", false)]
    public void SchemaArrayElements_PreserveNullAndConversionRules(string array, bool expected)
    {
        using var reader = new JsonReaderBuilder().WithSchema(s => s.Named().WithField<int[]>("values")).Build()
            .ToDataReader(Input("[{\"values\":" + array + "}]"));
        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.IsValid("values"), Is.EqualTo(expected));
        if (expected) Assert.That(reader.GetValue(0), Is.EqualTo(new[] { 1, 0, 2 }));
        else Assert.That(() => reader.GetValue(0), Throws.TypeOf<FormatException>());
    }
}
