using System.Buffers;
using System.Globalization;

namespace PocketCsvReader;

public partial class CsvDataReader
{
    private string[]? _sourceHeaders;
    private string[]? _sourceFields;
    private string[]? _projectedFields;
    private int[]? _projectedOrdinals;
    private bool _usingSourceFields;
    private string? _rowNumberText;

    private int RowNumberWidth => HasRowNumber ? 1 : 0;
    private bool HasVirtualRowNumber => HasRowNumber && _projectedOrdinals is not null;
    private int? SourceFieldCount => CurrentFieldSpans?.Length + (HasVirtualRowNumber ? 1 : 0);

    // Filters and index lookups use source ordinals (including the generated row number,
    // if configured). The physical field spans remain in source order for all readers.
    private protected override int GetFieldCacheOrdinal(int i)
    {
        if (_projectedOrdinals is null || _usingSourceFields || (HasRowNumber && i == 0))
            return i;

        var projectedOrdinal = i - RowNumberWidth;
        if ((uint)projectedOrdinal >= (uint)_projectedOrdinals.Length)
            throw new ArgumentOutOfRangeException(nameof(i));
        return _projectedOrdinals[projectedOrdinal] + RowNumberWidth;
    }

    private protected override void EnterSourceFields()
    {
        if (_sourceFields is null)
            return;
        Fields = _sourceFields;
        _usingSourceFields = true;
    }

    private protected override void ExitSourceFields()
    {
        if (_projectedFields is null)
            return;
        Fields = _projectedFields;
        _usingSourceFields = false;
    }

    private int GetPhysicalOrdinal(int i)
        => GetFieldCacheOrdinal(i) - (HasVirtualRowNumber ? 1 : 0);

    private protected override FieldSpan? GetRecordField(int i)
    {
        if (_projectedOrdinals is null)
            return base.GetRecordField(i);
        if (HasRowNumber && i == 0 && CurrentFieldSpans is { Length: > 0 })
            return default(FieldSpan);
        var ordinal = GetPhysicalOrdinal(i);
        return CurrentFieldSpans is { } fields && (uint)ordinal < (uint)fields.Length ? fields[ordinal] : null;
    }

    private ReadOnlyMemory<char> GetCsvFieldMemory(int i)
        => HasVirtualRowNumber && i == 0
            ? (_rowNumberText ??= CurrentRowNumber.ToString(CultureInfo.InvariantCulture)).AsMemory()
            : GetCurrentFieldMemory(GetPhysicalOrdinal(i));

    private bool TryGetCsvUtf8(int i, out ReadOnlyMemory<byte> bytes, out bool isEscaped)
    {
        // Derived readers can override the visible field count.
        if (_projectedOrdinals is null && !HasRowNumber && GetType() == typeof(CsvDataReader))
            return TryGetCurrentUtf8(i, out bytes, out isEscaped);

        if (CurrentFieldSpans is not null && (uint)i < (uint)FieldCount)
            return TryGetCurrentUtf8(GetPhysicalOrdinal(i), out bytes, out isEscaped);

        bytes = default;
        isEscaped = false;
        return false;
    }

    private T MapProjectedRecord<T>(SpanMapper<T> mapper)
    {
        // A public span mapper gets independently retainable metadata and one
        // character span. Assemble that span from the selected slices only.
        var buffer = new ArrayBufferWriter<char>();
        var fields = new FieldSpan[FieldCount];
        for (var i = 0; i < fields.Length; i++)
        {
            var field = GetRecordField(i);
            var text = field.HasValue ? GetCsvFieldMemory(i).Span : GetValueOrThrow(i).Value;
            var offset = buffer.WrittenCount;
            buffer.Write(text);
            fields[i] = (field ?? default) with
            {
                Value = (field?.Value ?? default) with { Start = offset, Length = text.Length },
                Children = field?.Children?.Select(child => MoveChild(child, offset - field.Value.Value.Start)).ToArray()
            };
        }

        return mapper(buffer.WrittenSpan, fields);

        static FieldSpan MoveChild(FieldSpan field, int offset)
        {
            var value = field.Value;
            return field with
            {
                Value = value with { Start = value.IsStarted ? value.Start + offset : 0 },
                Children = field.Children?.Select(child => MoveChild(child, offset)).ToArray()
            };
        }
    }
}
