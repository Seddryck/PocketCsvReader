using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Ndjson.CharParsing;
internal class CharOfLabelParser : IInternalCharParser
{
    protected CharParser Parser { get; set; }
    private char? QuoteChar { get; set; }    

    public CharOfLabelParser(CharParser parser)
        => (Parser, QuoteChar)
                = (parser, parser.Profile.Dialect.QuoteChar);

    public virtual ParserState Parse(char c)
    {
        if (QuoteChar.HasValue && c == QuoteChar.Value)
        {
            Parser.SetLabelEnd(-1);
            Parser.Switch(Parser.LabelValueSeparator);
            return ParserState.Continue;
        }

        return ParserState.Continue;
    }
}
