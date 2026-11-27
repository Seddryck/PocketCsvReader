using System.Data;

namespace PocketCsvReader;

/// <summary>Provides optional native UTF-8 access to logical text fields.</summary>
public interface IUtf8DataRecord : IDataRecord
{
    /// <summary>Indicates whether a column may provide native text for this result set.</summary>
    /// <remarks>
    /// Call once the reader's schema is available. A false result lets consumers skip
    /// probing a column that always needs fallback. A true result still requires checking each value.
    /// </remarks>
    bool CanGetUtf8Span(int ordinal) => true;

    /// <summary>Attempts to borrow the current field's logical text as valid UTF-8.</summary>
    /// <param name="ordinal">The zero-based field ordinal.</param>
    /// <param name="value">The UTF-8 text when available, including an empty span for an empty string.</param>
    /// <returns>
    /// True for an available, non-null text value. False for nulls or when native text is unavailable.
    /// Use <see cref="IDataRecord.IsDBNull(int)"/> to distinguish nulls on fallback.
    /// </returns>
    /// <remarks>
    /// The span is borrowed. Consume or copy it before calling this method again, advancing,
    /// closing, or disposing the reader. CSV quoting and configured transformations are reflected
    /// in the result when supported.
    /// </remarks>
    bool TryGetUtf8Span(int ordinal, out ReadOnlySpan<byte> value);
}
