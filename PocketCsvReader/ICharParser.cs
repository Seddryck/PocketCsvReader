using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader;
public interface ICharParser
{
    ParserState Parse(char c);
    void Reset();
    ParserState ParseEof();
    int ValueStart { get; }
    int ValueLength { get; }
    int LabelStart { get; }
    int LabelLength { get; }
    bool IsQuotedField { get; }
    bool IsEscapedField { get; }
    FieldSpan[] Children { get; }
    IInternalCharParser? InternalCharParser { get; }
}
