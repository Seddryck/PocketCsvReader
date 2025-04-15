using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.CharParsing;
public interface IParserStateController
{
    ParserState Parse(char c, int pos);
    ParserState ParseEof(int pos);
    void SwitchToValue();
    void SwitchToQuoted();
    void SwitchToRaw();
    void SwitchToArray();
    void SwitchToComment();
    void SwitchToLineTerminator(ParserState state);
    void SwitchBack();
    void SwitchUp();
    void Reset();
}
