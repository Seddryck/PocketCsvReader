using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.FieldParsing;

internal class FieldSanitizer : FieldValueSanitizer
{
    protected FieldValueSanitizer[] Sanitizers { get; } = [];

    public FieldSanitizer(FieldValueSanitizer[] sanitizers)
        => Sanitizers = sanitizers;

    internal override NullableSpan Sanitize(NullableSpan value, bool isEscapedField, bool wasQuotedField)
    {
        foreach (var sanitizer in Sanitizers)
        {
            if (value.HasValue)
                value = sanitizer.Sanitize(value, isEscapedField, wasQuotedField);
        }

        return value;
    }
}




