using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader;

public record DialectDescriptor
(
    bool Header = true,
    int[] HeaderRows = null!,
    string HeaderJoin = " ",
    bool HeaderRepeat = true,
    int[]? CommentRows = null,
    char? CommentChar = null,
    char Delimiter = ',',
    string LineTerminator = "\r\n",
    char? QuoteChar = '"',
    bool DoubleQuote = true,
    char? EscapeChar = null,
    string? NullSequence = null,
    string? MissingCell = null,
    bool SkipInitialSpace = false,
    char? ArrayDelimiter = null,
    char? ArrayPrefix = null,
    char? ArraySuffix = null
)
{
    public int[] HeaderRows { get; init; } = HeaderRows ?? [1];
}
