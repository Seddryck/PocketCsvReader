using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.CharParsing;
internal class CharOfQuotedFieldParser : IInternalCharParser
{
    protected CharParser Parser { get; set; }
    private char? QuoteChar { get; set; }
    private char? EscapeChar { get; set; }

    public CharOfQuotedFieldParser(CharParser parser)
        => (Parser, QuoteChar, EscapeChar) = (parser, parser.Profile.Dialect.QuoteChar, parser.Profile.Dialect.EscapeChar);

    public virtual ParserState Parse(char c)
    {
        if (QuoteChar.HasValue && QuoteChar.Value == c)
        {
            Parser.Switch(Parser.AfterQuoteChar);
            return ParserState.Continue;
        }

        if (EscapeChar.HasValue && EscapeChar.Value == c)
        {
            Parser.Switch(Parser.AfterEscapeCharQuotedField);
            return ParserState.Continue;
        }

        return ParserState.Continue;
    }
}
