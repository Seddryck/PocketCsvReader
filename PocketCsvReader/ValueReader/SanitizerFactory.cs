using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.FieldParsing;
internal class SanitizerFactory
{
    private ParserOptimizationOptions ParserOptimizations { get; }

    public SanitizerFactory(CsvProfile profile)
        : this(profile.ParserOptimizations)
    { }

    public SanitizerFactory(ParserOptimizationOptions parserOptimizations)
        => ParserOptimizations = parserOptimizations;

    public ISanitizer Create(ImmutableSequenceCollection? sequences = null, FieldEscaper? fieldEscaper = null)
    {
        var results = new List<ISanitizer>();

        if (ParserOptimizations.HandleSpecialValues && sequences is not null && !sequences.IsEmpty)
            results.Add(new SequenceSanitizer(sequences!));
        if (ParserOptimizations.UnescapeChars && fieldEscaper is not null)
            results.Add(new CharEscapeSanitizer(fieldEscaper!));

        return results.Count != 1 ? new FieldSanitizer(results.ToArray()) : results[0];
    }
}
