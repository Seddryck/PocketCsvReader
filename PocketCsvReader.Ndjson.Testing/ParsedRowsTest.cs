using System.Text;
using NUnit.Framework;
using PocketCsvReader.Ndjson.Configuration;

namespace PocketCsvReader.Ndjson.Testing;

public class ParsedRowsTest
{
    [Test]
    public void ParsedRows_PreserveChangingNamesAndNulls()
    {
        const string content = "{\"id\":1,\"name\":null}\n{\"name\":\"Ada\"}";
        using var arraysStream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        using var dictionariesStream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var arrays = new NdjsonReader(new NdjsonProfile("\n")).ToObjectArrays(arraysStream).ToArray();
        var dictionaries = new NdjsonReader(new NdjsonProfile("\n")).ToDictionaries(dictionariesStream).ToArray();
        Assert.That(arrays, Is.EqualTo(new[] { new object?[] { "1", null }, new object?[] { "Ada" } }));
        Assert.That(dictionaries[0]["name"], Is.Null);
        Assert.That(dictionaries[1].Keys, Is.EqualTo(new[] { "name" }));
    }
}
