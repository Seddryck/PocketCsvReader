namespace PocketCsvReader;

internal sealed class LabelRowShape
{
    public static LabelRowShape Empty { get; } = new([], new(StringComparer.Ordinal));

    public string[] Labels { get; }
    private Dictionary<string, int> Ordinals { get; }

    public LabelRowShape(string[] labels, Dictionary<string, int> ordinals)
    {
        Labels = labels;
        Ordinals = ordinals;
    }

    public bool TryGetOrdinal(string label, out int ordinal)
        => Ordinals.TryGetValue(label, out ordinal);
}

internal sealed class LabelRowShapeCache
{
    private const int MaxCachedShapes = 64;
    private readonly Dictionary<int, List<LabelRowShape>> _shapesByFieldCount = [];
    private int _cachedShapeCount;

    public LabelRowShape Resolve(ReadOnlyMemory<char> record, FieldSpan[] fields)
    {
        if (_shapesByFieldCount.TryGetValue(fields.Length, out var candidates))
        {
            foreach (var candidate in candidates)
            {
                if (Matches(record.Span, fields, candidate.Labels))
                    return candidate;
            }
        }

        var shape = Create(record.Span, fields);
        if (_cachedShapeCount >= MaxCachedShapes)
            return shape;

        (candidates ??= []).Add(shape);
        _shapesByFieldCount[fields.Length] = candidates;
        _cachedShapeCount++;
        return shape;
    }

    private static bool Matches(ReadOnlySpan<char> record, FieldSpan[] fields, string[] labels)
    {
        for (var index = 0; index < fields.Length; index++)
        {
            var field = fields[index];
            if (field.DecodedLabel is not null)
            {
                if (!field.DecodedLabel.Equals(labels[index], StringComparison.Ordinal))
                    return false;
            }
            else if (!record.Slice(field.Label.Start, field.Label.Length).SequenceEqual(labels[index]))
                return false;
        }

        return true;
    }

    private static LabelRowShape Create(ReadOnlySpan<char> record, FieldSpan[] fields)
    {
        if (fields.Length == 0)
            return LabelRowShape.Empty;

        var labels = new string[fields.Length];
        var ordinals = new Dictionary<string, int>(fields.Length, StringComparer.Ordinal);
        for (var index = 0; index < fields.Length; index++)
        {
            var field = fields[index];
            var label = field.DecodedLabel
                ?? record.Slice(field.Label.Start, field.Label.Length).ToString();
            labels[index] = label;
            ordinals.TryAdd(label, index);
        }

        return new(labels, ordinals);
    }
}
