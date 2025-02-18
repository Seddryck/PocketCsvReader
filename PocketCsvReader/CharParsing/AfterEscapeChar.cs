﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.CharParsing;
internal class AfterEscapeCharParser : CharOfFieldParser
{
    public AfterEscapeCharParser(CharParser parser)
        : base(parser) { }

    public override ParserState Parse(char c)
    {
        if (c == Parser.Profile.Dialect.Delimiter
            || c == Parser.Profile.Dialect.EscapeChar)
        {
            Parser.SetEscapedField();
            Parser.Switch(Parser.CharOfField);
            return ParserState.Continue;
        }

        return base.Parse(c);
    }
}
