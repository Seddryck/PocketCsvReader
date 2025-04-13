using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.CharParsing;
public interface IParser
{
    ParserState Parse(char c, int pos);
    ParserState ParseEof(int pos);
    void Reset();
    ref FieldSpan Result { get; }
}
