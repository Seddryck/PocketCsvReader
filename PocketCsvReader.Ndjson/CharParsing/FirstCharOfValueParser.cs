using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Ndjson.CharParsing;

internal class FirstCharOfValueParser : IInternalCharParser
{
    protected CharParser Parser { get; set; }

    private char? QuoteChar { get; set; }
    private char[] ForbiddenChars { get; set; }
    private char[] Whitespaces { get; }

    public FirstCharOfValueParser(CharParser parser)
    {
        (Parser, QuoteChar, Whitespaces)
                = (parser, parser.Profile.Dialect.QuoteChar, parser.Profile.Dialect.Whitespaces);
        var forbiddenChars = new List<char>();
        if (parser.Profile.Dialect.EndRecord.HasValue)
            forbiddenChars.Add(parser.Profile.Dialect.EndRecord.Value);
        forbiddenChars.Add(parser.Profile.Dialect.Delimiter);
        forbiddenChars.Add(parser.Profile.Dialect.SeparatorChar);
        ForbiddenChars = [.. forbiddenChars];
    }

    public virtual ParserState Parse(char c)
    {
        if (Whitespaces.Contains(c))
            return ParserState.Continue;
        if (QuoteChar.HasValue && c == QuoteChar)
        {
            Parser.SetQuotedField();
            Parser.SetValueStart(1);
            Parser.Switch(Parser.CharOfValue);
            return ParserState.Continue;
        }
        else if (ForbiddenChars.Contains(c))
            return ParserState.Error;
        else
        {
            Parser.SetValueStart();
            Parser.Switch(Parser.CharOfValue);
            return ParserState.Continue;
        }
    }
}
