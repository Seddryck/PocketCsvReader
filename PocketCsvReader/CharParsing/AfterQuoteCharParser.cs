﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.CharParsing;
internal class AfterQuoteCharParser : IInternalCharParser
{
    protected CharParser Parser { get; set; }

    public AfterQuoteCharParser(CharParser parser)
        => Parser = parser;

    public virtual ParserState Parse(char c)
    {
        if (c == Parser.Profile.Dialect.Delimiter)
        {
            Parser.SetFieldEnd(-2);
            Parser.Switch(Parser.FirstCharOfField);
            return ParserState.Field;
        }

        if (c == Parser.Profile.Dialect.LineTerminator[0])
        {
            Parser.SetFieldEnd(-2);
            Parser.Switch(Parser.LineTerminator);
            return Parser.Profile.Dialect.LineTerminator.Length == 1
                ? ParserState.Record
                : ParserState.Continue;
        }

        return ParserState.Error;
    }
}
