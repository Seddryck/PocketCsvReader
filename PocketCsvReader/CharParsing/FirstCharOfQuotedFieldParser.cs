using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.CharParsing;

internal class FirstCharOfQuotedFieldParser : IInternalCharParser
{
    protected CharParser Parser { get; set; }
    private char QuoteChar { get; set; }
    private char EscapeChar { get; set; }

    public FirstCharOfQuotedFieldParser(CharParser parser)
        => (Parser, QuoteChar, EscapeChar)
                = (parser, parser.Profile.Descriptor.QuoteChar, parser.Profile.Descriptor.EscapeChar);

    public virtual ParserState Parse(char c)
    {
        Parser.SetFieldStart();

        if (c == QuoteChar)
        {
            Parser.Switch(Parser.AfterQuoteChar);
            return ParserState.Continue;
        }


        if (c == EscapeChar)
        {
            Parser.Switch(Parser.AfterEscapeCharQuotedField);
            return ParserState.Continue;
        }

        Parser.Switch(Parser.CharOfQuotedField);
        return ParserState.Continue;
    }
}
