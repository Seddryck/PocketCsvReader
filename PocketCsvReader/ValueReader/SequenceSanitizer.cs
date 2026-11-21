using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.FieldParsing;
internal class SequenceSanitizer : FieldValueSanitizer
{
    private ImmutableSequenceCollection Sequences { get; }

    public SequenceSanitizer(ImmutableSequenceCollection sequences)
        => Sequences = sequences;

    internal override NullableSpan Sanitize(NullableSpan value, bool isEscapedField, bool wasQuotedField)
    {
        var buffer = value.Value;
        if (buffer.Length == 0 || (!isEscapedField && !wasQuotedField))
        {
            if (Sequences.TryGetValue(buffer, out var replacement))
                return replacement;
        }

        return value;
    }
}
