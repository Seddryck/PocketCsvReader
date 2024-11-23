using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.CharParsing;

internal class FirstCharOfFieldParser : IInternalCharParser
{
    protected CharParser Parser { get; set; }

    public FirstCharOfFieldParser(CharParser parser)
        => Parser = parser;

    public void Initialize()
        => Parser.ResetFieldState();

    public virtual ParserState Parse(char c)
    {
        if (c == Parser.Profile.Descriptor.QuoteChar)
        {
            Parser.SetQuotedField();
            Parser.Switch(Parser.FirstCharOfQuotedField);
            return ParserState.Continue;
        }

        if (c == ' ' && Parser.Profile.Descriptor.SkipInitialSpace)
            return ParserState.Continue;

        if (c == Parser.Profile.Descriptor.Delimiter)
        {
            Parser.ZeroField();
            Parser.Switch(Parser.FirstCharOfField);
            return ParserState.Field;
        }

        if (c == Parser.Profile.Descriptor.LineTerminator[0])
        {
            Parser.ZeroField();
            Parser.Switch(Parser.LineTerminator);
            return ParserState.Continue;
        }

        if (c == Parser.Profile.Descriptor.EscapeChar)
        {
            Parser.Switch(Parser.AfterEscapeChar);
            return ParserState.Continue;
        }

        Parser.SetFieldStart();
        Parser.Switch(Parser.CharOfField);
        return ParserState.Continue;
    }
}
