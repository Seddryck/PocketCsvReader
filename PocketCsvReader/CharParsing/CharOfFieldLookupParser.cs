﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.CharParsing;
internal class CharOfFieldLookupParser : IInternalCharParser
{
    protected CharParser Parser { get; set; }
    protected readonly bool[] InterestingChars;
    private char FirstCharOfLineTerminator { get; set; }
    private char Delimiter { get; set; }
    private char? EscapeChar { get; set; }

    public CharOfFieldLookupParser(CharParser parser)
    {
        (Parser, FirstCharOfLineTerminator, Delimiter, EscapeChar)
                    = (parser, parser.Profile.Dialect.LineTerminator[0], parser.Profile.Dialect.Delimiter
                        , parser.Profile.Dialect.EscapeChar);

        InterestingChars = new bool[char.MaxValue + 1];
        InterestingChars[Delimiter] = true;
        InterestingChars[FirstCharOfLineTerminator] = true;
        if (EscapeChar.HasValue)
            InterestingChars[EscapeChar.Value] = true;
    }

    public virtual ParserState Parse(char c)
    {
        if (!InterestingChars[c])
            return ParserState.Continue;

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
            return Parser.Profile.Dialect.LineTerminator.Length == 1
                ? ParserState.Record
                : ParserState.Continue;
        }

        if (EscapeChar.HasValue && c == EscapeChar)
        {
            Parser.Switch(Parser.AfterEscapeChar);
            return ParserState.Continue;
        }

        throw new InvalidOperationException("Unexpected character");
    }
}

