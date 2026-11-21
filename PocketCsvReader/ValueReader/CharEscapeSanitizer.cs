using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.FieldParsing;
internal class CharEscapeSanitizer : FieldValueSanitizer
{
    private FieldEscaper FieldEscaper { get; }

    public CharEscapeSanitizer(FieldEscaper fieldEscaper)
        => FieldEscaper = fieldEscaper;

    internal override NullableSpan Sanitize(NullableSpan value, bool isEscapedField, bool wasQuotedField)
        => isEscapedField ? (NullableSpan)FieldEscaper.EscapeMemory(value.Value) : value;
}

