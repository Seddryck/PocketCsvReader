using System.Text;

namespace PocketCsvReader;

internal static class CsvStreamSource
{
    public static IRecordSource<CsvProfile>? Create(Stream stream, CsvProfile profile, bool leaveOpen = false)
    {
        var options = profile.StreamInitialization;
        if (options.InputMode == CsvInputMode.DecodedChars && string.IsNullOrEmpty(profile.Resource?.Encoding))
            return null;
        if (!Enum.IsDefined(options.InputMode))
            throw new ArgumentOutOfRangeException(nameof(options.InputMode));

        // Read a bounded prefix even from forward-only streams. Replay any bytes
        // that were not consumed as a BOM, including on short reads.
        var prefix = new byte[4];
        int count = 0;
        while (count < prefix.Length)
        {
            int read = stream.Read(prefix, count, prefix.Length - count);
            if (read == 0) break;
            count += read;
        }
        return CreateWithPrefix(stream, profile, prefix, count, leaveOpen);
    }

    public static async ValueTask<IRecordSource<CsvProfile>?> CreateAsync(Stream stream, CsvProfile profile, CancellationToken cancellationToken)
    {
        if (profile.StreamInitialization.InputMode == CsvInputMode.DecodedChars && string.IsNullOrEmpty(profile.Resource?.Encoding)) return null;
        var prefix = new byte[4];
        int count = 0;
        while (count < prefix.Length)
        {
            int read = await stream.ReadAsync(prefix.AsMemory(count), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            count += read;
        }
        return CreateWithPrefix(stream, profile, prefix, count, leaveOpen: true);
    }

    private static IRecordSource<CsvProfile> CreateWithPrefix(Stream stream, CsvProfile profile, byte[] prefix, int count, bool leaveOpen)
    {
        var options = profile.StreamInitialization;
        if (!Enum.IsDefined(options.InputMode)) throw new ArgumentOutOfRangeException(nameof(options.InputMode));
        Encoding encoding = string.IsNullOrEmpty(profile.Resource?.Encoding)
            ? Encoding.UTF8 : Encoding.GetEncoding(profile.Resource.Encoding);
        if (string.IsNullOrEmpty(profile.Resource?.Encoding) && options.ProbeEncoding)
        {
            // Longest BOM first, since UTF-32 LE starts with the UTF-16 LE BOM.
            foreach (var candidate in new[] { Encoding.UTF32, new UTF32Encoding(true, true), Encoding.UTF8, Encoding.Unicode, Encoding.BigEndianUnicode })
            {
                if (prefix.AsSpan(0, count).StartsWith(candidate.Preamble))
                {
                    encoding = candidate;
                    break;
                }
            }
        }
        bool utf8 = encoding.CodePage == Encoding.UTF8.CodePage;
        if (!utf8 && options.InputMode == CsvInputMode.Utf8Bytes)
            throw new NotSupportedException($"UTF-8 byte parsing requires UTF-8 input; resolved encoding is '{encoding.WebName}'.");
        bool supported = Utf8RecordSource.Supports(profile);
        if (!supported && options.InputMode == CsvInputMode.Utf8Bytes)
            throw new NotSupportedException("UTF-8 byte parsing requires ASCII CSV syntax without array fields or a bad-data policy. Use Auto or DecodedChars for this profile.");

        var preamble = encoding.Preamble;
        bool useBytes = options.InputMode != CsvInputMode.DecodedChars && utf8 && supported;
        // The byte source consumes the BOM here; StreamReader consumes its own
        // preamble, so replay it on the character path rather than stripping twice.
        int skip = useBytes && preamble.Length > 0 && prefix.AsSpan(0, count).StartsWith(preamble) ? preamble.Length : 0;
        var replay = new PrefixStream(prefix.AsMemory(skip, count - skip), stream, leaveOpen);
        return useBytes ? new Utf8RecordSource(replay, profile)
            : new OwnedCharacterSource(replay, encoding, profile);
    }

    private sealed class OwnedCharacterSource : RecordParser
    {
        private readonly StreamReader _reader;
        public OwnedCharacterSource(Stream stream, Encoding encoding, CsvProfile profile)
            : this(new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: false), profile) { }
        private OwnedCharacterSource(StreamReader reader, CsvProfile profile) : base(reader, profile) => _reader = reader;
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) _reader.Dispose();
        }
    }

    private sealed class PrefixStream(ReadOnlyMemory<byte> prefix, Stream stream, bool leaveOpen) : Stream
    {
        private ReadOnlyMemory<byte> _prefix = prefix;
        public override bool CanRead => stream.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            int count = Math.Min(buffer.Length, _prefix.Length);
            _prefix.Span[..count].CopyTo(buffer);
            _prefix = _prefix[count..];
            // The character buffers interpret a short StreamReader read as EOF.
            // Fill across the replay boundary and underlying short reads.
            while (count < buffer.Length)
            {
                int read = stream.Read(buffer[count..]);
                if (read == 0) break;
                count += read;
            }
            return count;
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int count = Math.Min(buffer.Length, _prefix.Length);
            _prefix[..count].CopyTo(buffer);
            _prefix = _prefix[count..];
            while (count < buffer.Length)
            {
                int read = await stream.ReadAsync(buffer[count..], cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                count += read;
            }
            return count;
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !leaveOpen) stream.Dispose();
            base.Dispose(disposing);
        }
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
