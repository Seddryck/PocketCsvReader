using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.FieldParsing;
public interface ISanitizer
{
    NullableSpan Sanitize(ReadOnlySpan<char> buffer, bool isEscapedField, bool wasQuotedField);
}
