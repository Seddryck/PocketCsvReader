using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.FieldParsing;

internal class FieldSanitizer : ISanitizer
{
    protected ISanitizer[] Sanitizers { get; } = [];

    public FieldSanitizer(ISanitizer[] sanitizers)
        => Sanitizers = sanitizers;

    public NullableSpan Sanitize(ReadOnlySpan<char> buffer, bool isEscapedField, bool wasQuotedField)
    {
        if (Sanitizers.Length == 0)
            return buffer;

        var result = new NullableSpan(buffer);
        foreach (var sanitizer in Sanitizers)
            if (result.HasValue)
                result = sanitizer.Sanitize(result.Value, isEscapedField, wasQuotedField);
        return result;
    }
}




