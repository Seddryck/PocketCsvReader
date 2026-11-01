using System.Data;

namespace PocketCsvReader;

/// <summary>
/// Represents a data reader that can advance through records asynchronously.
/// </summary>
/// <remarks>
/// <see cref="IDataReader"/> itself is synchronous. This interface preserves that
/// contract while adding asynchronous record advancement and disposal.
/// </remarks>
public interface IAsyncDataReader : IDataReader, IAsyncDisposable
{
    /// <summary>The current record source position, when explicitly enabled.</summary>
    /// <value>The current source position, or null when unavailable.</value>
    RecordPosition? CurrentPosition => null;

    /// <summary>Advances the reader to the next record asynchronously.</summary>
    Task<bool> ReadAsync(CancellationToken cancellationToken = default);
}
