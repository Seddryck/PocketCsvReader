using System.Text;
using Apache.Arrow;
using Microsoft.Win32.SafeHandles;
using PocketCsvReader.Json;

namespace PocketCsvReader.Arrow.ColumnarJson;

// The reader and each returned record own one reference to the original file.
internal sealed class SharedJsonFile : IDisposable
{
    internal delegate IArrowArray ReadColumn(int index, Field field, int length, JsonFieldBuffer scratch);

    private readonly SafeFileHandle _handle;
    private int _references = 1;
    private int _disposed;

    public FileStream Stream { get; }
    public Encoding Encoding { get; }
    public long StartOffset { get; }

    private SharedJsonFile(FileStream stream, SafeFileHandle handle, Encoding encoding, int startOffset)
    {
        Stream = stream;
        _handle = handle;
        Encoding = encoding;
        StartOffset = startOffset;
        stream.Position = startOffset;
    }

    public static async ValueTask<SharedJsonFile> OpenAsync(
        string filename, bool asynchronous, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filename);
        cancellationToken.ThrowIfCancellationRequested();
        var options = asynchronous ? FileOptions.Asynchronous : FileOptions.None;
        var stream = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096, options);
        try
        {
            var handle = stream.SafeFileHandle;
            var prefix = new byte[4];
            var read = await ReadPrefixAsync(handle, prefix, asynchronous, cancellationToken).ConfigureAwait(false);
            var (encoding, offset) = DetectEncoding(prefix.AsSpan(0, read));
            return new SharedJsonFile(stream, handle, encoding, offset);
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public IJsonColumnSource RetainColumns(ReadColumn read)
    {
        // The scanning reader still owns its reference while handing off a record.
        var source = new FileColumns(this, read);
        Interlocked.Increment(ref _references);
        return source;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            Release();
    }

    private void Release()
    {
        if (Interlocked.Decrement(ref _references) == 0)
            Stream.Dispose();
    }

    private static async ValueTask<int> ReadPrefixAsync(
        SafeFileHandle handle, Memory<byte> prefix, bool asynchronous, CancellationToken cancellationToken)
    {
        var total = 0;
        while (total < prefix.Length)
        {
            var read = asynchronous
                ? await RandomAccess.ReadAsync(handle, prefix[total..], total, cancellationToken).ConfigureAwait(false)
                : RandomAccess.Read(handle, prefix.Span[total..], total);
            if (read == 0)
                break;
            total += read;
        }
        return total;
    }

    // Use encodings without preambles because the BOM has already been consumed.
    private static (Encoding Encoding, int Offset) DetectEncoding(ReadOnlySpan<byte> prefix)
        => prefix switch
        {
            [0xEF, 0xBB, 0xBF, ..] => (new UTF8Encoding(false, true), 3),
            [0xFF, 0xFE, 0, 0, ..] => (new UTF32Encoding(false, false, true), 4),
            [0, 0, 0xFE, 0xFF, ..] => (new UTF32Encoding(true, false, true), 4),
            [0xFF, 0xFE, ..] => (new UnicodeEncoding(false, false, true), 2),
            [0xFE, 0xFF, ..] => (new UnicodeEncoding(true, false, true), 2),
            _ => (new UTF8Encoding(false, true), 0),
        };

    // Position-independent reads allow retained records to share the scanning file.
    public int Read(long offset, Span<byte> buffer) => RandomAccess.Read(_handle, buffer, offset);

    private sealed class FileColumns(SharedJsonFile file, ReadColumn read) : IJsonColumnSource
    {
        private ReadColumn? _read = read;

        IArrowArray IJsonColumnSource.ReadColumn(int index, Field field, int length, JsonFieldBuffer scratch)
            => (_read ?? throw new ObjectDisposedException(nameof(FileColumns)))(index, field, length, scratch);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _read, null) is not null)
                file.Release();
        }
    }
}
