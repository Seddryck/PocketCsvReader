using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Ndjson.CharParsing;

internal class FirstCharOfRecordParser : IInternalCharParser
{
    protected CharParser Parser { get; set; }

    private char? BeginRecord { get; set; }
    private char[] Whitespaces { get; }

    public FirstCharOfRecordParser(CharParser parser)
        => (Parser, BeginRecord, Whitespaces)
                = (parser, parser.Profile.Dialect.BeginRecord, parser.Profile.Dialect.Whitespaces);

    public virtual ParserState Parse(char c)
    {
        if (Whitespaces.Contains(c))
            return ParserState.Continue;
        if (BeginRecord.HasValue && c == BeginRecord.Value)
        {
            Parser.SetQuotedField();
            Parser.Switch(Parser.QuotedCharOfLabel);
            return ParserState.Continue;
        }
        else
            return ParserState.Error;
    }
}
