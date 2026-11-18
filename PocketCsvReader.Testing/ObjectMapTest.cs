using System.Globalization;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing;

public class ObjectMapTest
{
    private DirectoryInfo _directory = null!;

    [SetUp]
    public void CreateDirectory() => _directory = Directory.CreateTempSubdirectory("pocketcsv-object-map-");

    [TearDown]
    public void DeleteDirectory()
    {
        foreach (var file in _directory.EnumerateFiles())
            file.Delete();
        _directory.Delete();
    }

    [Test]
    public void To_FactoryMap_BindsReorderedHeadersAndUsesFieldFormat()
    {
        var path = WriteCsv("people.csv", "Full Name,Extra,ID,Born,Extra\nAda Lovelace,a,101,10/12/1815,b\n");
        var csv = ReaderBuilder()
            .WithSchema(s => s.Named().WithField<int>("ID").WithField<string>("Full Name")
                .WithTemporalField<DateOnly>("Born", f => f.WithFormat("dd/MM/yyyy")))
            .Build();
        var map = ObjectMap<Person>.Create(() => new Person())
            .Map("ID", p => p.Id)
            .Map("Full Name", p => p.Name)
            .Map("Born", p => p.BirthDate);

        IEnumerable<Person> people = csv.To(path, map);
        var person = people.Single();

        Assert.That(person.Id, Is.EqualTo(101));
        Assert.That(person.Name, Is.EqualTo("Ada Lovelace"));
        Assert.That(person.BirthDate, Is.EqualTo(new DateOnly(1815, 12, 10)));
    }

    [Test]
    public void To_KeyedMap_UpdatesAndYieldsTheOriginalPeopleInContactOrder()
    {
        var personPath = WriteCsv("people.csv", "ID,Full Name\n101,Ada\n102,Grace\n103,Katherine\n");
        var contactPath = WriteCsv("contact.csv", "Email,ID,Full Name\ngrace@example.com,x102,Other\nada@example.com,x101,Other\n");
        var csv = ReaderBuilder()
            .WithParsers(p => p.WithParser((ReadOnlySpan<char> value) => int.Parse(value.TrimStart('x'), CultureInfo.InvariantCulture)))
            .Build();
        var personMap = ObjectMap<Person>.Create(() => new Person())
            .Map("ID", p => p.Id)
            .Map("Full Name", p => p.Name);
        var peopleById = csv.To(personPath, personMap).ToDictionary(p => p.Id);
        var contactMap = ObjectMap<Person>.Create("ID", (int id) => peopleById[id])
            .Map("Email", p => p.EmailAddress);

        var contactPeople = csv.To(contactPath, contactMap);
        Assert.That(peopleById.Values.Select(p => p.EmailAddress), Is.All.EqualTo("original@example.com"));
        var updatedPeople = contactPeople.ToArray();

        Assert.That(updatedPeople, Has.Length.EqualTo(2));
        Assert.That(updatedPeople[0], Is.SameAs(peopleById[102]));
        Assert.That(updatedPeople[1], Is.SameAs(peopleById[101]));
        Assert.That(peopleById[102].EmailAddress, Is.EqualTo("grace@example.com"));
        Assert.That(peopleById[101].EmailAddress, Is.EqualTo("ada@example.com"));
        Assert.That(peopleById[103].EmailAddress, Is.EqualTo("original@example.com"));
        Assert.That(updatedPeople.Select(p => p.Name), Is.EqualTo(new[] { "Grace", "Ada" }));
        Assert.That(updatedPeople.Select(p => p.Id), Is.EqualTo(new[] { 102, 101 }));
    }

