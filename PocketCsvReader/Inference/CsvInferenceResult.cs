using PocketCsvReader.Configuration;

namespace PocketCsvReader;

public sealed record CsvInferenceResult(
    DialectDescriptor Dialect,
    SchemaDescriptor Schema,
    int SampledRecordCount,
    IReadOnlyList<CsvInferredColumn> Columns,
    IReadOnlyList<InferenceDiagnostic> Diagnostics);

public sealed record CsvInferredColumn(
    string Name,
    Type RuntimeType,
    bool IsNullable,
    int ValueCount,
    int NullCount,
    int EmptyCount,
    int MissingCount);

public sealed record InferenceDiagnostic(
    string Code,
    string Message,
    InferenceDiagnosticSeverity Severity = InferenceDiagnosticSeverity.Information,
    double? Confidence = null);

public enum InferenceDiagnosticSeverity
{
    Information,
    Warning
}
