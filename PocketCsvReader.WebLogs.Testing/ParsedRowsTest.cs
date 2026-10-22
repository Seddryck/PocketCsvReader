using System.Text;
using NUnit.Framework;

namespace PocketCsvReader.WebLogs.Testing;

public class ParsedRowsTest
{
    [Test]
    public void ParsedRows_MatchDataReaderValuesAndNames()
    {
        const string content = "127.0.0.1 - frank [10/Oct/2000:13:55:36 -0700] \"GET / HTTP/1.0\" 200 42\n";
        using var valuesStream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        using var arraysStream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        using var dictionariesStream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        using var dataReader = new CommonLogReader().ToDataReader(valuesStream);
        Assert.That(dataReader.Read(), Is.True);
        var expected = Enumerable.Range(0, dataReader.FieldCount)
            .Select(i => dataReader.IsDBNull(i) ? null : dataReader.GetValue(i)).ToArray();
        var array = new CommonLogReader().ToObjectArrays(arraysStream).Single();
        var dictionary = new CommonLogReader().ToDictionaries(dictionariesStream).Single();
        Assert.That(array, Is.EqualTo(expected));
        for (var i = 0; i < expected.Length; i++)
            Assert.That(dictionary[dataReader.GetName(i)], Is.EqualTo(expected[i]));
    }
}
