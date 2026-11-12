namespace PocketCsvReader.Markdown;

internal static class MarkdownTableRow
{
    public static int CountCells(ReadOnlySpan<char> line)
    {
        var cells = new Cells(line);
        int count = 0;
        while (cells.TryRead(out _))
            count++;
        return count;
    }

    public static int CountDelimiterCells(ReadOnlySpan<char> line)
    {
        var cells = new Cells(line);
        int count = 0;
        while (cells.TryRead(out var field))
        {
            var value = line.Slice(field.Value.Start, field.Value.Length);
            if (value.StartsWith(":")) value = value[1..];
            if (value.EndsWith(":")) value = value[..^1];
            if (value.IsEmpty || value.ContainsAnyExcept('-')) return 0;
            count++;
        }
        return count;
    }

    public static string DecodePipeEscapes(ReadOnlyMemory<char> value)
    {
        const string escape = "\\|";
        var remaining = value.Span;
        int escapes = 0;
        int index;
        while ((index = remaining.IndexOf(escape, StringComparison.Ordinal)) >= 0)
        {
            escapes++;
            remaining = remaining[(index + escape.Length)..];
        }

        return string.Create(value.Length - escapes, value, static (destination, source) =>
        {
            var remaining = source.Span;
            int copied = 0;
            int index;
            while ((index = remaining.IndexOf(escape, StringComparison.Ordinal)) >= 0)
            {
                remaining[..index].CopyTo(destination[copied..]);
                copied += index;
                destination[copied++] = '|';
                remaining = remaining[(index + escape.Length)..];
            }
            remaining.CopyTo(destination[copied..]);
        });
    }

    public ref struct Cells
    {
        private readonly ReadOnlySpan<char> _line;
        private int _start;

        // The caller has already recognized the opening pipe.
        public Cells(ReadOnlySpan<char> line)
        {
            _line = line.TrimEnd();
            _start = line.Length - line.TrimStart().Length + 1;
        }

        public bool TryRead(out FieldSpan field)
        {
            if (_start >= _line.Length)
            {
                field = default;
                return false;
            }

            int end = _start;
            bool escaped = false;
            while (end < _line.Length)
            {
                int pipe = _line[end..].IndexOf('|');
                if (pipe < 0)
                {
                    end = _line.Length;
                    break;
                }

                end += pipe;
                if (end == _start || _line[end - 1] != '\\')
                    break;

                escaped = true;
                end++;
            }

            var cell = _line[_start..end];
            var leftTrimmed = cell.TrimStart();
            field = new FieldSpan(_start + cell.Length - leftTrimmed.Length,
                leftTrimmed.TrimEnd().Length, wasQuoted: false, isEscaped: escaped);
            _start = end + 1;
            return true;
        }
    }
}
