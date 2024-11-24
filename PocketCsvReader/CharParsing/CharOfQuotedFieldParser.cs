using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.CharParsing;
internal class CharOfQuotedFieldParser : IInternalCharParser
{
    protected CharParser Parser { get; set; }

    public CharOfQuotedFieldParser(CharParser parser)
        => Parser = parser;

    public virtual ParserState Parse(char c)
    {
        if (c == Parser.Profile.Descriptor.QuoteChar)
        {
            Parser.Switch(Parser.AfterQuoteChar);
            return ParserState.Continue;
        }

        if (c == Parser.Profile.Descriptor.EscapeChar)
        {
            Parser.Switch(Parser.AfterEscapeCharQuotedField);
            return ParserState.Continue;
        }

        return ParserState.Continue;
    }
}
