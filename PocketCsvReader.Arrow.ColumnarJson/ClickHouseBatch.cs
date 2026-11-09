using Apache.Arrow;
using PocketCsvReader.Json;

namespace PocketCsvReader.Arrow.ColumnarJson;

internal static class ClickHouseBatch
{
    public static async ValueTask<RecordBatch> ReadAsync(
        BufferedJsonInput input, Schema schema, JsonFieldBuffer scratch, CancellationToken token)
    {
        var columns = new IArrowArray[schema.FieldsList.Count];
        try
        {
            var length = await ReadColumnsAsync(input, schema, scratch, ReadColumn, token).ConfigureAwait(false);
            return new RecordBatch(schema, columns, length);
        }
        catch
        {
            foreach (var column in columns)
                column?.Dispose();
            throw;
        }

        async ValueTask<int> ReadColumn(int index, Field field)
        {
            var array = await ReadColumnAsync(input, field, scratch, token).ConfigureAwait(false);
            columns[index] = array;
            return array.Length;
        }
    }

    public static async ValueTask<IArrowRecord> ReadLazyAsync(
        BufferedJsonInput input, Schema schema, JsonFieldBuffer scratch, bool asynchronous, CancellationToken token)
    {
        var store = new RawColumnStore(asynchronous, schema.FieldsList.Count, ReadStoredColumn);
        try
        {
            var length = await ReadColumnsAsync(input, schema, scratch, WriteColumn, token).ConfigureAwait(false);
            store.CompleteWriting();
            return new LazyJsonArrowRecord(schema, length, store);
        }
        catch
        {
            store.Dispose();
            throw;
        }

        async ValueTask<int> WriteColumn(int index, Field field)
        {
            store.MarkColumn(index);
            try
            {
                return await WriteRawColumnAsync(input, store, token).ConfigureAwait(false);
            }
            catch (Exception exception) when (ColumnDecoder.IsInvalidValue(exception))
            {
                throw ColumnDecoder.ColumnError(field, exception);
            }
        }
    }

    public static async ValueTask<IArrowRecord> ReadLazyFileAsync(
        BufferedJsonInput input, Schema schema, JsonFieldBuffer scratch, SharedJsonFile file, CancellationToken token)
    {
        var columns = new PackedJsonValues?[schema.FieldsList.Count];
        int? rowCount = null;
        var length = await ReadColumnsAsync(input, schema, scratch, IndexColumn, token).ConfigureAwait(false);
        return LazyJsonArrowRecord.FromFile(schema, length, file, ReadColumn);

        async ValueTask<int> IndexColumn(int index, Field field)
        {
            var values = new PackedJsonValues(input.BytePosition, rowCount.GetValueOrDefault());
            try
            {
                var count = await ReadCellsAsync(input, IndexCell, token).ConfigureAwait(false);
                columns[index] = values;
                rowCount ??= count;
                return count;
            }
            catch (Exception exception) when (ColumnDecoder.IsInvalidValue(exception))
            {
                throw ColumnDecoder.ColumnError(field, exception);
            }

            ValueTask IndexCell(ReadOnlyMemory<char> cell)
            {
                values.Add(input.LastValueByteStart, input.LastValueByteLength, cell.Length);
                return ValueTask.CompletedTask;
            }
        }

        IArrowArray ReadColumn(int index, Field field, int count, JsonFieldBuffer buffer)
        {
            var values = columns[index] ?? throw new InvalidOperationException("The column index is no longer available.");
            var array = DecodeIndexed(values, file, field, count, buffer);
            columns[index] = null;
            return array;
        }
    }

    private static IArrowArray DecodeIndexed(
        PackedJsonValues index, SharedJsonFile file, Field field, int count, JsonFieldBuffer scratch)
    {
        try
        {
            var decoder = ColumnDecoder.Create(field, scratch);
            decoder.Reserve(count);
            using var cells = index.Open(file);
            while (cells.MoveNext())
                decoder.Append(cells.Value);
            return decoder.Build();
        }
        catch (Exception exception) when (ColumnDecoder.IsInvalidValue(exception))
        {
            throw ColumnDecoder.ColumnError(field, exception);
        }
    }

    private static IArrowArray ReadStoredColumn(BufferedJsonInput input, Field field, int length, JsonFieldBuffer scratch)
    {
        var array = ReadColumnAsync(input, field, scratch, default, validate: false, capacity: length).GetAwaiter().GetResult();
        if (array.Length == length)
            return array;
        array.Dispose();
        throw new InvalidDataException($"Stored ClickHouse column '{field.Name}' does not contain {length} values.");
    }

