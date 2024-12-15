using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Configuration;

public enum Delimiter
{
    Comma = ',',
    Semicolon = ';',
    Tab = '\t',
    Pipe = '|'
}

public enum LineTerminator
{
    CarriageReturnLineFeed,
    LineFeed,
    CarriageReturn
}

public enum QuoteChar
{
    DoubleQuote = '"',
    SingleQuote = '\''
}

public enum EscapeChar
{
    BackSlash = '\\',
    ForwardSlash = '/'
}

public enum CommentChar
{
    Hash = '#',
    Semicolon = ';',
    ForwardSlash = '/',
    Dash = '-'
}
