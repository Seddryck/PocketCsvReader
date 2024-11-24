using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.CharParsing;

internal class FirstCharOfFieldParser : IInternalCharParser
{
    protected CharParser Parser { get; set; }
    private char FirstCharOfLineTerminator { get; set; }
    private char QuoteChar { get; set; }
    private char Delimiter { get; set; }
    private bool IsSkipInitialSpace { get; set; }
    private char EscapeChar { get; set; }

    public FirstCharOfFieldParser(CharParser parser)
        => (Parser, FirstCharOfLineTerminator, QuoteChar, Delimiter, IsSkipInitialSpace, EscapeChar)
                = (parser, parser.Profile.Descriptor.LineTerminator[0], parser.Profile.Descriptor.QuoteChar
                    , parser.Profile.Descriptor.Delimiter, parser.Profile.Descriptor.SkipInitialSpace
                    , parser.Profile.Descriptor.EscapeChar);

    public virtual ParserState Parse(char c)
    {
        Parser.ResetFieldState();

        if (c == QuoteChar)
        {
            Parser.SetQuotedField();
            Parser.Switch(Parser.FirstCharOfQuotedField);
            return ParserState.Continue;
        }

        if (c == ' ' && IsSkipInitialSpace)
            return ParserState.Continue;

        if (c == Delimiter)
        {
            Parser.ZeroField();
            Parser.Switch(Parser.FirstCharOfField);
            return ParserState.Field;
        }

        if (c == FirstCharOfLineTerminator)
        {
            Parser.ZeroField();
            Parser.Switch(Parser.LineTerminator);
            return ParserState.Continue;
        }

        if (c == EscapeChar)
        {
            Parser.Switch(Parser.AfterEscapeChar);
            return ParserState.Continue;
        }

        Parser.SetFieldStart();
        Parser.Switch(Parser.CharOfField);
        return ParserState.Continue;
    }
}
