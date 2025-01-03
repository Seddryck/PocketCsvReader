using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader;

public record DialectDescriptor
(
    string Schema = "https://datapackage.org/profiles/1.0/tabledialect.json",
    bool Header = true,
    int[] HeaderRows = null!,
    string HeaderJoin = " ",
    int[]? CommentRows = null,
    char? CommentChar = null,
    char Delimiter = ',',
    string LineTerminator = "\r\n",
    char? QuoteChar = '"',
    bool DoubleQuote = true,
    char? EscapeChar = null,
    string? NullSequence = null,
    bool SkipInitialSpace = false,
    string CsvDdfVersion = "2.0"
)
{
    public int[] HeaderRows { get; init; } = HeaderRows ?? [1];
}
