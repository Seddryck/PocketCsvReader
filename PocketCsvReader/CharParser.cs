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
    public CharParser(CsvProfile profile)
    {
            
    }

    public int ValueStart => throw new NotImplementedException();

    public int ValueLength => throw new NotImplementedException();

    public int LabelStart => throw new NotImplementedException();

    public int LabelLength => throw new NotImplementedException();

    public bool IsQuotedField => throw new NotImplementedException();

    public bool IsEscapedField => throw new NotImplementedException();

    public FieldSpan[] Children => throw new NotImplementedException();

    public IInternalCharParser? InternalCharParser => throw new NotImplementedException();

    public ParserState Parse(char c) => throw new NotImplementedException();
    public ParserState ParseEof() => throw new NotImplementedException();
    public void Reset() => throw new NotImplementedException();
}

public enum ParserState
{
    Continue,
    Error,
    Field,
    Record,
    Header,
    Comment,
    Eof,
}
