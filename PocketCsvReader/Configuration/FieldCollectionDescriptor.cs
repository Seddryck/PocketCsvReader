using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Configuration;
public abstract class FieldCollectionDescriptor : IEnumerable<FieldDescriptor>
{
    public abstract void Add(FieldDescriptor field);

    public abstract int Length { get; }

    public abstract FieldDescriptor this[int index] { get; }
    public abstract FieldDescriptor this[string name] { get; }

    public abstract bool TryGetValue(string name, [NotNullWhen(true)] out FieldDescriptor? field);

    public abstract IEnumerator<FieldDescriptor> GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public class NamedFieldCollectionDescriptor : FieldCollectionDescriptor
    {
        private Dictionary<string, FieldDescriptor> _dict = [];

        public override int Length => _dict.Count;

        public override void Add(FieldDescriptor field)
        {
            if (string.IsNullOrWhiteSpace(field.Name))
                throw new ArgumentException($"Empty or null names are not allowed.", nameof(field.Name));
            if (!_dict.TryAdd(field.Name, field))
                throw new DuplicateNameException($"Duplicated names are not allowed. A field named '{field.Name}' already exists.");
        }

        public override FieldDescriptor this[string name]
        {
            get => _dict[name];
        }

        public override FieldDescriptor this[int index]
        {
            get => throw new NotSupportedException();
        }

        public override bool TryGetValue(string name, [NotNullWhen(true)] out FieldDescriptor? field)
            => _dict.TryGetValue(name, out field);

        public override IEnumerator<FieldDescriptor> GetEnumerator() => _dict.Values.GetEnumerator();
    }

    public class IndexedFieldCollectionDescriptor : FieldCollectionDescriptor
    {
        private List<FieldDescriptor> _list = [];

        public override int Length => _list.Count;

        public override void Add(FieldDescriptor field)
        {
            _list.Add(field);
        }

        public override FieldDescriptor this[int index]
        {
            get => _list[index];
        }

        public override FieldDescriptor this[string name]
        {
            get => throw new NotSupportedException();
        }

        public override bool TryGetValue(string name, [NotNullWhen(true)] out FieldDescriptor? field)
        {
            field = _list.FirstOrDefault(f => f.Name == name);
            return field is not null;
        }

        public override IEnumerator<FieldDescriptor> GetEnumerator() => _list.GetEnumerator();
    }
}


