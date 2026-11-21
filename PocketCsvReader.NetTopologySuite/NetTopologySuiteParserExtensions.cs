using global::NetTopologySuite;
using global::NetTopologySuite.Geometries;
using global::NetTopologySuite.IO;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.NetTopologySuite;

/// <summary>Registers opt-in parsers for NetTopologySuite geometry fields.</summary>
public static class NetTopologySuiteParserExtensions
{
    /// <summary>Enables WKT by default and GeoJSON for fields configured with the geojson format.</summary>
    /// <param name="options">The runtime parser configuration.</param>
    /// <param name="wktSrid">The default spatial reference ID for WKT input.</param>
    public static RuntimeParsersDescriptorBuilder UseNetTopologySuite(
        this RuntimeParsersDescriptorBuilder options, int wktSrid = 0)
    {
        ArgumentNullException.ThrowIfNull(options);
        var wkt = new WKTReader(new NtsGeometryServices(new PrecisionModel(), wktSrid));
        var geoJson = new GeoJsonGeometryReader(new GeometryFactory(new PrecisionModel(), 4326));

        Register<Geometry>(options, wkt, geoJson);
        Register<Point>(options, wkt, geoJson);
        Register<LineString>(options, wkt, geoJson);
        Register<Polygon>(options, wkt, geoJson);
        Register<MultiPoint>(options, wkt, geoJson);
        Register<MultiLineString>(options, wkt, geoJson);
        Register<MultiPolygon>(options, wkt, geoJson);
        Register<GeometryCollection>(options, wkt, geoJson);
        return options;
    }

    private static void Register<T>(RuntimeParsersDescriptorBuilder options, WKTReader wkt, GeoJsonGeometryReader geoJson)
        where T : Geometry
        => options.WithDefaultMemoryParser<T>(format => format switch
        {
            _ when format is null || format.Equals("wkt", StringComparison.OrdinalIgnoreCase)
                => memory => RequireType<T>(ReadWkt(wkt, memory)),
            _ when format.Equals("geojson", StringComparison.OrdinalIgnoreCase)
                => memory => RequireType<T>(geoJson.Read(memory.Span)),
            _ => throw new ArgumentException($"Unsupported geometry format '{format}'. Expected wkt or geojson.")
        });

    private static Geometry ReadWkt(WKTReader reader, ReadOnlyMemory<char> memory)
    {
        using var text = new MemoryTextReader(memory);
        try
        {
            return reader.Read(text) ?? throw new FormatException("Invalid WKT geometry.");
        }
        catch (Exception ex) when (ex is ParseException or ArgumentException or OverflowException)
        {
            throw new FormatException("Invalid WKT geometry.", ex);
        }
    }

    private static T RequireType<T>(Geometry geometry)
        where T : Geometry
        => geometry as T ?? throw new FormatException($"Expected {typeof(T).Name}, but found {geometry.GeometryType}.");
}
