using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.FieldParsing;
internal class SequenceSanitizer : ISanitizer
{
    private ImmutableSequenceCollection Sequences { get; }

    public SequenceSanitizer(ImmutableSequenceCollection sequences)
        => Sequences = sequences;

    public NullableSpan Sanitize(ReadOnlySpan<char> buffer, bool isEscapedField, bool wasQuotedField)
    {
        if (buffer.Length == 0 || !isEscapedField && !wasQuotedField)
            if (Sequences?.TryGetValue(buffer, out var value) ?? false)
                return value;
        return buffer;
    }
}
