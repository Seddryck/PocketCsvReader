using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using PocketCsvReader.CharParsing;

namespace PocketCsvReader;
public class CharParser : ICharParser
{
    public int RowNumber { get; set; } = 0;
    public int Position { get; private set; } = -1;
    public int ValueStart { get; private set; } = 0;
    public int ValueLength { get; private set; } = 0;
    public int LabelStart => 0;
    public int LabelLength => 0;
    public bool IsQuotedField { get; private set; } = false;
    public bool IsEscapedField { get; private set; } = false;
    public bool IsHeaderRow { get; private set; } = false;
    public CsvProfile Profile { get; }

    internal delegate ParserState InternalParse(char c);
    internal InternalParse Internal { get; private set; }

    internal IInternalCharParser LineTerminatorParser { get; }

    internal InternalParse FirstCharOfRecord { get; }
    internal InternalParse FirstCharOfField { get; }
    internal InternalParse FirstCharOfQuotedField { get; }
    internal InternalParse CharOfField { get; }
    internal InternalParse CharOfQuotedField { get; }
    internal InternalParse LineTerminator { get; }
    internal InternalParse Comment { get; }
    internal InternalParse AfterQuoteChar { get; }
    internal InternalParse AfterEscapeCharQuotedField { get; }
    internal InternalParse AfterEscapeChar { get; }

    public CharParser(CsvProfile profile)
    {
        Profile = profile;
        FirstCharOfRecord = new FirstCharOfRecordParser(this).Parse;
        FirstCharOfQuotedField = new FirstCharOfQuotedFieldParser(this).Parse;
        FirstCharOfField = Profile.ParserOptimizations.LookupTableChar
            ? new FirstCharOfFieldLookupParser(this).Parse
            : new FirstCharOfFieldParser(this).Parse;
        LineTerminatorParser = Profile.Dialect.LineTerminator.Length == 1
            ? new FirstCharOfRecordParser(this)
            : new LineTerminatorParser(this, Profile.Dialect.LineTerminator.Length);
        LineTerminator = LineTerminatorParser.Parse;
        Comment = new CommentParser(this, Profile.Dialect.LineTerminator.Length).Parse;
        CharOfField = Profile.ParserOptimizations.LookupTableChar
            ? new CharOfFieldLookupParser(this).Parse
            : new CharOfFieldParser(this).Parse;
        CharOfQuotedField = new CharOfQuotedFieldParser(this).Parse;
        AfterQuoteChar = Profile.Dialect.DoubleQuote
            ? new AfterQuoteCharDoubleParser(this).Parse
            : new AfterQuoteCharParser(this).Parse;
        AfterEscapeCharQuotedField = new AfterEscapeCharQuotedFieldParser(this).Parse;
        AfterEscapeChar = new AfterEscapeCharParser(this).Parse;
        Internal = FirstCharOfRecord;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ParserState Parse(char c)
    {
        Position++;
        return Internal.Invoke(c);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset()
    {
        Position = Internal == LineTerminator && LineTerminatorParser is LineTerminatorParser parser
            ? ValueStart + ValueLength - Position - parser.Index - 1
            : -1;
        ValueStart = ValueLength = 0;
    }

    public ParserState ParseEof()
    {
        if (Internal == FirstCharOfRecord || Internal == Comment || (Internal == LineTerminator && Profile.Dialect.LineTerminator.Length == 1))
            return ParserState.Eof;
        else if (Internal == FirstCharOfField)
        {
            ZeroField();
            return ParserState.Record;
        }
        else
        if (Internal == AfterQuoteChar || Internal == CharOfField)
        {
            SetFieldEnd(Internal == AfterQuoteChar ? -1 : 0);
            return ParserState.Record;
        }
        return ParserState.Error;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void ZeroField()
        => (ValueStart, ValueLength) = (Position, 0);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void SetFieldStart()
        => (ValueStart, ValueLength) = (Position, 1);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void SetFieldEnd(int i)
        => (ValueLength) = (Position - ValueStart + 1 + i);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void SetFieldEnd()
        => SetFieldEnd(0);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void ResetFieldState()
        => IsQuotedField = IsEscapedField = false;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void SetQuotedField()
        => IsQuotedField = true;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void SetEscapedField()
        => IsEscapedField = true;
    internal void SetHeaderRow()
        => IsHeaderRow = true;
    internal void UnsetHeaderRow()
        => IsHeaderRow = false;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Switch(InternalParse parse)
        => Internal = parse;
}

public enum ParserState
{
    Continue,
    Error,
    Field,
    Record,
    Header,
    Eof,
}
