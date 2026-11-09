using Apache.Arrow;
using PocketCsvReader.Json;

namespace PocketCsvReader.Arrow.ColumnarJson;

internal sealed class ClickHouseMetadataInput(BufferedJsonInput input, JsonFieldBuffer scratch)
{
    private bool _subsequent;
    private int? _rows;

    public async ValueTask<Schema> ReadSchemaAsync(CancellationToken token)
    {
        await input.ExpectAsync('{', token).ConfigureAwait(false);
        while (await ReadPropertyAsync(token).ConfigureAwait(false) is { } name)
        {
            switch (name)
            {
                case "meta":
                    var metadata = await input.ReadValueAsync(token).ConfigureAwait(false);
                    return ClickHouseMetadata.ParseSchema(metadata.Span, scratch, token);
                case "data":
                    throw new InvalidDataException("JSONColumnsWithMetadata requires meta before data.");
                default:
                    await ReadOtherPropertyAsync(name, token).ConfigureAwait(false);
                    break;
            }
        }
        throw new InvalidDataException("A JSONColumnsWithMetadata document requires meta.");
    }

    public async ValueTask<IArrowRecord> ReadBatchAsync(
        Func<CancellationToken, ValueTask<IArrowRecord>> readData, CancellationToken token)
    {
        IArrowRecord? batch = null;
        try
        {
            while (await ReadPropertyAsync(token).ConfigureAwait(false) is { } name)
            {
                if (name == "data" && batch is null)
                    batch = await readData(token).ConfigureAwait(false);
                else
                    await ReadOtherPropertyAsync(name, token).ConfigureAwait(false);
            }

            if (batch is null)
                throw new InvalidDataException("A JSONColumnsWithMetadata document requires data.");
            if (_rows is int rows && rows != batch.Length)
                throw new InvalidDataException($"ClickHouse metadata declares {rows} rows but the block contains {batch.Length}.");
            return batch;
        }
        catch
        {
            batch?.Dispose();
            throw;
        }
    }

    private async ValueTask<string?> ReadPropertyAsync(CancellationToken token)
    {
        if (!await input.MoveNextAsync('}', _subsequent, token).ConfigureAwait(false))
            return null;
        _subsequent = true;
        return await input.ReadPropertyNameAsync(scratch, token).ConfigureAwait(false);
    }

    private async ValueTask ReadOtherPropertyAsync(string name, CancellationToken token)
    {
        if (name is "meta" or "data" || (name == "rows" && _rows.HasValue))
            throw new InvalidDataException($"Duplicate ClickHouse property '{name}'.");

        var value = await input.ReadValueAsync(token).ConfigureAwait(false);
        if (name == "rows")
            _rows = JsonValues.Count(value.Span);
        else
            JsonRecordParser.ValidateValue(value.Span);
    }
}
