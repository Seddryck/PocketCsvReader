namespace PocketCsvReader.Markdown;

public sealed class MarkdownReader(MarkdownProfile profile) : FlatFileReader<MarkdownProfile, MarkdownDataReader>(profile)
{
    protected override int StreamBufferSize => Profile.ParserOptimizations.BufferSize;

    protected override MarkdownDataReader CreateDataReader(Stream stream) => new(stream, Profile);
}
