using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader;
public record struct FieldSpan
(
    SpanInfo Value,
    SpanInfo Label,
    FieldSpan[]? Children = null
)
{
    public FieldSpan(int startValue, int lengthValue)
        : this (new SpanInfo(startValue, lengthValue), default, null)
    { }

    public FieldSpan(int startValue, int lengthValue, bool wasQuoted, bool isEscaped)
        : this(new SpanInfo(startValue, lengthValue, wasQuoted, isEscaped), default, null)
    { }

    public bool IsEmpty => Value.Length == 0;
}

public record struct SpanInfo
(
    int Start,
    int Length,
    bool WasQuoted = false,
    bool IsEscaped = false,
    bool IsStarted = false
)
{ }
