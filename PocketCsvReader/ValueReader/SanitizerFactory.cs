using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.FieldParsing;
internal class SanitizerFactory
{
    private CsvProfile Profile { get; }

    public SanitizerFactory(CsvProfile Profile)
        => this.Profile = Profile;

    public ISanitizer Create(ImmutableSequenceCollection? sequences = null, FieldEscaper? fieldEscaper = null)
    {
        var results = new List<ISanitizer>();

        if (Profile.ParserOptimizations.HandleSpecialValues && sequences is not null && !sequences.IsEmpty)
            results.Add(new SequenceSanitizer(sequences!));
        if (Profile.ParserOptimizations.UnescapeChars && fieldEscaper is not null)
            results.Add(new CharEscapeSanitizer(fieldEscaper!));

        return results.Count != 1 ? new FieldSanitizer(results.ToArray()) : results[0];
    }
}
