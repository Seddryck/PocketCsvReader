namespace PocketCsvReader.Configuration;

/// <summary>Configures the output columns of a labeled reader in declaration order.</summary>
public sealed class ColumnProjectionBuilder
{
    private readonly List<ProjectedColumn> _columns = [];
    private readonly HashSet<string> _names = new(StringComparer.Ordinal);

    /// <summary>Selects a source key and optionally gives it a different output name.</summary>
    public ColumnProjectionBuilder Column(string key, string? alias = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        if (alias is not null)
            ArgumentException.ThrowIfNullOrEmpty(alias);
        var name = alias ?? key;
        if (!_names.Add(name))
            throw new ArgumentException($"Output column '{name}' is already projected.", nameof(alias));

        _columns.Add(new ProjectedColumn(key, name));
        return this;
    }

    internal ProjectedColumn[] Build()
    {
        if (_columns.Count == 0)
            throw new InvalidOperationException("At least one projected column must be configured.");
        return [.. _columns];
    }
}

internal readonly record struct ProjectedColumn(string Key, string Name);
