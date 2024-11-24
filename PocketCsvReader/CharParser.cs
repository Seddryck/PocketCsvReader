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

    public IInternalCharParser Internal { get; private set; } = null!;

    internal FirstCharOfRecordParser FirstCharOfRecord { get; }
    internal FirstCharOfFieldParser FirstCharOfField { get; }
    internal FirstCharOfQuotedFieldParser FirstCharOfQuotedField { get; }
    internal CharOfFieldParser CharOfField { get; }
    internal CharOfQuotedFieldParser CharOfQuotedField { get; }
    internal IInternalCharParser LineTerminator { get; }
    internal CommentParser Comment { get; }
    internal AfterQuoteCharParser AfterQuoteChar { get; }
    internal AfterEscapeCharQuotedFieldParser AfterEscapeCharQuotedField { get; }
    internal AfterEscapeCharParser AfterEscapeChar { get; }

    public CharParser(CsvProfile profile)
    {
        Profile = profile;
        FirstCharOfRecord = new FirstCharOfRecordParser(this);
        FirstCharOfQuotedField = new FirstCharOfQuotedFieldParser(this);
        FirstCharOfField = new FirstCharOfFieldParser(this);
        LineTerminator = Profile.Descriptor.LineTerminator.Length == 1
            ? new FirstCharOfRecordParser(this)
            : new LineTerminatorParser(this, Profile.Descriptor.LineTerminator.Length);
        Comment = new CommentParser(this, Profile.Descriptor.LineTerminator.Length);
        CharOfField = new CharOfFieldParser(this);
        CharOfQuotedField = new CharOfQuotedFieldParser(this);
        AfterQuoteChar = Profile.Descriptor.DoubleQuote
            ? new AfterQuoteCharDoubleParser(this)
            : new AfterQuoteCharParser(this);
        AfterEscapeCharQuotedField = new AfterEscapeCharQuotedFieldParser(this);
        AfterEscapeChar = new AfterEscapeCharParser(this);
        Internal = FirstCharOfRecord;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ParserState Parse(char c)
    {
        Position++;
        return Internal.Parse(c);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset()
    {
        Position = Internal == LineTerminator && LineTerminator is LineTerminatorParser parser
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
    internal void Switch(IInternalCharParser parser)
        => Internal = parser;
}

public enum ParserState
{
    Continue,
    Error,
    Field,
    Record,
    Eof,
}