    [TestCase("Other,Email\n1,new@example.com\n", "ID", "not found")]
    [TestCase("ID,ID,Email\n1,2,new@example.com\n", "ID", "ambiguous")]
    [TestCase("ID,Other\n", "Email", "not found")]
    [TestCase("ID,Email,Email\n1,new@example.com,duplicate@example.com\n", "Email", "ambiguous")]
    public void To_KeyedMap_ValidatesHeadersBeforeResolvingTargets(string content, string header, string error)
    {
        var path = WriteCsv("contact.csv", content);
        var csv = ReaderBuilder().Build();
        var map = ObjectMap<Person>.Create<int>("ID", _ => throw new AssertionException("The resolver must not run."))
            .Map("Email", p => p.EmailAddress);

        var exception = Assert.Throws<InvalidDataException>(() => csv.To(path, map).ToArray());

        Assert.That(exception!.Message, Does.Contain($"'{header}'").And.Contain(error));
    }

    [Test]
    public void To_ExplicitNullMapper_StillUsesDefaultConstructorMapping()
    {
        var path = WriteCsv("people.csv", "ID,Full Name\n101,Ada\n");
        var csv = ReaderBuilder().Build();

        var person = csv.To<ConstructorPerson>(path, null).Single();

        Assert.That(person, Is.EqualTo(new ConstructorPerson(101, "Ada")));
    }

    [Test]
    public void To_ReusedMap_RebindsHeadersForEachFileAndEnumeration()
    {
        var firstPath = WriteCsv("first.csv", "ID,Full Name\n101,Ada\n103,Katherine\n");
        var secondPath = WriteCsv("second.csv", "Full Name,ID\nGrace,102\n");
        var csv = ReaderBuilder().Build();
        var map = ObjectMap<Person>.Create(() => new Person())
            .Map("ID", p => p.Id).Map("Full Name", p => p.Name);

        var firstRows = csv.To(firstPath, map);
        using var first = firstRows.GetEnumerator();
        using var second = csv.To(secondPath, map).GetEnumerator();
        Assert.That(first.MoveNext(), Is.True);
        Assert.That(second.MoveNext(), Is.True);
        Assert.That(first.Current.Id, Is.EqualTo(101));
        Assert.That(first.Current.Name, Is.EqualTo("Ada"));
        Assert.That(second.Current.Id, Is.EqualTo(102));
        Assert.That(second.Current.Name, Is.EqualTo("Grace"));
        Assert.That(firstRows.First(), Is.Not.SameAs(first.Current));
        Assert.That(first.MoveNext(), Is.True);
        Assert.That(first.Current.Id, Is.EqualTo(103));
        Assert.That(first.Current.Name, Is.EqualTo("Katherine"));
    }

    [Test]
    public void To_PropertyMap_UsesRuntimeParserAndNullPolicy()
    {
        var path = WriteCsv("people.csv", "Full Name\nAda\nNULL\n");
        var csv = ReaderBuilder()
            .WithDialect(d => d.WithNullSequence("NULL"))
            .WithParsers(p => p.WithParser((ReadOnlySpan<char> text) => text.ToString().ToUpperInvariant()))
            .Build();
        var map = ObjectMap<Person>.Create(() => new Person()).Map("Full Name", p => p.Name);

        var people = csv.To(path, map).ToArray();

        Assert.That(people[0].Name, Is.EqualTo("ADA"));
        Assert.That(people[1].Name, Is.Null);
    }

    [Test]
    public void To_FiltersAndWindow_OnlyCreateAcceptedTargets()
    {
        var path = WriteCsv("people.csv", "ID\n1\n2\n3\n4\n");
        var csv = ReaderBuilder().WithFilter<int>("ID", id => id % 2 == 0).WithSkip(1).WithTake(1).Build();
        var resolved = new List<int>();
        var map = ObjectMap<Person>.Create("ID", (int id) =>
        {
            resolved.Add(id);
            return new Person { Id = id };
        });

        Assert.That(csv.To(path, map).Single().Id, Is.EqualTo(4));
        Assert.That(resolved, Is.EqualTo(new[] { 4 }));
    }

