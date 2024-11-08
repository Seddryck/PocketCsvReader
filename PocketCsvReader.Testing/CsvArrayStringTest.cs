using PocketCsvReader;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;

namespace PocketCsvReader.Testing
{
    [TestFixture]
    public class CsvArrayStringTest
    {
        private static MemoryStream CreateStream(string content)
        {
            var byteArray = Encoding.UTF8.GetBytes(content);
            var stream = new MemoryStream(byteArray);
            stream.Position = 0;
            return stream;
        }

        [Test]
        [TestCase("Ansi")]
        [TestCase("Utf16-BE")]
        [TestCase("Utf16-LE")]
        [TestCase("Utf8-BOM")]
        [TestCase("Utf8")]
        public void ToDataReader_Financial_CorrectRowsColumns(string filename)
        {
            var reader = new CsvReader(new CsvProfile('\t', '\"', "\r\n", true));

            using (var stream =
                    Assembly.GetExecutingAssembly()
                        .GetManifestResourceStream($"{Assembly.GetExecutingAssembly().GetName().Name}.Resources.{filename}.csv")
                        ?? throw new FileNotFoundException()
            )
            {
                var rows = reader.ToArrayString(stream);
                Assert.That(rows.Count, Is.EqualTo(21));
            }
        }

        [Test]
        [TestCase("Ansi")]
        [TestCase("Utf16-BE")]
        [TestCase("Utf16-LE")]
        [TestCase("Utf8-BOM")]
        [TestCase("Utf8")]
        public void ToDataReader_Financial_CorrectColumnByIndexer(string filename)
        {
            var reader = new CsvReader(new CsvProfile('\t', '\"', "\r\n", true));

            using (var stream =
                    Assembly.GetExecutingAssembly()
                        .GetManifestResourceStream($"{Assembly.GetExecutingAssembly().GetName().Name}.Resources.{filename}.csv")
                        ?? throw new FileNotFoundException()
            )
            {
                foreach(var row in reader.ToArrayString(stream))
                {
                    Assert.Multiple(() =>
                    {
                        Assert.That(row[0], Is.EqualTo("2018"));
                        Assert.That(row[1], Is.EqualTo("7"));
                        Assert.That(row[2], Is.EqualTo("1"));
                        Assert.That(row[13], Does.StartWith("2018-"));
                    });
                } 
            }
        }

        [Test]
        [TestCase("Ansi")]
        [TestCase("Utf16-BE")]
        [TestCase("Utf16-LE")]
        [TestCase("Utf8-BOM")]
        [TestCase("Utf8")]
        public void ToDataReader_Financial_CorrectColumnWithGetStringIndex(string filename)
        {
            var reader = new CsvReader(new CsvProfile('\t', '\"', "\r\n", true));

            using (var stream =
                    Assembly.GetExecutingAssembly()
                        .GetManifestResourceStream($"{Assembly.GetExecutingAssembly().GetName().Name}.Resources.{filename}.csv")
                        ?? throw new FileNotFoundException()
            )
            {
                var r = reader.ToArrayString(stream);
                foreach (var row in reader.ToArrayString(stream))
                {
                    Assert.Multiple(() =>
                    {
                        Assert.That(row[0], Is.EqualTo("2018"));
                        Assert.That(row[1], Is.EqualTo("7"));
                        Assert.That(row[2], Is.EqualTo("1"));
                        Assert.That(row[13], Does.StartWith("2018-"));
                    });
                }
            }
        }
    }
}
