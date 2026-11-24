namespace PocketCsvReader;

internal sealed class CsvColumnProjection
{
    private readonly string[]? _names;
    private readonly int[]? _ordinals;

    public CsvColumnProjection(string[] columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        if (columns.Length == 0 || columns.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Specify at least one non-blank column name.", nameof(columns));
        _names = (string[])columns.Clone();
    }

    public CsvColumnProjection(int[] ordinals)
    {
        ArgumentNullException.ThrowIfNull(ordinals);
        if (ordinals.Length == 0)
            throw new ArgumentException("Specify at least one source ordinal.", nameof(ordinals));
        if (ordinals.Any(ordinal => ordinal < 0))
            throw new ArgumentOutOfRangeException(nameof(ordinals), "Source ordinals must be non-negative.");
        _ordinals = (int[])ordinals.Clone();
    }

    public bool UsesNames => _names is not null;

    public int[] Bind(string[] headers)
    {
        if (_ordinals is { } ordinals)
        {
            foreach (var ordinal in ordinals)
            {
                if (ordinal >= headers.Length)
                    throw new InvalidDataException($"Projected source ordinal {ordinal} is outside the CSV source width of {headers.Length}.");
            }

            return ordinals;
        }

        var byName = new Dictionary<string, int>(headers.Length, StringComparer.Ordinal);
        for (var i = 0; i < headers.Length; i++)
        {
            if (!byName.TryAdd(headers[i], i))
                byName[headers[i]] = -1;
        }

        var names = _names!;
        var result = new int[names.Length];
        for (var i = 0; i < names.Length; i++)
        {
            var name = names[i];
            if (!byName.TryGetValue(name, out var ordinal))
                throw new InvalidDataException($"Projected column '{name}' was not found in the CSV header.");
            if (ordinal < 0)
                throw new InvalidDataException($"Projected column '{name}' is ambiguous in the CSV header. Use its source ordinal.");
            result[i] = ordinal;
        }

        return result;
    }
}
