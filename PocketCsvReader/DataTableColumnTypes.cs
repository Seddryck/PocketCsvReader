namespace PocketCsvReader;

/// <summary>Controls how CSV columns are materialized in a data table.</summary>
public enum DataTableColumnTypes
{
    /// <summary>Stores ordinary columns as strings and the generated row-number column as an integer.</summary>
    Text,

    /// <summary>Stores parsed values using the configured schema. Undeclared columns are strings.</summary>
    Schema
}
