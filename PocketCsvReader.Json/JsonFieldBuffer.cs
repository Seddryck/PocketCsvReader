using System.Text;
using System.Runtime.InteropServices;

namespace PocketCsvReader.Json;

// Metadata is borrowed until the next read, just like the record's character buffer.
// Parse into scratch storage first so invalid input cannot partially overwrite it.
internal sealed class JsonFieldBuffer
{
    public List<FieldSpan> Scratch { get; } = [];
    private readonly FieldMetadataBuffer _fields = new();
    private StringBuilder? _decodedString;

    public StringBuilder GetStringBuilder()
    {
        // Do not retain a very large decoding buffer after an exceptional value.
        if (_decodedString is null || _decodedString.Capacity > 4096)
            _decodedString = new StringBuilder();
        else
            _decodedString.Clear();
        return _decodedString;
    }

    public FieldSpan[] Complete()
    {
        return _fields.PublishBorrowed(CollectionsMarshal.AsSpan(Scratch));
    }
}