    private static async ValueTask<int> ReadColumnsAsync(
        BufferedJsonInput input, Schema schema, JsonFieldBuffer scratch,
        Func<int, Field, ValueTask<int>> readColumn, CancellationToken token)
    {
        var fields = schema.FieldsList;
        var indices = IndexFields(fields);
        var seen = new bool[fields.Count];
        int? rows = null;
        var subsequent = false;

        await input.ExpectAsync('{', token).ConfigureAwait(false);
        while (await input.MoveNextAsync('}', subsequent, token).ConfigureAwait(false))
        {
            subsequent = true;
            var name = await input.ReadPropertyNameAsync(scratch, token).ConfigureAwait(false);
            var index = FindColumn(indices, seen, name);
            var length = await readColumn(index, fields[index]).ConfigureAwait(false);
            seen[index] = true;
            rows ??= length;
            if (length != rows)
                throw new InvalidDataException($"Column '{name}' contains {length} rows. Expected {rows}.");
        }

        RequireAllColumns(fields, seen);
        return rows ?? 0;
    }

    private static async ValueTask<IArrowArray> ReadColumnAsync(
        BufferedJsonInput input, Field field, JsonFieldBuffer scratch, CancellationToken token,
        bool validate = true, int capacity = 0)
    {
        try
        {
            var decoder = ColumnDecoder.Create(field, scratch);
            decoder.Reserve(capacity);
            await ReadCellsAsync(input, AppendCell, token, validate).ConfigureAwait(false);
            return decoder.Build();

            ValueTask AppendCell(ReadOnlyMemory<char> cell)
            {
                decoder.Append(cell.Span);
                return ValueTask.CompletedTask;
            }
        }
        catch (Exception exception) when (ColumnDecoder.IsInvalidValue(exception))
        {
            throw ColumnDecoder.ColumnError(field, exception);
        }
    }

    private static async ValueTask<int> WriteRawColumnAsync(
        BufferedJsonInput input, RawColumnStore store, CancellationToken token)
    {
        var first = true;
        await store.WriteAsync("[".AsMemory(), token).ConfigureAwait(false);
        var count = await ReadCellsAsync(input, WriteCellAsync, token).ConfigureAwait(false);
        await store.WriteAsync("]".AsMemory(), token).ConfigureAwait(false);
        await store.FlushAsync(token).ConfigureAwait(false);
        return count;

        async ValueTask WriteCellAsync(ReadOnlyMemory<char> cell)
        {
            if (!first)
                await store.WriteAsync(",".AsMemory(), token).ConfigureAwait(false);
            first = false;
            await store.WriteAsync(cell, token).ConfigureAwait(false);
        }
    }

    private static async ValueTask<int> ReadCellsAsync(
        BufferedJsonInput input, Func<ReadOnlyMemory<char>, ValueTask> consume, CancellationToken token, bool validate = true)
    {
        await input.ExpectAsync('[', token).ConfigureAwait(false);
        var count = 0;
        while (await input.MoveNextAsync(']', count != 0, token).ConfigureAwait(false))
        {
            var cell = await input.ReadValueAsync(token).ConfigureAwait(false);
            if (validate)
                JsonRecordParser.ValidateValue(cell.Span);
            await consume(cell).ConfigureAwait(false);
            count = checked(count + 1);
        }

        return count;
    }

    private static int FindColumn(Dictionary<string, int> indices, bool[] seen, string name)
    {
        if (!indices.TryGetValue(name, out var index))
            throw new InvalidDataException($"Column '{name}' is absent from the Arrow schema.");
        if (seen[index])
            throw new InvalidDataException($"Duplicate ClickHouse column '{name}'.");
        return index;
    }

    private static Dictionary<string, int> IndexFields(IReadOnlyList<Field> fields)
    {
        var indices = new Dictionary<string, int>(fields.Count, StringComparer.Ordinal);
        for (var index = 0; index < fields.Count; index++)
        {
            if (!indices.TryAdd(fields[index].Name, index))
                throw new InvalidDataException($"Duplicate Arrow schema field '{fields[index].Name}'.");
        }
        return indices;
    }

    private static void RequireAllColumns(IReadOnlyList<Field> fields, bool[] seen)
    {
        for (var index = 0; index < seen.Length; index++)
        {
            if (!seen[index])
                throw new InvalidDataException($"Missing ClickHouse column '{fields[index].Name}'.");
        }
    }
}
