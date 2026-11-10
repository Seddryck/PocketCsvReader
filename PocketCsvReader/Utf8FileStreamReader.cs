using System.Text;

namespace PocketCsvReader;

// Input starts at a parser-confirmed byte offset or immediately after the source BOM.
internal sealed class Utf8FileStreamReader(Stream stream, bool trackByteOffsets, bool restarted)
    : StreamReader(stream, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true)
{
    public bool TrackByteOffsets { get; } = trackByteOffsets;
    public bool Restarted { get; } = restarted;
}

internal enum RecordSelection
{
    Skip,
    Include,
    Stop
}

internal interface IFileHeaderSource
{
    void ReadSeekHeader();
    ValueTask ReadSeekHeaderAsync(CancellationToken cancellationToken);
    void CopySeekHeaderFrom(IFileHeaderSource source);
}
