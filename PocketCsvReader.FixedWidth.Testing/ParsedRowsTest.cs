using System.Text;
using NUnit.Framework;
using PocketCsvReader.FixedWidth.Configuration;

namespace PocketCsvReader.FixedWidth.Testing;

public class ParsedRowsTest
{
    [Test]
    public void ParsedRows_UseConfiguredNamesAndTypes()
    {
        var reader = new FixedWidthReaderBuilder().WithLineTerminator("\n")
            .WithField("Id", 0, 3, FixedWidthPadding.Left, '0')
            .WithField("Name", 3, 3, FixedWidthPadding.None)
            .WithSchema(s => s.Indexed().WithField<int>().WithField<string>()).Build();
        using var arraysStream = new MemoryStream(Encoding.UTF8.GetBytes("001Ada\n002Bob\n"));
        using var dictionariesStream = new MemoryStream(Encoding.UTF8.GetBytes("001Ada\n002Bob\n"));
        Assert.That(reader.ToObjectArrays(arraysStream), Is.EqualTo(new[] { new object[] { 1, "Ada" }, new object[] { 2, "Bob" } }));
        var dictionaries = reader.ToDictionaries(dictionariesStream).ToArray();
        Assert.That(dictionaries[0]["Id"], Is.EqualTo(1));
        Assert.That(dictionaries[1]["Name"], Is.EqualTo("Bob"));
    }
}
