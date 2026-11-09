using System.Runtime.ExceptionServices;
using Apache.Arrow;
using Apache.Arrow.Types;
using PocketCsvReader.Json;

namespace PocketCsvReader.Arrow.ColumnarJson;

internal static class IntegrationBatch
{
    public static RecordBatch Parse(ReadOnlySpan<char> json, Schema schema, JsonFieldBuffer scratch, CancellationToken token)
    {
        var (length, columns) = ReadLayout(json, scratch);
        var arrays = new IArrowArray[schema.FieldsList.Count];
        var items = JsonRecordParser.Enumerate(json[columns]);
        try
        {
            for (var index = 0; index < arrays.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                if (!items.MoveNext())
                    throw new InvalidDataException("The Arrow batch is missing columns from its schema.");
                arrays[index] = ParseColumn(items.Value, schema.FieldsList[index], length, scratch, token);
            }

            if (items.MoveNext())
                throw new InvalidDataException("The Arrow batch has more columns than its schema.");
            return new RecordBatch(schema, arrays, length);
        }
        catch
        {
            foreach (var array in arrays)
                array?.Dispose();
            throw;
        }
    }

    private static (int Length, Range Columns) ReadLayout(ReadOnlySpan<char> json, JsonFieldBuffer scratch)
    {
        var properties = JsonRecordParser.Enumerate(json, properties: true);
        var count = ReadOnlySpan<char>.Empty;
        Range? columns = null;
        while (properties.MoveNext())
        {
            switch (JsonValues.Text(properties.Name, scratch))
            {
                case "count":
                    SetOnce(ref count, properties.Value, "count");
                    break;
                case "columns" when columns is null:
                    columns = properties.ValueStart..(properties.ValueStart + properties.Value.Length);
                    break;
                case "columns":
                    throw new InvalidDataException("Duplicate Arrow property 'columns'.");
            }
        }

        return (JsonValues.Count(count), columns ?? throw new InvalidDataException("Missing Arrow property 'columns'."));
    }

    public static async ValueTask<IArrowRecord> ReadLazyAsync(
        ReadOnlyMemory<char> json, Schema schema, JsonFieldBuffer scratch, bool asynchronous, CancellationToken token)
    {
        var (length, columns) = ReadLayout(json.Span, scratch);
        var frames = GetColumnFrames(json.Span, columns, schema.FieldsList.Count, token);
        var store = new RawColumnStore(asynchronous, frames.Length, ReadStoredColumn);
        try
        {
            for (var index = 0; index < frames.Length; index++)
            {
                store.MarkColumn(index);
                await store.WriteAsync(json[frames[index]], token).ConfigureAwait(false);
                await store.FlushAsync(token).ConfigureAwait(false);
            }

            store.CompleteWriting();
            return new LazyJsonArrowRecord(schema, length, store);
        }
        catch
        {
            store.Dispose();
            throw;
        }
    }

    public static IArrowRecord ReadLazyFile(
        ReadOnlySpan<char> json, Schema schema, JsonFieldBuffer scratch, SharedJsonFile file, long batchOffset, CancellationToken token)
    {
        var (length, columns) = ReadLayout(json, scratch);
        var frames = GetColumnFrames(json, columns, schema.FieldsList.Count, token);
        var indexed = new IndexedColumn[frames.Length];
        var previousEnd = 0;
        var offset = batchOffset;
        for (var index = 0; index < indexed.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            var start = frames[index].Start.Value;
            var end = frames[index].End.Value;
            offset += file.Encoding.GetByteCount(json[previousEnd..start]);
            var column = json[start..end];
            indexed[index] = IndexColumn(column, schema.FieldsList[index], length, scratch, file, offset, token);
            offset += file.Encoding.GetByteCount(column);
            previousEnd = end;
        }

        return LazyJsonArrowRecord.FromFile(schema, length, file, ReadColumn);

        IArrowArray ReadColumn(int index, Field field, int count, JsonFieldBuffer buffer)
        {
            var array = DecodeIndexed(indexed[index], file, field, count, buffer);
            indexed[index] = default;
            return array;
        }
    }

