using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Ndjson.CharParsing;
internal class LabelValueSeparatorParser : IInternalCharParser
{
    protected CharParser Parser { get; set; }
    private char Separator { get; set; }
    private char[] Whitespaces { get; }

    public LabelValueSeparatorParser(CharParser parser)
        => (Parser, Separator, Whitespaces)
                = (parser, parser.Profile.Dialect.SeparatorChar, parser.Profile.Dialect.Whitespaces);

    public virtual ParserState Parse(char c)
    {
        if (c == Separator)
        {
            Parser.Switch(Parser.FirstCharOfValue);
            return ParserState.Continue;
        }
        else if (Whitespaces.Contains(c))
            return ParserState.Continue;
        else
            return ParserState.Error;
    }
}

