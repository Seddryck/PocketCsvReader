using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Ndjson.Configuration;

public record NdjsonDialectDescriptor
(
    char Delimiter = ',',
    char Separator = ':',
    string LineTerminator = "\r\n",
    char? QuoteChar = '"',
    char? EscapeChar = '\\',
    char ObjectPrefix = '{',
    char ObjectSuffix = '}',
    char? CommentChar = null,
    bool SkipInitialSpace = true,
    char[] Whitespaces = null!,
    char? ArrayPrefix = null,
    char? ArraySuffix = null,
    char? ArrayDelimiter = null
)
{
    public char[] Whitespaces { get; init; } = Whitespaces ?? [' ', '\t'];
}
