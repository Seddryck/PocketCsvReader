using System.Text;
using global::NetTopologySuite.Geometries;
using NUnit.Framework;
using PocketCsvReader.Configuration;
using PocketCsvReader.Json.Configuration;

namespace PocketCsvReader.NetTopologySuite.Testing;

public class GeometryColumnTests
{
    private static IEnumerable<TestCaseData> Shapes()
    {
        yield return new(typeof(Point), "POINT (4 50)", """{"type":"Point","coordinates":[4,50]}""");
        yield return new(typeof(LineString), "LINESTRING (4 50, 5 51)",
            """{"type":"LineString","coordinates":[[4,50],[5,51]]}""");
        yield return new(typeof(Polygon), "POLYGON ((0 0, 4 0, 4 4, 0 4, 0 0), (1 1, 1 2, 2 2, 2 1, 1 1))",
            """{"type":"Polygon","coordinates":[[[0,0],[4,0],[4,4],[0,4],[0,0]],[[1,1],[1,2],[2,2],[2,1],[1,1]]]}""");
        yield return new(typeof(MultiPoint), "MULTIPOINT ((4 50), (5 51))",
            """{"type":"MultiPoint","coordinates":[[4,50],[5,51]]}""");
        yield return new(typeof(MultiLineString), "MULTILINESTRING ((4 50, 5 51))",
            """{"type":"MultiLineString","coordinates":[[[4,50],[5,51]]]}""");
        yield return new(typeof(MultiPolygon), "MULTIPOLYGON (((0 0, 4 0, 0 4, 0 0)))",
            """{"type":"MultiPolygon","coordinates":[[[[0,0],[4,0],[0,4],[0,0]]]]}""");
        yield return new(typeof(GeometryCollection), "GEOMETRYCOLLECTION (GEOMETRYCOLLECTION (POINT (4 50)))",
            """{"type":"GeometryCollection","geometries":[{"type":"GeometryCollection","geometries":[{"type":"Point","coordinates":[4,50]}]}]}""");
    }

    [TestCaseSource(nameof(Shapes))]
    public void BothFormatsReadEverySupportedShape(Type type, string wkt, string geoJson)
    {
        var csv = new CsvReaderBuilder()
            .WithDialect(d => d.WithLineTerminator("\n"))
            .WithParsers(p => p.UseNetTopologySuite(wktSrid: 3857))
            .WithSchema(s => s.Named()
                .WithCustomField(type, "wkt")
                .WithCustomField(type, "json", f => f.WithFormat("geojson")))
            .Build();
        using var rows = Open(csv, $"wkt,json\n{Escape(wkt)},{Escape(geoJson)}\n");
        Assert.That(rows.Read(), Is.True);
        var left = (Geometry)rows.GetValue(0);
        var right = (Geometry)rows.GetValue(1);

        Assert.That(left.GetType(), Is.EqualTo(type));
        Assert.That(right.GetType(), Is.EqualTo(type));
        Assert.That(left.SRID, Is.EqualTo(3857));
        Assert.That(right.SRID, Is.EqualTo(4326));
        Assert.That(right.EqualsExact(left), Is.True);
    }

