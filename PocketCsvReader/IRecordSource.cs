namespace PocketCsvReader;

public interface IRecordSource<out P> : IDisposable
{
    P Profile { get; }
    bool IsEndOfFile(out RecordSpan record, out RecordState recordState);
}
