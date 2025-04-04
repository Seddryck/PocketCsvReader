using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Ndjson.CharParsing;
internal class QuotedCharOfLabelParser : FirstCharOfLabelParser
{
    private char? QuoteChar { get; }
    private char[] Whitespaces { get; }

    public QuotedCharOfLabelParser(CharParser parser)
        : base(parser)
    {
        QuoteChar = Parser.Profile.Dialect.QuoteChar;
        Whitespaces = Parser.Profile.Dialect.Whitespaces;
    }

    public override ParserState Parse(char c)
    {
        if (QuoteChar.HasValue && c == QuoteChar.Value)
        {
            Parser.Switch(Parser.FirstCharOfLabel);
            return ParserState.Continue;
        }
        else if (Whitespaces.Contains(c))
            return ParserState.Continue;
        else
            return ParserState.Error;
    }
}
