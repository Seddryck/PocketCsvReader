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
    private char? QuoteChar { get; set; }
    private char Delimiter { get; set; }
    private bool IsSkipInitialSpace { get; set; }
    private char? EscapeChar { get; set; }

    public FirstCharOfFieldParser(CharParser parser)
        => (Parser, FirstCharOfLineTerminator, QuoteChar, Delimiter, IsSkipInitialSpace, EscapeChar)
                = (parser, parser.Profile.Dialect.LineTerminator[0], parser.Profile.Dialect.QuoteChar
                    , parser.Profile.Dialect.Delimiter, parser.Profile.Dialect.SkipInitialSpace
                    , parser.Profile.Dialect.EscapeChar);

    public virtual ParserState Parse(char c)
    {
        Parser.ResetFieldState();

        if (QuoteChar.HasValue && c == QuoteChar)
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

        if (EscapeChar.HasValue && c == EscapeChar)
        {
            Parser.Switch(Parser.AfterEscapeChar);
            return ParserState.Continue;
        }

        Parser.SetFieldStart();
        Parser.Switch(Parser.CharOfField);
        return ParserState.Continue;
    }
}
