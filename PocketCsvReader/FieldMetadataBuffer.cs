namespace PocketCsvReader;

/// <summary>Publishes metadata with an explicit owned or next-read lifetime.</summary>
internal sealed class FieldMetadataBuffer
{
    private FieldSpan[] _fields = [];

    // Only reader-internal consumers may retain this until the next publication.
    public FieldSpan[] PublishBorrowed(ReadOnlySpan<FieldSpan> fields)
    {
        var storage = GetBorrowedStorage(fields.Length);
        fields.CopyTo(storage);
        return storage;
    }

    // Callers writing directly must populate every field before returning it.
    public FieldSpan[] GetBorrowedStorage(int length)
    {
        if (_fields.Length != length)
            _fields = new FieldSpan[length];
        return _fields;
    }

    // Public parser results keep independent top-level metadata after advancement.
    public static FieldSpan[] CopyOwned(ReadOnlySpan<FieldSpan> fields) => fields.ToArray();
}
