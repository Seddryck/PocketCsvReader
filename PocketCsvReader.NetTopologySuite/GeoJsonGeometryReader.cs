using System.Globalization;
using global::NetTopologySuite.Geometries;
using PocketCsvReader.Json;

namespace PocketCsvReader.NetTopologySuite;

internal sealed class GeoJsonGeometryReader(GeometryFactory factory)
{
    private delegate T ReadElement<T>(ReadOnlySpan<char> value, ref JsonFieldBuffer? scratch);

    internal Geometry Read(ReadOnlySpan<char> json)
    {
        JsonFieldBuffer? scratch = null;
        try
        {
            return ReadGeometry(json, ref scratch);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException)
        {
            throw new FormatException("Invalid GeoJSON geometry.", ex);
        }
    }

    private Geometry ReadGeometry(ReadOnlySpan<char> json, ref JsonFieldBuffer? scratch)
    {
        ReadOnlySpan<char> type = default, coordinates = default, geometries = default;
        var properties = JsonRecordParser.Enumerate(json, properties: true);
        while (properties.MoveNext())
        {
            switch (ReadString(properties.Name, ref scratch))
            {
                case "type":
                    type = AssignMember(type, properties.Value, "type");
                    break;
                case "coordinates":
                    coordinates = AssignMember(coordinates, properties.Value, "coordinates");
                    break;
                case "geometries":
                    geometries = AssignMember(geometries, properties.Value, "geometries");
                    break;
            }
        }

        var kind = ReadString(type, ref scratch);
        if (kind.SequenceEqual("GeometryCollection"))
            return factory.CreateGeometryCollection(ReadArray(geometries, ReadGeometry, ref scratch));

        return kind switch
        {
            "Point" => ReadPoint(coordinates, ref scratch),
            "LineString" => ReadLineString(coordinates, ref scratch),
            "Polygon" => ReadPolygon(coordinates, ref scratch),
            "MultiPoint" => factory.CreateMultiPoint(ReadArray(coordinates, ReadPoint, ref scratch)),
            "MultiLineString" => factory.CreateMultiLineString(ReadArray(coordinates, ReadLineString, ref scratch)),
            "MultiPolygon" => factory.CreateMultiPolygon(ReadArray(coordinates, ReadPolygon, ref scratch)),
            _ => throw new FormatException("Expected a GeoJSON geometry type.")
        };
    }

    private Point ReadPoint(ReadOnlySpan<char> json, ref JsonFieldBuffer? scratch)
    {
        var position = JsonRecordParser.Enumerate(json);
        return position.MoveNext() ? factory.CreatePoint(ReadPosition(ref position)) : factory.CreatePoint();
    }

    private LineString ReadLineString(ReadOnlySpan<char> json, ref JsonFieldBuffer? scratch)
        => factory.CreateLineString(ReadArray(json, ReadCoordinate, ref scratch));

    private Polygon ReadPolygon(ReadOnlySpan<char> json, ref JsonFieldBuffer? scratch)
    {
        var rings = JsonRecordParser.Enumerate(json);
        if (!rings.MoveNext())
            return factory.CreatePolygon();

        var shell = ReadRing(rings.Value, ref scratch);
        if (!rings.MoveNext())
            return factory.CreatePolygon(shell);

        var holes = new List<LinearRing>();
        do
            holes.Add(ReadRing(rings.Value, ref scratch));
        while (rings.MoveNext());
        return factory.CreatePolygon(shell, holes.ToArray());
    }

    private LinearRing ReadRing(ReadOnlySpan<char> json, ref JsonFieldBuffer? scratch)
        => factory.CreateLinearRing(ReadArray(json, ReadCoordinate, ref scratch));

    private static Coordinate ReadCoordinate(ReadOnlySpan<char> json, ref JsonFieldBuffer? scratch)
    {
        var position = JsonRecordParser.Enumerate(json);
        if (!position.MoveNext())
            throw new FormatException("Expected a GeoJSON position.");
        return ReadPosition(ref position);
    }

    private static Coordinate ReadPosition(ref JsonRecordParser.Elements position)
    {
        var x = ReadNumber(position.Value);
        if (!position.MoveNext())
            throw new FormatException("Expected a GeoJSON latitude.");

        var y = ReadNumber(position.Value);
        if (!position.MoveNext())
            return new Coordinate(x, y);

        var z = ReadNumber(position.Value);
        if (position.MoveNext())
            throw new FormatException("Expected a two- or three-dimensional GeoJSON position.");
        return new CoordinateZ(x, y, z);
    }

    private static double ReadNumber(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty || value[0] is not ('-' or (>= '0' and <= '9'))
            || !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            || !double.IsFinite(number))
            throw new FormatException("Expected a finite GeoJSON coordinate number.");
        return number;
    }

    private static T[] ReadArray<T>(ReadOnlySpan<char> json, ReadElement<T> read, ref JsonFieldBuffer? scratch)
    {
        var elements = JsonRecordParser.Enumerate(json);
        if (!elements.MoveNext())
            return [];

        var values = new List<T>();
        do
            values.Add(read(elements.Value, ref scratch));
        while (elements.MoveNext());
        return values.ToArray();
    }

    private static ReadOnlySpan<char> AssignMember(ReadOnlySpan<char> previous, ReadOnlySpan<char> value, string name)
        => previous.IsEmpty ? value : throw new FormatException($"Duplicate GeoJSON member '{name}'.");

    private static ReadOnlySpan<char> ReadString(ReadOnlySpan<char> quoted, ref JsonFieldBuffer? scratch)
    {
        if (quoted.IsEmpty || quoted[0] != '"')
            throw new FormatException("Expected a GeoJSON string.");
        var content = quoted[1..^1];
        return content.Contains('\\') ? JsonRecordParser.DecodeString(quoted, scratch ??= new()).AsSpan() : content;
    }
}
