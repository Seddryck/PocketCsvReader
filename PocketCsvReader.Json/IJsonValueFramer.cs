namespace PocketCsvReader.Json;

internal interface IJsonValueFramer : IRecordFramer
{
    int Delimiter { get; }
    void Prepare(char first, long startPosition);
}
