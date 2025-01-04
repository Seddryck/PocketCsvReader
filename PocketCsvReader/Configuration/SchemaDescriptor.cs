using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Configuration;
public abstract class SchemaDescriptor
{
    public FieldCollectionDescriptor Fields { get; internal set; }
    public abstract bool IsMatchingByName { get; }
    public abstract bool IsMatchingByIndex { get; }

    protected SchemaDescriptor(FieldCollectionDescriptor fields)
        => Fields = fields;

    public class NamedSchemaDescriptor
        : SchemaDescriptor
    {
        public NamedSchemaDescriptor()
            : base(new FieldCollectionDescriptor.NamedFieldCollectionDescriptor())
        { }

        public override bool IsMatchingByName => true;
        public override bool IsMatchingByIndex => false;
    }

    public class IndexedSchemaDescriptor
        : SchemaDescriptor
    {
        public IndexedSchemaDescriptor()
            : base(new FieldCollectionDescriptor.IndexedFieldCollectionDescriptor())
        { }
        public override bool IsMatchingByName => false;
        public override bool IsMatchingByIndex => true;
    }
}
