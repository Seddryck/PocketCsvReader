using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Ndjson.CharParsing;
internal class CharOfValueParser : IInternalCharParser
{
    protected CharParser Parser { get; set; }
    private char? QuoteChar { get; set; }
    private char[] EndingValueChars { get; set; }
    private char? EndRecord { get; set; }
    private char? FieldDelimiter { get; set; }

    public CharOfValueParser(CharParser parser)
    {
        (Parser, QuoteChar) = (parser, parser.Profile.Dialect.QuoteChar);
        var endingValueChars = new List<char>();
        endingValueChars.AddRange(parser.Profile.Dialect.Whitespaces);
        EndingValueChars = [.. endingValueChars];
        EndRecord = parser.Profile.Dialect.EndRecord;
        FieldDelimiter = parser.Profile.Dialect.Delimiter;
    }

    public virtual ParserState Parse(char c)
    {
        if ((QuoteChar.HasValue && c == QuoteChar) || (!Parser.IsQuotedField && EndingValueChars.Contains(c)))
        {
            Parser.SetValueEnd(-1);
            Parser.Switch(Parser.FieldDelimiter);
            return ParserState.Continue;
        }
        else if (!Parser.IsQuotedField && EndRecord.HasValue && EndRecord.Value==c)
        {
            Parser.SetValueEnd(-1);
            Parser.Switch(Parser.LineTerminator);
            return ParserState.Field;
        }
        else if (!Parser.IsQuotedField && FieldDelimiter.HasValue && FieldDelimiter.Value == c)
        {
            Parser.SetValueEnd(-1);
            Parser.Switch(Parser.QuotedCharOfLabel);
            return ParserState.Field;
        }

        return ParserState.Continue;
    }
}
