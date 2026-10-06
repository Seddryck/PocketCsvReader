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
    /// <summary>
    /// Advances the reader to the next record asynchronously.
    /// </summary>
    Task<bool> ReadAsync(CancellationToken cancellationToken = default);
}
