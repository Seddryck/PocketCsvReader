namespace PocketCsvReader;

internal interface IFieldValueReader
{
    T GetFieldValue<T>(int ordinal);
}