    [Test]
    public void To_EnumerationIsLazyAndDisposesFileOnEarlyExit()
    {
        var path = WriteCsv("people.csv", "ID\n1\n2\n");
        var map = ObjectMap<Person>.Create(() => new Person()).Map("ID", p => p.Id);
        var people = ReaderBuilder().Build().To(path, map);
        File.WriteAllText(path, "ID\n3\n4\n");

        using (var iterator = people.GetEnumerator())
        {
            Assert.That(iterator.MoveNext(), Is.True);
            Assert.That(iterator.Current.Id, Is.EqualTo(3));
        }

        using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Test]
    public void To_UnknownKey_PropagatesFailureAndDisposesFile()
    {
        var path = WriteCsv("contact.csv", "ID,Email\n99,new@example.com\n");
        var peopleById = new Dictionary<int, Person>();
        var map = ObjectMap<Person>.Create("ID", (int id) => peopleById[id]).Map("Email", p => p.EmailAddress);

        Assert.Throws<KeyNotFoundException>(() => ReaderBuilder().Build().To(path, map).ToArray());
        using var exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Test]
    public void To_RepeatedKey_UpdatesTheSameInstanceAndPreservesUpdatesBeforeFailure()
    {
        var path = WriteCsv("contact.csv", "ID,Email\n101,first@example.com\n101,second@example.com\n999,unknown@example.com\n");
        var person = new Person { Id = 101, Name = "Ada" };
        var peopleById = new Dictionary<int, Person> { [101] = person };
        var map = ObjectMap<Person>.Create("ID", (int id) => peopleById[id]).Map("Email", p => p.EmailAddress);
        using var iterator = ReaderBuilder().Build().To(path, map).GetEnumerator();

        Assert.That(iterator.MoveNext(), Is.True);
        Assert.That(iterator.Current, Is.SameAs(person));
        Assert.That(person.EmailAddress, Is.EqualTo("first@example.com"));
        Assert.That(iterator.MoveNext(), Is.True);
        Assert.That(iterator.Current, Is.SameAs(person));
        Assert.That(person.EmailAddress, Is.EqualTo("second@example.com"));
        Assert.Throws<KeyNotFoundException>(() => iterator.MoveNext());
        Assert.That(person.EmailAddress, Is.EqualTo("second@example.com"));
    }

    [Test]
    public void To_StructTarget_PreservesAssignedProperties()
    {
        var path = WriteCsv("people.csv", "ID\n101\n");
        var map = ObjectMap<PersonValue>.Create(() => default).Map("ID", p => p.Id);

        Assert.That(ReaderBuilder().Build().To(path, map).Single().Id, Is.EqualTo(101));
    }

    [Test]
    public void Map_InvalidPropertySelectors_ThrowBeforeReading()
    {
        var map = ObjectMap<Person>.Create(() => new Person());

        Assert.Throws<ArgumentException>(() => map.Map("ID", p => p.Id + 1));
        Assert.Throws<ArgumentException>(() => map.Map("Name", p => p.Name.Length));
        Assert.Throws<ArgumentException>(() => map.Map("Name", p => p.ReadOnly));
        Assert.Throws<ArgumentException>(() => map.Map("Name", p => p.InitOnly));
    }

    private static CsvReaderBuilder ReaderBuilder()
        => new CsvReaderBuilder().WithDialect(d => d.WithHeader().WithLineTerminator("\n"));

    private string WriteCsv(string filename, string content)
    {
        var path = Path.Combine(_directory.FullName, filename);
        File.WriteAllText(path, content);
        return path;
    }

    private sealed class Person
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public DateOnly BirthDate { get; set; }
        public string EmailAddress { get; set; } = "original@example.com";
        public string ReadOnly => Name;
        public string InitOnly { get; init; } = string.Empty;
    }

    private struct PersonValue
    {
        public int Id { get; set; }
    }

    private sealed record ConstructorPerson(int Id, string Name);
}
