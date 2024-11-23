using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.CharParsing;

internal class FirstCharOfQuotedFieldParser : IInternalCharParser
{
    protected CharParser Parser { get; set; }

    public FirstCharOfQuotedFieldParser(CharParser parser)
        => Parser = parser;

    public void Initialize()
    { }

    public virtual ParserState Parse(char c)
    {
        Parser.SetFieldStart();

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

        Parser.Switch(Parser.CharOfQuotedField);
        return ParserState.Continue;
    }
}