    [TestCase("""{"location":{"type":"Point","coordinates":[4.35,50.85]}}""", "location", null)]
    [TestCase("""
        {"type":"FeatureCollection","features":[
            {"type":"Feature","geometry":{"type":"Point","coordinates":[4.35,50.85]},"properties":{}}
        ]}
        """, "geometry", "$.features[*]")]
    public void ReadsNestedGeoJsonPointFromJson(string input, string property, string? recordPath)
    {
        var json = new JsonReaderBuilder()
            .WithParsers(p => p.UseNetTopologySuite())
            .WithSchema(s => s.Named().WithCustomField<Point>(property, f => f.WithFormat("geojson")));
        if (recordPath is not null)
            json.WithRecordPath(recordPath);

        using var rows = json.Build().ToDataReader(new MemoryStream(Encoding.UTF8.GetBytes(input)));
        Assert.That(rows.Read(), Is.True);
        var point = rows.GetFieldValue<Point>(property);
        Assert.That((point.X, point.Y, point.SRID), Is.EqualTo((4.35, 50.85, 4326)));
        Assert.That(rows.GetValue(property), Is.EqualTo(point));
        Assert.That(rows.Read(), Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task MixedFormatsReadFromBothInputModesAndSurviveAdvancement(bool utf8)
    {
        var builder = new CsvReaderBuilder()
            .WithDialect(d => d.WithLineTerminator("\n"))
            .WithParsers(p => p.UseNetTopologySuite())
            .WithSchema(s => s.Named()
                .WithCustomField<Geometry>("wkt", f => f.WithFormat("wkt").WithSequence("origin", "POINT (0 0)"))
                .WithCustomField<Geometry>("json", f => f.WithFormat("GeoJSON")));
        if (utf8)
            builder.WithStreamInitialization(StreamInitializationOptions.Utf8);

        const string json = """{"coordinates":[4.35,50.85,42],"t\u0079pe":"Po\u0069nt","label":"é"}""";
        using var rows = Open(builder.Build(),
            $"wkt,json\norigin,{Escape(json)}\n{Escape("SRID=3857;POINT (6 7)")},{Escape("""{"type":"Point","coordinates":[2,3]}""")}\n");
        Assert.That(await rows.ReadAsync(), Is.True);
        var origin = (Point)rows.GetFieldValue<Geometry>(0);
        var stop = (Point)rows.GetValue(1);
        Assert.That(await rows.ReadAsync(), Is.True);
        var explicitSrid = (Point)rows.GetValue(0);
        var nextStop = (Point)rows.GetFieldValue<Geometry>(1);

        Assert.That((origin.X, origin.Y, origin.SRID), Is.EqualTo((0d, 0d, 0)));
        Assert.That((stop.X, stop.Y, stop.Z, stop.SRID), Is.EqualTo((4.35, 50.85, 42d, 4326)));
        Assert.That((explicitSrid.X, explicitSrid.Y, explicitSrid.SRID), Is.EqualTo((6d, 7d, 3857)));
        Assert.That((nextStop.X, nextStop.Y), Is.EqualTo((2d, 3d)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ExplicitRuntimeAndFieldParsersOverrideIntegration(bool registerIntegrationFirst)
    {
        var supplied = new GeometryFactory().CreatePoint(new Coordinate(7, 8));
        var field = new GeometryFactory().CreatePoint(new Coordinate(1, 2));
        var csv = new CsvReaderBuilder()
            .WithDialect(d => d.WithLineTerminator("\n"))
            .WithParsers(p =>
            {
                if (registerIntegrationFirst)
                    p.UseNetTopologySuite();
                p.WithParser<Point>((ReadOnlySpan<char> _) => supplied);
                return registerIntegrationFirst ? p : p.UseNetTopologySuite();
            })
            .WithSchema(s => s.Named()
                .WithCustomField<Point>("runtime", f => f.WithFormat("geojson"))
                .WithCustomField<Point>("field", f => f.WithFormat("wkt")
                    .WithParser<Point>((ReadOnlySpan<char> _) => field)))
            .Build();

        using var rows = Open(csv, "runtime,field\nignored,ignored\n");
        Assert.That(rows.Read(), Is.True);
        Assert.That(rows.GetFieldValue<Point>(0), Is.SameAs(supplied));
        Assert.That(rows.GetValue(0), Is.SameAs(supplied));
        Assert.That(rows.GetFieldValue<Point>(1), Is.SameAs(field));
    }

    [TestCase("wkt", "broken")]
    [TestCase("wkt", "SRID 3857;POINT (1 2)")]
    [TestCase("wkt", "SRID=99999999999;POINT (1 2)")]
    [TestCase("wkt", "LINESTRING (0 0, 1 1)")]
    [TestCase("geojson", """{"type":"Point","coordinates":["4",50]}""")]
    [TestCase("geojson", """{"type":"Point","coordinates":[4,null]}""")]
    [TestCase("geojson", """{"type":"Point","coordinates":[4,1e500]}""")]
    [TestCase("geojson", """{"type":"Point","coordinates":[4,50,1,2]}""")]
    [TestCase("geojson", """{"type":"Point","coordinates":[4,50]} {}""")]
    [TestCase("geojson", """{"type":"Point","t\u0079pe":"Point","coordinates":[4,50]}""")]
    public void InvalidValuesAndShapeMismatchesFail(string format, string input)
    {
        var csv = new CsvReaderBuilder()
            .WithDialect(d => d.WithLineTerminator("\n"))
            .WithParsers(p => p.UseNetTopologySuite())
            .WithSchema(s => s.Named().WithCustomField<Point>("shape", f => f.WithFormat(format)))
            .Build();
        using var rows = Open(csv, $"shape\n{Escape(input)}\n");
        Assert.That(rows.Read(), Is.True);
        Assert.That(rows.IsValid(0), Is.False);
        Assert.That(() => rows.GetFieldValue<Point>(0), Throws.TypeOf<FormatException>());
        Assert.That(() => rows.GetFieldValue<Geometry>(0), Throws.TypeOf<FormatException>());
        Assert.That(() => rows.GetValue(0), Throws.TypeOf<FormatException>());
    }

    [Test]
    public void NullSequenceRetainsStandardGetterSemantics()
    {
        var csv = new CsvReaderBuilder()
            .WithDialect(d => d.WithLineTerminator("\n"))
            .WithParsers(p => p.UseNetTopologySuite())
            .WithSchema(s => s.Named().WithCustomField<Point>("shape", f => f.WithSequence("NULL", null)))
            .Build();
        using var rows = Open(csv, "shape\nNULL\n");
        Assert.That(rows.Read(), Is.True);
        Assert.That(rows.IsDBNull(0), Is.True);
        Assert.That(rows.GetFieldValue<Point>(0), Is.Null);
        Assert.That(() => rows.GetValue(0), Throws.TypeOf<InvalidCastException>());
    }

    [Test]
    public void UnsupportedFormatFailsWhenColumnIsAccessed()
    {
        var csv = new CsvReaderBuilder()
            .WithDialect(d => d.WithLineTerminator("\n"))
            .WithParsers(p => p.UseNetTopologySuite())
            .WithSchema(s => s.Named().WithCustomField<Geometry>("shape", f => f.WithFormat("invalid")))
            .Build();
        using var rows = Open(csv, "shape\nPOINT (1 2)\n");
        Assert.That(rows.Read(), Is.True);
        Assert.That(() => rows.GetValue(0), Throws.TypeOf<ArgumentException>());
    }

    internal static CsvDataReader Open(CsvReader csv, string text)
        => csv.ToDataReader(new MemoryStream(Encoding.UTF8.GetBytes(text)));

    internal static string Escape(string text) => "\"" + text.Replace("\"", "\"\"") + "\"";
}