    private static IndexedColumn IndexColumn(
        ReadOnlySpan<char> json, Field field, int length, JsonFieldBuffer scratch,
        SharedJsonFile file, long offset, CancellationToken token)
    {
        try
        {
            var buffers = ReadColumnBuffers(json, field, length, scratch);
            if (buffers.Data is not { } data)
                return default;

            return new IndexedColumn(
                IndexBuffer(json, data, length, file, offset, token),
                IndexBuffer(json, buffers.Validity.GetValueOrDefault(), length, file, offset, token),
                buffers.Offsets is { } offsets ? IndexBuffer(json, offsets, length, file, offset, token) : null,
                null);
        }
        catch (InvalidDataException exception)
        {
            // Column layout errors still surface only when that column is requested.
            return new IndexedColumn(null, null, null, ExceptionDispatchInfo.Capture(exception));
        }
    }

    private static PackedJsonValues IndexBuffer(
        ReadOnlySpan<char> json, Range range, int length, SharedJsonFile file, long offset, CancellationToken token)
    {
        offset += file.Encoding.GetByteCount(json[..range.Start.Value]);
        var buffer = json[range];
        var index = new PackedJsonValues(offset, Math.Min(length, (buffer.Length - 1) / 2));
        var cells = JsonRecordParser.Enumerate(buffer);
        var previousEnd = 0;
        while (cells.MoveNext())
        {
            token.ThrowIfCancellationRequested();
            offset += file.Encoding.GetByteCount(buffer[previousEnd..cells.ValueStart]);
            var byteLength = file.Encoding.GetByteCount(cells.Value);
            index.Add(offset, byteLength, cells.Value.Length);
            offset += byteLength;
            previousEnd = cells.ValueStart + cells.Value.Length;
        }
        return index;
    }

    private static IArrowArray DecodeIndexed(
        IndexedColumn column, SharedJsonFile file, Field field, int length, JsonFieldBuffer scratch)
    {
        column.Error?.Throw();
        if (column.Data is not { } data)
            return new NullArray(length);

        IArrowArray array;
        try
        {
            var decoder = ColumnDecoder.Create(field, scratch, integration: true);
            var validity = column.Validity ?? throw new InvalidOperationException("The indexed column is missing its validity buffer.");
            decoder.Reserve(Math.Min(length, Math.Min(data.Count, validity.Count)));
            AppendIndexed(data, validity, file, decoder);
            if (column.Offsets is { } offsets)
                ValidateIndexedOffsets(offsets, file, decoder.Offsets);
            array = decoder.Build();
        }
        catch (Exception exception) when (ColumnDecoder.IsInvalidValue(exception))
        {
            throw ColumnDecoder.ColumnError(field, exception);
        }
        return VerifyLength(array, field, length);
    }

    private static void AppendIndexed(
        PackedJsonValues data, PackedJsonValues validity, SharedJsonFile file, ColumnDecoder decoder)
    {
        using var cells = data.Open(file);
        using var flags = validity.Open(file);
        while (cells.MoveNext())
        {
            if (!flags.MoveNext())
                throw new InvalidDataException("VALIDITY and DATA lengths differ.");
            decoder.Append(cells.Value, ColumnDecoder.DecodeValidity(flags.Value));
        }
        if (flags.MoveNext())
            throw new InvalidDataException("VALIDITY and DATA lengths differ.");
    }

    private static void ValidateIndexedOffsets(PackedJsonValues offsets, SharedJsonFile file, ReadOnlySpan<int> expected)
    {
        using var cells = offsets.Open(file);
        foreach (var offset in expected)
            ColumnDecoder.ValidateOffset(cells.MoveNext(), cells.Value, offset);
        if (cells.MoveNext())
            throw new InvalidDataException("Too many OFFSET entries.");
    }

    private readonly record struct IndexedColumn(
        PackedJsonValues? Data, PackedJsonValues? Validity, PackedJsonValues? Offsets, ExceptionDispatchInfo? Error);

    private static Range[] GetColumnFrames(ReadOnlySpan<char> json, Range columns, int count, CancellationToken token)
    {
        var frames = new Range[count];
        var items = JsonRecordParser.Enumerate(json[columns]);
        for (var index = 0; index < frames.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            if (!items.MoveNext())
                throw new InvalidDataException("The Arrow batch is missing columns from its schema.");
            var start = columns.Start.Value + items.ValueStart;
            frames[index] = start..(start + items.Value.Length);
        }

        if (items.MoveNext())
            throw new InvalidDataException("The Arrow batch has more columns than its schema.");
        return frames;
    }

