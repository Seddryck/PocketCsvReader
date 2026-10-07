namespace PocketCsvReader.Json.Configuration;

public sealed class JsonProjectionBuilder
{
    private readonly List<string> _properties = [];

    public JsonProjectionBuilder Property(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (_properties.Contains(name, StringComparer.Ordinal))
            throw new ArgumentException($"Property '{name}' is already projected.", nameof(name));

        _properties.Add(name);
        return this;
    }

    internal string[] Build()
    {
        if (_properties.Count == 0)
            throw new InvalidOperationException("At least one projected property must be configured.");
        return [.. _properties];
    }
}
