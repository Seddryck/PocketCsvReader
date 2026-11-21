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

    internal override NullableSpan Sanitize(NullableSpan input, bool isEscapedField, bool wasQuotedField)
    {
        var buffer = input.Value;
        if (buffer.Length == 0 || (!isEscapedField && !wasQuotedField))
        {
            if (Sequences?.TryGetValue(buffer, out var value) ?? false)
                return value;
        }

        return input;
    }
}
