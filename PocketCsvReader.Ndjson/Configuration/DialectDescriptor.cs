using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Ndjson;

public record DialectDescriptor
(
    char Delimiter = ',',
    string LineTerminator = "\r\n",
    char? QuoteChar = '"',
    char? EscapeChar = '\\',
    char? BeginRecord = '{',
    char? EndRecord = '}',
    char SeparatorChar = ':',
    char[] Whitespaces = null!
)
{
    public char[] Whitespaces { get; init; } = Whitespaces ?? [' ', '\t'];
}
