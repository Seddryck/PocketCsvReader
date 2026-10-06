namespace PocketCsvReader;

public interface IRecordSource<out P> : IDisposable
{
    P Profile { get; }
    bool IsEndOfFile(out RecordSpan record, out RecordState recordState);
    ValueTask<RecordReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var isEndOfFile = IsEndOfFile(out var record, out var state);
        return ValueTask.FromResult(new RecordReadResult(isEndOfFile, record.AsMemory(), state));
    }
}

public readonly record struct RecordReadResult(bool IsEndOfFile, RecordMemory Record, RecordState State);
