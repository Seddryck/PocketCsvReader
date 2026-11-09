using PocketCsvReader.Configuration;

namespace PocketCsvReader;

internal sealed class LabeledColumnProjection(ProjectedColumn[] columns)
{
    private readonly int[] _sourceOrdinals = new int[columns.Length];
    private readonly FieldSpan[] _output = new FieldSpan[columns.Length];

    public void Resolve(ReadOnlySpan<char> record, ReadOnlySpan<FieldSpan> fields)
    {
        for (var i = 0; i < columns.Length; i++)
            _sourceOrdinals[i] = FindFirst(record, fields, columns[i].Key);
    }

    public FieldSpan[] Apply(ReadOnlySpan<FieldSpan> fields)
    {
        for (var i = 0; i < columns.Length; i++)
        {
            var sourceOrdinal = _sourceOrdinals[i];
            var field = sourceOrdinal < 0
                ? new FieldSpan(new SpanInfo(0, 0, IsNull: true), default)
                : fields[sourceOrdinal];
            _output[i] = field with { Label = default, DecodedLabel = columns[i].Name };
        }
        return _output;
    }

    internal static int FindFirst(ReadOnlySpan<char> record, ReadOnlySpan<FieldSpan> fields, string key)
    {
        for (var i = 0; i < fields.Length; i++)
        {
            ref readonly var field = ref fields[i];
            if (field.DecodedLabel is { } decoded
                ? decoded.Equals(key, StringComparison.Ordinal)
                : record.Slice(field.Label.Start, field.Label.Length).SequenceEqual(key))
                return i;
        }
        return -1;
    }
}
