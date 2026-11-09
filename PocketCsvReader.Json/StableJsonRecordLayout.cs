namespace PocketCsvReader.Json;

// One layout per record source. The two metadata trees alternate so a rejected
// record cannot overwrite the preceding row's fields. Value text stays borrowed.
internal sealed class StableJsonRecordLayout(
    char[] whitespaces,
    IReadOnlyList<string>? properties,
    bool orderedProjection,
    bool allowComments = false,
    bool allowTrailingCommas = false)
{
    private readonly FieldSpan[]? _projectedFields = properties is null ? null : new FieldSpan[properties.Count];
    private StableJsonShape? _shape;
    private FieldSpan[]? _fields;
    private FieldSpan[]? _scratchFields;
    private int[]? _projectedOrdinals;

    public FieldSpan[] Parse(ReadOnlySpan<char> record)
    {
        if (_shape is null)
        {
            if (record.IsEmpty || record[0] != '{')
                throw new InvalidDataException("A stable-layout JSON record must be an object.");
            var fields = JsonRecordParser.Parse(record, whitespaces, allowComments, allowTrailingCommas);
            if (properties is not null)
                _projectedOrdinals = ResolveProjectedOrdinals(record, fields, properties, orderedProjection);
            _shape = JsonRecordParser.CreateStableShape(record, fields);
            _fields = fields;
        }
        else
        {
            _scratchFields ??= _shape.CreateFieldBuffer();
            JsonRecordParser.ParseStableObjectValues(
                record, whitespaces, _shape, _scratchFields, allowComments, allowTrailingCommas);
            (_fields, _scratchFields) = (_scratchFields, _fields);
        }

        if (_projectedOrdinals is null)
            return _fields!;

        for (var i = 0; i < _projectedOrdinals.Length; i++)
            _projectedFields![i] = _fields![_projectedOrdinals[i]];
        return _projectedFields!;
    }

    private static int[] ResolveProjectedOrdinals(
        ReadOnlySpan<char> record, FieldSpan[] fields, IReadOnlyList<string> properties, bool ordered)
    {
        var ordinals = new int[properties.Count];
        var previousOrdinal = -1;
        for (var i = 0; i < ordinals.Length; i++)
        {
            var property = properties[i];
            var ordinal = LabeledColumnProjection.FindFirst(record, fields, property);
            if (ordinal < 0)
                throw new InvalidDataException($"Projected property '{property}' was not found.");
            if (ordered && ordinal <= previousOrdinal)
                throw new InvalidDataException($"Projected property '{property}' was not found in the configured order.");
            ordinals[i] = ordinal;
            previousOrdinal = ordinal;
        }
        return ordinals;
    }
}
