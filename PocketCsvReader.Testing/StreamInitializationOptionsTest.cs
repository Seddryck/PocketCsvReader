using System.Text;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing;

public class StreamInitializationOptionsTest
{
    [Test]
    public void ForwardOnly_DoesNotProbeOrRewindSeekableStream()
    {
        var bytes = Encoding.UTF8.GetBytes("1,Ada\n2,Grace\n");
        using var stream = new TrackingStream(bytes);
        var profile = new CsvProfile(new DialectDescriptorBuilder()
            .WithLineTerminator("\n")
            .WithoutHeader()
            .Build())
        {
            StreamInitialization = StreamInitializationOptions.ForwardOnly
        };
        using var reader = new CsvReader(profile).ToDataReader(stream);

        while (reader.Read())
            _ = reader.GetString(0);

        Assert.Multiple(() =>
        {
            Assert.That(stream.BackwardSeeks, Is.Zero);
            Assert.That(stream.BytesRead, Is.EqualTo(bytes.Length));
        });
    }

    private sealed class TrackingStream : Stream
    {
        private readonly MemoryStream _inner;
        public int BackwardSeeks { get; private set; }
        public long BytesRead { get; private set; }

        public TrackingStream(byte[] bytes)
            => _inner = new MemoryStream(bytes);

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position
        {
            get => _inner.Position;
            set
            {
                if (value < _inner.Position)
                    BackwardSeeks++;
                _inner.Position = value;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _inner.Read(buffer, offset, count);
            BytesRead += read;
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            var read = _inner.Read(buffer);
            BytesRead += read;
            return read;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            var previous = _inner.Position;
            var position = _inner.Seek(offset, origin);
            if (position < previous)
                BackwardSeeks++;
            return position;
        }

        public override void Flush() { }
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
