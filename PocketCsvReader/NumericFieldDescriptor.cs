using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PocketCsvReader.Configuration;

namespace PocketCsvReader;
public record NumericFieldDescriptor 
(
    Type RuntimeType
    , string? Name = null
    , string? Format = null
    , ImmutableSequenceCollection? Sequences = null
    , char ? DecimalChar = null
    , char ? GroupChar = null
) : FieldDescriptor(RuntimeType, Name, Format, Sequences)
{ }
