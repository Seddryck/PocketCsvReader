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
    public class CsvReaderTest
    {
        [Test]
        [TestCase("Ansi")]
        [TestCase("Utf16-BE")]
        [TestCase("Utf16-LE")]
        [TestCase("Utf8-BOM")]
        [TestCase("Utf8")]
        public void ToDataTable_Financial_CorrectRowsColumns(string filename)
        {
            var reader = new CsvReader(new CsvProfile('\t', '\"', "\r\n", true));

            using (var stream =
                    Assembly.GetExecutingAssembly()
                        .GetManifestResourceStream($"{Assembly.GetExecutingAssembly().GetName().Name}.Resources.{filename}.csv")
                        ?? throw new FileNotFoundException()
            )
            {
                var dataTable = reader.ToDataTable(stream);
                Assert.That(dataTable.Columns.Count, Is.EqualTo(14));
                Assert.That(dataTable.Rows.Count, Is.EqualTo(21));
            }
            
        }
    }
}
