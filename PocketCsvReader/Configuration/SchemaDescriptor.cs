using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Configuration;
public abstract class SchemaDescriptor
{
    public FieldCollectionDescriptor Fields { get; }

    protected SchemaDescriptor(FieldCollectionDescriptor fields)
        => Fields = fields;

    public class NamedSchemaDescriptor
        : SchemaDescriptor
    {
        public NamedSchemaDescriptor()
            : base(new FieldCollectionDescriptor.NamedFieldCollectionDescriptor())
        { }
    }

    public class IndexedSchemaDescriptor
        : SchemaDescriptor
    {
        public IndexedSchemaDescriptor()
            : base(new FieldCollectionDescriptor.IndexedFieldCollectionDescriptor())
        { }
    }
}
