namespace PocketCsvReader;

/// <summary>Produces records using format-specific framing and field parsing.</summary>
/// <remarks>
/// Returned character memory and field metadata may be borrowed from reusable source storage.
/// Unless an implementation explicitly promises a longer lifetime, consume or copy those views
/// before the next read or disposal. Independently materialized output values can outlive the source.
/// New sources should use buffered framing and document their storage ownership.
/// </remarks>
public interface IRecordSource<out P> : IDisposable
{
    P Profile { get; }
    RecordPosition? CurrentPosition => null;
    bool IsEndOfFile(out RecordSpan record, out RecordState recordState);
    ValueTask<RecordReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var isEndOfFile = IsEndOfFile(out var record, out var state);
        return ValueTask.FromResult(new RecordReadResult(isEndOfFile, record.AsMemory(), state));
    }
}

public readonly record struct RecordReadResult(bool IsEndOfFile, RecordMemory Record, RecordState State);
