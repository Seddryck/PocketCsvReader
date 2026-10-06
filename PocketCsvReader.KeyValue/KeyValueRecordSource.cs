using PocketCsvReader.KeyValue.Configuration;

namespace PocketCsvReader.KeyValue;

internal sealed class KeyValueRecordSource : IRecordSource<KeyValueProfile>
{
    private readonly StreamReader _reader;

    public KeyValueProfile Profile { get; }

    public KeyValueRecordSource(StreamReader reader, KeyValueProfile profile)
        => (_reader, Profile) = (reader, profile);

    public bool IsEndOfFile(out RecordSpan record, out RecordState recordState)
    {
        var line = _reader.ReadLine();
        if (line is null)
        {
            record = new RecordSpan([], []);
            recordState = RecordState.Eof;
            return true;
        }

        var fields = KeyValueRecordParser.Parse(line, Profile.Format);
        record = new RecordSpan(line.AsSpan(), fields);
        recordState = RecordState.Record;
        return _reader.Peek() < 0;
    }

    public async ValueTask<RecordReadResult> ReadAsync(CancellationToken cancellationToken = default)
    {
        var line = await _reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (line is null)
            return new(true, RecordMemory.Empty, RecordState.Eof);

        var fields = KeyValueRecordParser.Parse(line, Profile.Format);
        return new(false, new RecordMemory(line.AsSpan(), fields), RecordState.Record);
    }

    public void Dispose() { }
}
