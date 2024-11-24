using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using PocketCsvReader.CharParsing;

namespace PocketCsvReader;
public class CharParser
{
    public int Position { get; private set; } = -1;
    public int FieldStart { get; private set; } = 0;
    public int FieldLength { get; private set; } = 0;
    public bool IsQuotedField { get; private set; } = false;
    public bool IsEscapedField { get; private set; } = false;
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
        FirstCharOfField = new FirstCharOfFieldParser(this).Parse;
        LineTerminatorParser = Profile.Descriptor.LineTerminator.Length == 1
            ? new FirstCharOfRecordParser(this)
            : new LineTerminatorParser(this, Profile.Descriptor.LineTerminator.Length);
        LineTerminator = LineTerminatorParser.Parse;
        Comment = new CommentParser(this, Profile.Descriptor.LineTerminator.Length).Parse;
        CharOfField = Profile.ParserOptimizations.LookupTableChar
            ? new CharOfFieldLookupParser(this).Parse
            : new CharOfFieldParser(this).Parse;
        CharOfQuotedField = new CharOfQuotedFieldParser(this).Parse;
        AfterQuoteChar = Profile.Descriptor.DoubleQuote
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
            ? FieldStart + FieldLength - Position - parser.Index - 1
            : -1;
        FieldStart = FieldLength = 0;
    }

    public ParserState ParseEof()
    {
        if (Internal == FirstCharOfRecord || Internal == Comment)
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
        => (FieldStart, FieldLength) = (Position, 0);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void SetFieldStart()
        => (FieldStart, FieldLength) = (Position, 1);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void SetFieldEnd(int i)
        => (FieldLength) = (Position - FieldStart + 1 + i);
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
    Eof,
}
