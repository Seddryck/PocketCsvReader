using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Ndjson.CharParsing;

internal class FirstCharOfLabelParser : IInternalCharParser
{
    protected CharParser Parser { get; set; }

    private char? QuoteChar { get; set; }
    private char[] Whitespaces { get; }

    public FirstCharOfLabelParser(CharParser parser)
        => (Parser, QuoteChar, Whitespaces)
                = (parser, parser.Profile.Dialect.QuoteChar, parser.Profile.Dialect.Whitespaces);

    public virtual ParserState Parse(char c)
    {
        if (Whitespaces.Contains(c) || QuoteChar.HasValue && c == QuoteChar.Value)
            return ParserState.Error;

        Parser.ResetFieldState();
        Parser.SetLabelStart();
        Parser.Switch(Parser.CharOfLabel);
        return ParserState.Continue;
    }
}
