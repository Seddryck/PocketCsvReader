namespace PocketCsvReader.Ndjson;

internal sealed class NdjsonLineBoundaryScanner(int terminatorLength) : IRecordBoundaryScanner
{
    public int IndexBoundaries(ReadOnlySpan<char> input, Span<RecordBoundary> boundaries)
    {
        int start = 0;
        int searchStart = 0;
        int count = 0;
        while (count < boundaries.Length)
        {
            int offset = input[searchStart..].IndexOf('\n');
            if (offset < 0)
                break;

            int newline = searchStart + offset;
            searchStart = newline + 1;
            int contentEnd = searchStart - terminatorLength;
            if (terminatorLength == 2 && (contentEnd < start || input[contentEnd] != '\r'))
                continue;

            // A physical newline is eligible only outside strings. Leave uncertain or
            // cross-buffer input untouched for the stateful framer and its error handling.
            if (!EndsOutsideString(input[start..contentEnd]))
                break;

            boundaries[count++] = new(contentEnd, searchStart);
            start = searchStart;
        }

        return count;
    }

    private static bool EndsOutsideString(ReadOnlySpan<char> input)
    {
        while (true)
        {
            int quote = input.IndexOf('"');
            if (quote < 0)
                return true;

            input = input[(quote + 1)..];
            while (true)
            {
                int special = input.IndexOfAny('"', '\\');
                if (special < 0)
                    return false;
                if (input[special] == '"')
                {
                    input = input[(special + 1)..];
                    break;
                }
                if (special + 1 == input.Length)
                    return false;
                input = input[(special + 2)..];
            }
        }
    }
}
