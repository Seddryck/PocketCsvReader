using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Ndjson.Configuration;

/// <summary>
/// Defines the dialect configuration for NDJSON (Newline-Delimited JSON) data parsing.
/// </summary>
/// <param name="Delimiter">The character used to separate fields within a record.</param>
/// <param name="Separator">The character that separates labels from values (typically ':' in JSON).</param>
/// <param name="LineTerminator">The string sequence that terminates a line or record.</param>
/// <param name="QuoteChar">The character used for quoting field values, or null if quoting is not supported.</param>
/// <param name="EscapeChar">The character used for escaping special characters, or null if escaping is not supported.</param>
/// <param name="ObjectPrefix">The character that marks the beginning of an object (typically '{' in JSON).</param>
/// <param name="ObjectSuffix">The character that marks the end of an object (typically '}' in JSON).</param>
/// <param name="CommentChar">The character that starts a comment, or null if comments are not supported.</param>
/// <param name="SkipInitialSpace">Indicates whether to skip initial whitespace in fields.</param>
/// <param name="Whitespaces">The collection of characters considered as whitespace, defaulting to space and tab if null.</param>
/// <param name="ArrayPrefix">The character that marks the beginning of an array, or null if arrays are not supported.</param>
/// <param name="ArraySuffix">The character that marks the end of an array, or null if arrays are not supported.</param>
/// <param name="ArrayDelimiter">The character used to separate array elements, or null if arrays are not supported.</param>
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
    char? ArrayPrefix = '[',
    char? ArraySuffix = ']',
    char? ArrayDelimiter = ','
)
{
    public char[] Whitespaces { get; init; } = Whitespaces ?? [' ', '\t', '\r', '\n'];
}
