namespace PocketCsvReader;

/// <summary>Publishes metadata with an explicit owned or next-read lifetime.</summary>
internal sealed class FieldMetadataBuffer
{
    private FieldSpan[] _fields = [];

    // Only reader-internal consumers may retain this until the next publication.
    public FieldSpan[] PublishBorrowed(ReadOnlySpan<FieldSpan> fields)
    {
        if (_fields.Length != fields.Length)
            _fields = new FieldSpan[fields.Length];
        fields.CopyTo(_fields);
        return _fields;
    }

    // Public parser results keep independent top-level metadata after advancement.
    public static FieldSpan[] CopyOwned(ReadOnlySpan<FieldSpan> fields) => fields.ToArray();
}
