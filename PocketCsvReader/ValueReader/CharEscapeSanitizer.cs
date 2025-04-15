using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.FieldParsing;
internal class CharEscapeSanitizer : ISanitizer
{
    private FieldEscaper FieldEscaper { get; }

    public CharEscapeSanitizer(FieldEscaper fieldEscaper)
        => FieldEscaper = fieldEscaper;

    public NullableSpan Sanitize(ReadOnlySpan<char> buffer, bool isEscapedField, bool wasQuotedField)
        => isEscapedField ? FieldEscaper.Escape(buffer) : buffer;
}

