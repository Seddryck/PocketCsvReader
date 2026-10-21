using System.Text;
using NUnit.Framework;
using PocketCsvReader.Json.Configuration;

namespace PocketCsvReader.Json.Testing;

public class ParsedRowsTest
{
    [Test]
    public async Task DataReaderAsync_ResolvesNamedParsersWhenFieldOrderChanges()
    {
        var reader = new JsonReaderBuilder()
            .WithSchema(s => s.Named().WithField<int>("id").WithField<string>("name"))
            .Build();
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(
            "[{\"id\":1,\"name\":\"Ada\"},{\"name\":\"Grace\",\"id\":2},{\"id\":3,\"name\":\"Bob\"}]"));
        await using var dataReader = reader.ToDataReader(stream);
        for (var id = 1; id <= 3; id++)
        {
            Assert.That(await dataReader.ReadAsync(), Is.True);
            Assert.That(dataReader.GetValue(dataReader.GetOrdinal("id")), Is.EqualTo(id));
            Assert.That(dataReader.GetValue(dataReader.GetOrdinal("name")), Is.TypeOf<string>());
        }
        Assert.That(await dataReader.ReadAsync(), Is.False);
    }

    [Test]
    public void ParsedRows_PreserveChangingShapesTypesAndNulls()
    {
        var reader = new JsonReaderBuilder()
            .WithSchema(s => s.Named().WithField<int>("id").WithField<string>("name"))
            .Build();
        const string content = "[{\"id\":1,\"name\":null},{\"name\":\"Ada\"},{\"id\":2}]";
        using var arraysStream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        using var dictionariesStream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var arrays = reader.ToObjectArrays(arraysStream).ToArray();
        var dictionaries = reader.ToDictionaries(dictionariesStream).ToArray();
        Assert.That(arrays, Is.EqualTo(new[] { new object?[] { 1, null }, new object?[] { "Ada" }, new object?[] { 2 } }));
        Assert.That(dictionaries[0]["name"], Is.Null);
        Assert.That(dictionaries[1].Keys, Is.EqualTo(new[] { "name" }));
        Assert.That(dictionaries[2]["id"], Is.EqualTo(2));
    }
}
