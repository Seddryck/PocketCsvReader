using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using PocketCsvReader.CharParsing;

namespace PocketCsvReader;

public enum ParserState
{
    Continue,
    Error,
    Field,
    Record,
    Header,
    Comment,
    Eof,
}
