using NUnit.Framework;
using PocketCsvReader.Configuration;
using PocketCsvReader.FieldParsing;

namespace PocketCsvReader.Testing;

public class RecordSourceTest
{
    [Test]
    public void BaseDataReader_AcceptsRecordSourceWithoutRecordParser()
    {
        using var reader = new StubDataReader(new MemoryStream(), new StubProfile());

        Assert.That(reader.Read(), Is.True);
        Assert.That(reader.GetString(0), Is.EqualTo("value"));
        Assert.That(reader.Read(), Is.False);
        Assert.That(reader.Read(), Is.False);
        Assert.That(reader.RecordSourceCreationCount, Is.EqualTo(1));
    }

    private sealed class StubProfile : IProfile
    {
        public SchemaDescriptor? Schema => null;
        public ResourceDescriptor? Resource => new(Encoding: "utf-8");
        public RuntimeParsersDescriptor? Parsers => null;
        public StreamInitializationOptions StreamInitialization => StreamInitializationOptions.ForwardOnly;
    }

    private sealed class StubDataReader : BaseDataReader<StubProfile>
    {
        public int RecordSourceCreationCount { get; private set; }

        public StubDataReader(Stream stream, StubProfile profile)
            : base(stream, profile, new StringMapper())
        { }

        public override int FieldCount => 1;
        public override string GetRawString(int i) => Record!.Slice(i).ToString();
        protected override NullableSpan GetValueOrThrow(int i) => Record!.Slice(i).Span;

        protected override IRecordSource<StubProfile> CreateRecordSource(StreamReader reader, StubProfile profile)
        {
            RecordSourceCreationCount++;
            return new StubRecordSource(profile);
        }

        protected override bool ReadCore()
        {
            IsEof = RecordSource!.IsEndOfFile(out var record, out var state);
            if (state == RecordState.Eof)
                return false;
            Record = record.AsMemory();
            Fields = ["Value"];
            return true;
        }
    }

    private sealed class StubRecordSource : IRecordSource<StubProfile>
    {
        private bool _read;
        public StubProfile Profile { get; }

        public StubRecordSource(StubProfile profile)
            => Profile = profile;

        public bool IsEndOfFile(out RecordSpan record, out RecordState recordState)
        {
            if (_read)
            {
                record = new RecordSpan([], []);
                recordState = RecordState.Eof;
                return true;
            }

            _read = true;
            record = new RecordSpan("value", [new FieldSpan(0, 5)]);
            recordState = RecordState.Record;
            return false;
        }

        public void Dispose() { }
    }
}
