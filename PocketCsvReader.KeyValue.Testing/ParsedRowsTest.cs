using System.Text;
using NUnit.Framework;
using PocketCsvReader.KeyValue.Configuration;

namespace PocketCsvReader.KeyValue.Testing;

public class ParsedRowsTest
{
    [TestCase(false)]
    [TestCase(true)]
    public void ParsedRows_PreserveRecordOrderAndNames(bool logfmt)
    {
        const string ltsv = "id:1\tname:Ada\nname:Grace\tid:2";
        const string log = "id=1 name=Ada\nname=Grace id=2";
        using var arraysStream = new MemoryStream(Encoding.UTF8.GetBytes(logfmt ? log : ltsv));
        using var dictionariesStream = new MemoryStream(Encoding.UTF8.GetBytes(logfmt ? log : ltsv));
        var arrays = logfmt ? new LogfmtReaderBuilder().Build().ToObjectArrays(arraysStream)
            : new LtsvReaderBuilder().Build().ToObjectArrays(arraysStream);
        var dictionaries = (logfmt ? new LogfmtReaderBuilder().Build().ToDictionaries(dictionariesStream)
            : new LtsvReaderBuilder().Build().ToDictionaries(dictionariesStream)).ToArray();
        Assert.That(arrays, Is.EqualTo(new[] { new[] { "1", "Ada" }, new[] { "Grace", "2" } }));
        Assert.That(dictionaries[0]["id"], Is.EqualTo("1"));
        Assert.That(dictionaries[1]["id"], Is.EqualTo("2"));
    }
}
