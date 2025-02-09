using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Configuration;
public record FieldDescriptor
(
    Type RuntimeType
    , string? Name = null
    , IFormatDescriptor? Format = null
    , ParseFunction? Parse = null
    , ImmutableSequenceCollection? Sequences = null
    , string DataSourceTypeName = ""
)
{ }
