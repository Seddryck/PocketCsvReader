using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using PocketCsvReader.Ndjson.CharParsing;
using PocketCsvReader.Ndjson.Configuration;

namespace PocketCsvReader.Ndjson;
public class CharParser : ICharParser
{
    public int RowNumber { get; set; } = 0;
    public int Position { get; private set; } = -1;
    public int ValueStart { get; private set; } = 0;
    public int ValueLength { get; private set; } = 0;
    public int LabelStart { get; private set; } = 0;
    public int LabelLength { get; private set; } = 0;
    public bool IsQuotedField { get; private set; } = false;
    public bool IsEscapedField { get; private set; } = false;
    public bool IsHeaderRow { get; private set; } = false;
    public NdjsonProfile Profile { get; }

    internal delegate ParserState InternalParse(char c);
    internal InternalParse Internal { get; private set; }

    internal IInternalCharParser LineTerminatorParser { get; }

    internal InternalParse FirstCharOfRecord { get; }
    internal InternalParse QuotedCharOfLabel { get; }
    internal InternalParse FirstCharOfLabel { get; }
    internal InternalParse CharOfLabel { get; }
    internal InternalParse LabelValueSeparator { get; }
    internal InternalParse FirstCharOfValue { get; }
    internal InternalParse CharOfValue { get; }
    internal InternalParse FieldDelimiter { get; }
    internal InternalParse LineTerminator { get; }

    public CharParser(NdjsonProfile profile)
    {
        Profile = profile;
        FirstCharOfRecord = new FirstCharOfRecordParser(this).Parse;
        QuotedCharOfLabel = new QuotedCharOfLabelParser(this).Parse;
        FirstCharOfLabel = new FirstCharOfLabelParser(this).Parse;
        CharOfLabel = new CharOfLabelParser(this).Parse;
        LabelValueSeparator = new LabelValueSeparatorParser(this).Parse;
        FirstCharOfValue = new FirstCharOfValueParser(this).Parse;
        CharOfValue = new CharOfValueParser(this).Parse;
        FieldDelimiter = new FieldDelimiterParser(this).Parse;
        LineTerminatorParser = new LineTerminatorParser(this, profile.Dialect.LineTerminator.Length);
        LineTerminator = LineTerminatorParser.Parse;
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
        if (Internal == FirstCharOfRecord)
            return ParserState.Eof;
        if (Internal == LineTerminator && ((LineTerminatorParser)LineTerminatorParser).Index == 0)
            return ParserState.Record;
        return ParserState.Error;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void ZeroField()
        => (ValueStart, ValueLength) = (Position, 0);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void SetLabelStart()
        => (LabelStart, LabelLength) = (Position, 1);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void SetLabelEnd(int i)
        => (LabelLength) = (Position - LabelStart + 1 + i);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void SetValueStart()
        => (ValueStart, ValueLength) = (Position, 1);
    internal void SetValueStart(int i)
        => (ValueStart, ValueLength) = (Position + i, 1);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void SetValueEnd(int i)
        => (ValueLength) = (Position - ValueStart + 1 + i);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void SetFieldEnd()
        => SetValueEnd(0);
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

