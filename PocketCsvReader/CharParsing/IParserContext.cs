using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.CharParsing;
public interface IParserContext
{
    void StartLabel(int pos, bool quoted);
    void EndLabel(int pos);

    void StartValue(int pos, bool quoted);
    void EndValue(int pos);
    IParserContext? Parent { get; }
    void AddChild(FieldSpan span);

    void StartEscaping();
    void EndEscaping();
    void RemoveEscaping();
    bool Escaping { get; }
    bool Escaped { get; }

    ref FieldSpan Span { get; }
    bool IsComplete { get; }
    void Reset();
}
