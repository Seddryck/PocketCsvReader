namespace PocketCsvReader.Json.Configuration;

internal static class JsonProjection
{
    public static IReadOnlyList<string>? Create(IEnumerable<string>? projectedProperties, bool orderedProjection)
    {
        var projection = projectedProperties?.ToArray();
        if (projection is { Length: 0 })
            throw new ArgumentException("At least one projected property must be configured.", nameof(projectedProperties));
        if (orderedProjection && projection is null)
            throw new ArgumentException("An ordered projection requires projected properties.", nameof(orderedProjection));
        if (projection is null)
            return null;
        ValidateNames(projection, nameof(projectedProperties));
        return Array.AsReadOnly(projection);
    }

    private static void ValidateNames(string[] properties, string parameterName)
    {
        if (properties.Any(string.IsNullOrEmpty))
            throw new ArgumentException("Projected property names cannot be null or empty.", parameterName);
        if (properties.Distinct(StringComparer.Ordinal).Count() != properties.Length)
            throw new ArgumentException("Projected properties must be unique.", parameterName);
    }
}
