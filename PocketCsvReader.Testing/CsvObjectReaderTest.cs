using PocketCsvReader;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.VisualStudio.TestPlatform.PlatformAbstractions.Interfaces;

namespace PocketCsvReader.Testing;

[TestFixture]
public class CsvObjectReaderTest
{
    private static MemoryStream CreateStream(string content)
    {
        byte[] byteArray = Encoding.UTF8.GetBytes(content);
        MemoryStream stream = new MemoryStream(byteArray);
        stream.Position = 0;
        return stream;
    }

    private record struct Human(string Name, bool IsAdult);
    [Test]
    public void GetString_SingleFieldAttemptForSecond_Throws()
    {
        var spanMapper = new SpanMapper<Human>((span, fieldSpans) =>
        {
            return new Human(
                span.Slice(fieldSpans.First().Start, fieldSpans.First().Length).ToString(),
                int.Parse(span.Slice(fieldSpans.Last().Start, fieldSpans.Last().Length).ToString()) > 18);
        });

        var profile = new CsvProfile(',', '\"', "\r\n", false);
        using var stream = CreateStream("foo,16\r\nbar,21");
        using var dataReader = new CsvObjectReader<Human>(stream, profile, spanMapper);

        var humans = dataReader.Read().ToArray();
        Assert.That(humans, Has.Length.EqualTo(2));
        Assert.That(humans[0].Name, Is.EqualTo("foo"));
        Assert.That(humans[0].IsAdult, Is.False);
        Assert.That(humans[1].Name, Is.EqualTo("bar"));
        Assert.That(humans[1].IsAdult, Is.True);
    }

    private record struct Financial(
        int Year, int Month, int Day, DateTime DateTime,
        string ResolutionCode, string Status, string AreaCode, string AreaTypeCode, string AreaName, string MapCode,
        decimal Expenses, decimal Income, string Currency, DateTime UpdateTime);
    [Test]
    [TestCase("Ansi")]
    [TestCase("Utf16-BE")]
    [TestCase("Utf16-LE")]
    [TestCase("Utf8-BOM")]
    [TestCase("Utf8")]
    public void Read_FinancialWithCompleteParsers_CorrectRowsColumns(string filename)
    {
        var profile = new CsvProfile('\t', '\"', "\r\n", true);

        using (var stream =
                Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream($"{Assembly.GetExecutingAssembly().GetName().Name}.Resources.{filename}.csv")
                    ?? throw new FileNotFoundException()
        )
        {
            var spanMapper = new SpanMapper<Financial>((span, fieldSpans) =>
            {
                return new Financial(
                    int.Parse(span.Slice(fieldSpans.ElementAt(0).Start, fieldSpans.ElementAt(0).Length)),
                    int.Parse(span.Slice(fieldSpans.ElementAt(1).Start, fieldSpans.ElementAt(1).Length)),
                    int.Parse(span.Slice(fieldSpans.ElementAt(2).Start, fieldSpans.ElementAt(2).Length)),
                    DateTime.Parse(span.Slice(fieldSpans.ElementAt(3).Start, fieldSpans.ElementAt(3).Length)),
                    span.Slice(fieldSpans.ElementAt(4).Start, fieldSpans.ElementAt(4).Length).ToString(),
                    span.Slice(fieldSpans.ElementAt(5).Start, fieldSpans.ElementAt(5).Length).ToString(),
                    span.Slice(fieldSpans.ElementAt(6).Start, fieldSpans.ElementAt(6).Length).ToString(),
                    span.Slice(fieldSpans.ElementAt(7).Start, fieldSpans.ElementAt(7).Length).ToString(),
                    span.Slice(fieldSpans.ElementAt(8).Start, fieldSpans.ElementAt(8).Length).ToString(),
                    span.Slice(fieldSpans.ElementAt(9).Start, fieldSpans.ElementAt(9).Length).ToString(),
                    decimal.Parse(span.Slice(fieldSpans.ElementAt(10).Start, fieldSpans.ElementAt(10).Length)),
                    decimal.Parse(span.Slice(fieldSpans.ElementAt(11).Start, fieldSpans.ElementAt(11).Length)),
                    span.Slice(fieldSpans.ElementAt(12).Start, fieldSpans.ElementAt(12).Length).ToString(),
                    DateTime.Parse(span.Slice(fieldSpans.ElementAt(13).Start, fieldSpans.ElementAt(13).Length)));
            });
            var rowCount = 0;
            using var dataReader = new CsvObjectReader<Financial>(stream, profile, spanMapper);
            foreach(var human in dataReader.Read())
            { Console.WriteLine($"{rowCount++}: {human.AreaCode}");  }
            Assert.That(rowCount, Is.EqualTo(21));
        }
    }

    [Test]
    [TestCase("Ansi")]
    [TestCase("Utf16-BE")]
    [TestCase("Utf16-LE")]
    [TestCase("Utf8-BOM")]
    [TestCase("Utf8")]
    public void Read_FinancialWithSpanObjectBuilder_CorrectRowsColumns(string filename)
    {
        var profile = new CsvProfile('\t', '\"', "\r\n", true);

        using (var stream =
                Assembly.GetExecutingAssembly()
                    .GetManifestResourceStream($"{Assembly.GetExecutingAssembly().GetName().Name}.Resources.{filename}.csv")
                    ?? throw new FileNotFoundException()
        )
        {
            var objBuilder = new SpanObjectBuilder<Financial>();
            var spanMapper = new SpanMapper<Financial>(objBuilder.Instantiate);
            var rowCount = 0;
            using var dataReader = new CsvObjectReader<Financial>(stream, profile, spanMapper);
            foreach (var human in dataReader.Read())
            { rowCount++; }
            Assert.That(rowCount, Is.EqualTo(21));
        }
    }
}
