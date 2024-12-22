using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.CharParsing;

internal class FirstCharOfFieldLookupParser : IInternalCharParser
{
    protected CharParser Parser { get; set; }
    protected readonly bool[] InterestingChars;
    private char FirstCharOfLineTerminator { get; set; }
    private char? QuoteChar { get; set; }
    private char Delimiter { get; set; }
    private bool IsSkipInitialSpace { get; set; }
    private char? EscapeChar { get; set; }

    public FirstCharOfFieldLookupParser(CharParser parser)
    {
        (Parser, FirstCharOfLineTerminator, QuoteChar, Delimiter, IsSkipInitialSpace, EscapeChar)
                = (parser, parser.Profile.Descriptor.LineTerminator[0], parser.Profile.Descriptor.QuoteChar
                    , parser.Profile.Descriptor.Delimiter, parser.Profile.Descriptor.SkipInitialSpace
                    , parser.Profile.Descriptor.EscapeChar);

        InterestingChars = new bool[char.MaxValue + 1];
        InterestingChars[Delimiter] = true;
        InterestingChars[FirstCharOfLineTerminator] = true;
        if (EscapeChar.HasValue)
            InterestingChars[EscapeChar.Value] = true;
        if (QuoteChar.HasValue)
            InterestingChars[QuoteChar.Value] = true;
        InterestingChars[' '] = IsSkipInitialSpace;
    }

    public virtual ParserState Parse(char c)
    {
        Parser.ResetFieldState();

        if (!InterestingChars[c])
        {
            Parser.SetFieldStart();
            Parser.Switch(Parser.CharOfField);
            return ParserState.Continue;
        }

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

        throw new InvalidOperationException("Unexpected character");
    }
}
