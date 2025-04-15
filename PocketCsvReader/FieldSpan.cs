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
    /// <summary>
    /// Initializes a <see cref="FieldSpan"/> with the specified value span, setting the label to default and no children.
    /// </summary>
    /// <param name="startValue">The starting index of the value span.</param>
    /// <param name="lengthValue">The length of the value span.</param>
    public FieldSpan(int startValue, int lengthValue)
        : this (new SpanInfo(startValue, lengthValue), default, null)
    { }

    /// <summary>
    /// Initializes a <see cref="FieldSpan"/> with the specified value span, quoting, and escaping information.
    /// </summary>
    /// <param name="startValue">The starting index of the value span.</param>
    /// <param name="lengthValue">The length of the value span.</param>
    /// <param name="wasQuoted">Indicates whether the value was quoted.</param>
    /// <param name="isEscaped">Indicates whether the value contains escaped characters.</param>
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
