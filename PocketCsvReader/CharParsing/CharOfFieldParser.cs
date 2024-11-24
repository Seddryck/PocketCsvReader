using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.CharParsing;
internal class CharOfFieldParser : IInternalCharParser
{
    protected CharParser Parser { get; set; }
    private char FirstCharOfLineTerminator { get; set; }
    private char Delimiter { get; set; }
    private char EscapeChar { get; set; }

    public CharOfFieldParser(CharParser parser)
        => (Parser, FirstCharOfLineTerminator, Delimiter, EscapeChar)
                = (parser, parser.Profile.Descriptor.LineTerminator[0], parser.Profile.Descriptor.Delimiter
                    , parser.Profile.Descriptor.EscapeChar);

    public virtual ParserState Parse(char c)
    {
        if (c == Delimiter)
        {
            Parser.SetFieldEnd(-1);
            Parser.Switch(Parser.FirstCharOfField);
            return ParserState.Field;
        }

        if (c == FirstCharOfLineTerminator)
        {
            Parser.SetFieldEnd(-1);
            Parser.Switch(Parser.LineTerminator);
            return Parser.Profile.Descriptor.LineTerminator.Length == 1
                ? ParserState.Record
                : ParserState.Continue;
        }

        if (c == Parser.Profile.Descriptor.EscapeChar)
        {
            Parser.Switch(Parser.AfterEscapeChar);
            return ParserState.Continue;
        }

        return ParserState.Continue;
    }
}
