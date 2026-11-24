namespace PocketCsvReader;

/// <summary>Attempts to parse a complete field from valid UTF-8 bytes.</summary>
/// <remarks>The span is borrowed for the duration of the call.</remarks>
public delegate bool TryParseUtf8<T>(ReadOnlySpan<byte> utf8, out T value);
