namespace PocketCsvReader;

internal static class CsvHeaders
{
    public static async ValueTask<string[][]> ReadAsync(IRecordSource<CsvProfile> source, CancellationToken cancellationToken)
    {
        if (source is RecordParser parser)
            return await parser.ReadHeadersAsync(cancellationToken).ConfigureAwait(false);
        var dialect = source.Profile.Dialect;
        if (!dialect.Header) return [];
        var headers = new List<string[]>();
        for (int row = 1; row <= dialect.HeaderRows.Max(); row++)
        {
            var result = await source.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (dialect.HeaderRows.Contains(row))
            {
                var values = new string[result.Record.ParsedFields?.Length ?? 0];
                for (int i = 0; i < values.Length; i++)
                    values[i] = result.Record.Slice(i).ToString();
                headers.Add(values);
            }
        }
        return [.. headers];
    }

    public static string[][] Read(IRecordSource<CsvProfile> source)
    {
        if (source is RecordParser parser)
            return parser.ReadHeaders();
        var dialect = source.Profile.Dialect;
        if (!dialect.Header)
            return [];
        var headers = new List<string[]>();
        for (int row = 1; row <= dialect.HeaderRows.Max(); row++)
        {
            source.IsEndOfFile(out var record, out _);
            if (dialect.HeaderRows.Contains(row))
            {
                var values = new string[record.ParsedFields?.Length ?? 0];
                for (int i = 0; i < values.Length; i++)
                    values[i] = record.Slice(i).ToString();
                headers.Add(values);
            }
        }
        return [.. headers];
    }
}
