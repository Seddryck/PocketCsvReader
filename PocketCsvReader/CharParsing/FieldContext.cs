using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.CharParsing;

class FieldContext : IParserContext
{
    private FieldSpan _span;
    private bool _escaping;
    private bool _escaped;
    private bool _complete;

    public IParserContext? Parent { get; } 

    public FieldContext()
    { }

    public FieldContext(IParserContext parent)
    {
        Parent = parent;
    }

    public ref FieldSpan Span => ref _span;
    public bool IsComplete => _complete;

    public void StartLabel(int pos, bool quoted)
        => _span.Label = _span.Label with { Start = pos };

    public void EndLabel(int pos)
        => _span.Label = _span.Label with { Length = pos - _span.Label.Start };

    public void StartValue(int pos, bool quoted)
    {
        _span.Value = _span.Value with { Start = quoted ? pos + 1 : pos, WasQuoted = quoted, IsStarted = true };
    }

    public void EndValue(int pos)
    {
        _span.Value = _span.Value with { Length = pos - _span.Value.Start + 1 };
        _complete = true;
    }

    public void EmptyValue()
    {
        _span.Value = _span.Value with { Length = 0 };
        _complete = true;
    }

    public void StartEscaping()
    {
        _escaping = true;
    }

    public void EndEscaping()
    {
        _escaped = true;
        _span.Value = _span.Value with { IsEscaped = true };
        _escaping = false;
    }

    public void RemoveEscaping()
    {
        _escaping = false;
    }

    public bool Escaping => _escaping;
    public bool Escaped => _escaped;

    public void AddChild(FieldSpan child)
        => Span.Children = Span.Children?.Append(child).ToArray() ?? [child];

    public void Reset()
    {
        _span = default;
        _escaping = false;
        _escaped = false;
        _complete = false;
    }
}
