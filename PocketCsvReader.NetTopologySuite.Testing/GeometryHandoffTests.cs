using global::NetTopologySuite.Geometries;
using global::NetTopologySuite.IO;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.NetTopologySuite.Testing;

public class GeometryHandoffTests
{
    [Test]
    public void WktTextReaderBorrowsOnlyTheFieldSlice()
    {
        var record = "before,POINT (1 2),after".ToCharArray();
        using var input = new MemoryTextReader(record.AsMemory(7, 11));
        record[14] = '9';

        var point = (Point)new WKTReader().Read(input);
        Assert.That((point.X, point.Y), Is.EqualTo((9d, 2d)));
        Assert.That(input.Peek(), Is.EqualTo(-1));
        Assert.That(input.Read(), Is.EqualTo(-1));
    }

    [TestCase("wkt", false, false)]
    [TestCase("wkt", true, false)]
    [TestCase("geojson", false, false)]
    [TestCase("geojson", true, false)]
    [TestCase("geojson", false, true)]
    [TestCase("geojson", true, true)]
    public void NeitherHandoffAllocatesAnExtraFullCellCopy(string format, bool utf8, bool escapeCsv)
    {
        var builder = new CsvReaderBuilder()
            .WithDialect(d => d.WithLineTerminator("\n").WithQuoteChar(escapeCsv ? '"' : '\''))
            .WithParsers(p => p.UseNetTopologySuite())
            .WithSchema(s => s.Named().WithCustomField<Geometry>("shape", f => f.WithFormat(format)));
        if (utf8)
            builder.WithStreamInitialization(StreamInitializationOptions.Utf8);

        var csv = builder.Build();
        const int paddingLength = 32 * 1024;
        string shortText = GeometryText(format, "");
        string longText = GeometryText(format, new string(' ', paddingLength));
        using var shortRows = GeometryColumnTests.Open(csv, Cell(shortText, escapeCsv));
        using var longRows = GeometryColumnTests.Open(csv, Cell(longText, escapeCsv));
        Assert.That(shortRows.Read(), Is.True);
        Assert.That(longRows.Read(), Is.True); // The large cell crosses the default CSV buffer boundary.

        // Warm parser bindings, tokenizer/JSON paths, and any pooled UTF-8 decoding before measuring.
        _ = Measure(shortRows);
        _ = Measure(longRows);
        long small = Measure(shortRows);
        long large = Measure(longRows);
        TestContext.WriteLine($"{format}, UTF-8={utf8}, escaped={escapeCsv}: {small} → {large} bytes per read");

        // NTS allocates for whitespace tokenization. Subtract a direct Read(TextReader)
        // baseline where the input string already exists before measurement.
        long tokenizerGrowth = format == "wkt" ? MeasureWkt(longText) - MeasureWkt(shortText) : 0;
        long unescapeGrowth = escapeCsv ? paddingLength * sizeof(char) : 0;
        long extra = large - small - tokenizerGrowth - unescapeGrowth;
        TestContext.WriteLine($"Growth from tokenizer: {tokenizerGrowth}. Growth beyond tokenizer and unescaping: {extra}.");

        // An added full-cell string or array would add at least 64 KiB per padded value.
        const int margin = 8 * 1024;
        Assert.That(extra, Is.InRange(-margin, margin));
    }

    private static string GeometryText(string format, string padding)
        => format == "wkt"
            ? $"POINT (4 {padding}50)"
            : $"{{\"type\":\"Point\",\"coordinates\":[4,{padding}50]}}";

    private static string Cell(string value, bool escapeCsv)
        => escapeCsv ? $"shape\n{GeometryColumnTests.Escape(value)}\n" : $"shape\n'{value}'\n";

    private static long MeasureWkt(string text)
    {
        var reader = new WKTReader();
        using (var warmup = new StringReader(text))
            _ = reader.Read(warmup);

        const int iterations = 16;
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++)
        {
            using var input = new StringReader(text);
            _ = reader.Read(input);
        }
        return (GC.GetAllocatedBytesForCurrentThread() - start) / iterations;
    }

    private static long Measure(CsvDataReader rows)
    {
        const int iterations = 16;
        Geometry? result = null;
        long start = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < iterations; i++)
        {
            result = rows.GetFieldValue<Geometry>(0);
            result = (Geometry)rows.GetValue(0);
        }
        long bytes = GC.GetAllocatedBytesForCurrentThread() - start;
        Assert.That(((Point)result!).Y, Is.EqualTo(50));
        return bytes / (iterations * 2);
    }
}
