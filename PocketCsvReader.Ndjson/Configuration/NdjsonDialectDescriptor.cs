using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Ndjson.Configuration;

namespace PocketCsvReader.Ndjson.Configuration;

/// <summary>
/// Defines the dialect configuration for NDJSON (Newline-Delimited JSON) data parsing.
/// </summary>
public record NdjsonDialectDescriptor
(
    /// <summary>The character used to separate fields within a record.</summary>
    char Delimiter = ',',
    /// <summary>The character that separates labels from values (typically ':' in JSON).</summary>
    char Separator = ':',
    /// <summary>The string sequence that terminates a line or record.</summary>
    string LineTerminator = "\r\n",
    /// <summary>The character used for quoting field values, or null if quoting is not supported.</summary>
    char? QuoteChar = '"',
    /// <summary>The character used for escaping special characters, or null if escaping is not supported.</summary>
    char? EscapeChar = '\\',
    /// <summary>The character that marks the beginning of an object (typically '{' in JSON).</summary>
    char ObjectPrefix = '{',
    /// <summary>The character that marks the end of an object (typically '}' in JSON).</summary>
    char ObjectSuffix = '}',
    /// <summary>The character that starts a comment, or null if comments are not supported.</summary>
    char? CommentChar = null,
    /// <summary>Indicates whether to skip initial whitespace in fields.</summary>
    bool SkipInitialSpace = true,
    /// <summary>The collection of characters considered as whitespace, defaulting to space and tab if null.</summary>
    char[] Whitespaces = null!,
    /// <summary>The character that marks the beginning of an array, or null if arrays are not supported.</summary>
    char? ArrayPrefix = null,
    /// <summary>The character that marks the end of an array, or null if arrays are not supported.</summary>
    char? ArraySuffix = null,
    /// <summary>The character used to separate array elements, or null if arrays are not supported.</summary>
    char? ArrayDelimiter = null
)
{
    public char[] Whitespaces { get; init; } = Whitespaces ?? [' ', '\t'];
}