    private static IArrowArray ReadStoredColumn(BufferedJsonInput input, Field field, int length, JsonFieldBuffer scratch)
    {
        var json = input.ReadValueAsync(default).GetAwaiter().GetResult();
        return ParseColumn(json.Span, field, length, scratch, default);
    }

    private static IArrowArray ParseColumn(
        ReadOnlySpan<char> json, Field field, int length, JsonFieldBuffer scratch, CancellationToken token)
    {
        var buffers = ReadColumnBuffers(json, field, length, scratch);
        if (buffers.Data is not { } data)
            return new NullArray(length);

        var cells = json[data];
        var validity = json[buffers.Validity.GetValueOrDefault()];
        var offsets = buffers.Offsets is { } offsetRange ? json[offsetRange] : default;
        // A declared count can be wrong. Even one-character entries need separators
        // and brackets, so the raw buffers bound the reservation before decoding.
        var capacity = Math.Min(length, (Math.Min(cells.Length, validity.Length) - 1) / 2);
        var array = ColumnDecoder.Decode(field, cells, validity, offsets, scratch, token, capacity);
        return VerifyLength(array, field, length);
    }

    private static ColumnBuffers ReadColumnBuffers(
        ReadOnlySpan<char> json, Field field, int length, JsonFieldBuffer scratch)
    {
        var (name, count, buffers) = ReadColumnProperties(json, field, scratch);
        if (!JsonValues.NameEquals(json[name.GetValueOrDefault()], field.Name, scratch)
            || JsonValues.Count(json[count.GetValueOrDefault()]) != length)
            throw new InvalidDataException($"Arrow column '{field.Name}' does not match the declared name and row count.");

        buffers.Validate(field, length);
        return buffers;
    }

    private static (Range? Name, Range? Count, ColumnBuffers Buffers) ReadColumnProperties(
        ReadOnlySpan<char> json, Field field, JsonFieldBuffer scratch)
    {
        var properties = JsonRecordParser.Enumerate(json, properties: true);
        Range? name = null, count = null, data = null, validity = null, offsets = null;
        while (properties.MoveNext())
        {
            var property = JsonValues.Text(properties.Name, scratch);
            var range = properties.ValueStart..(properties.ValueStart + properties.Value.Length);
            switch (property)
            {
                case "name": SetOnce(ref name, range, property); break;
                case "count": SetOnce(ref count, range, property); break;
                case "DATA": SetOnce(ref data, range, property); break;
                case "VALIDITY": SetOnce(ref validity, range, property); break;
                case "OFFSET": SetOnce(ref offsets, range, property); break;
                case "children":
                    if (JsonRecordParser.Enumerate(properties.Value).MoveNext())
                        throw new InvalidDataException($"Unexpected children in column '{field.Name}'.");
                    break;
            }
        }

        return (name, count, new ColumnBuffers(data, validity, offsets));
    }

    private readonly record struct ColumnBuffers(Range? Data, Range? Validity, Range? Offsets)
    {
        public void Validate(Field field, int length)
        {
            if (field.DataType is NullType)
            {
                ValidateNull(field, length);
                return;
            }

            var expectsOffsets = field.DataType is StringType or BinaryType;
            if (Data is null || Validity is null || expectsOffsets != Offsets.HasValue)
                throw new InvalidDataException($"Missing or unexpected Arrow buffers for column '{field.Name}'.");
        }

        private void ValidateNull(Field field, int length)
        {
            if ((!field.IsNullable && length != 0) || Data is not null || Validity is not null || Offsets is not null)
                throw new InvalidDataException($"Invalid buffers for null column '{field.Name}'.");
        }
    }

    private static IArrowArray VerifyLength(IArrowArray array, Field field, int length)
    {
        if (array.Length == length)
            return array;
        array.Dispose();
        throw new InvalidDataException($"Arrow column '{field.Name}' does not contain {length} values.");
    }

    private static void SetOnce(ref Range? target, Range value, ReadOnlySpan<char> name)
    {
        if (target is not null)
            throw new InvalidDataException($"Duplicate Arrow property '{name.ToString()}'.");
        target = value;
    }

    private static void SetOnce(ref ReadOnlySpan<char> target, ReadOnlySpan<char> value, ReadOnlySpan<char> name)
    {
        if (!target.IsEmpty)
            throw new InvalidDataException($"Duplicate Arrow property '{name.ToString()}'.");
        target = value;
    }
}
