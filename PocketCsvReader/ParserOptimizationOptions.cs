using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader;
public record ParserOptimizationOptions
(
    bool NoTextQualifier = false,
    bool UnescapeChars = true,
    bool HandleSpecialValues = true,
    bool RowCountAtStart = false,
    bool ExtendIncompleteRecords = true,
    bool ReadAhead = true,
    int BufferSize = 4096,
    PoolString? PoolString = null,
    bool LookupTableChar = true
) {
    /// <summary>Enables source positions. Disabled by default; configure before creating the reader.</summary>
    /// <value>True to track source positions; otherwise false.</value>
    public bool TrackPosition { get; init; }

    /// <summary>Reuses strings for eligible native UTF-8 CSV fields by looking up their bytes before decoding.</summary>
    /// <remarks>The cache is bounded and belongs to one reader. A configured <see cref="PoolString"/> takes precedence.</remarks>
    /// <value><see langword="true"/> to enable the cache. The default is <see langword="false"/>.</value>
    public bool PoolUtf8Strings { get; init; }
}
