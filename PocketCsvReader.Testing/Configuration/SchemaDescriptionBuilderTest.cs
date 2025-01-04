using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing.Configuration;
public class SchemaDescriptionBuilderTest
{
    [Test]
    [TestCase(typeof(int))]
    [TestCase(typeof(bool))]
    public void WithField_ShouldSetField(Type type)
    {
        var descriptor = new SchemaDescriptorBuilder()
            .Indexed()
            .WithField(type)
            .Build();
        Assert.That(descriptor, Is.Not.Null);
        Assert.That(descriptor!.IsMatchingByName, Is.False);
        Assert.That(descriptor!.IsMatchingByIndex, Is.True);
        Assert.That(descriptor.Fields, Has.Length.EqualTo(1));
        Assert.That(descriptor.Fields[0].RuntimeType, Is.EqualTo(type));
    }

    [Test]
    [TestCase(typeof(int), typeof(bool))]
    [TestCase(typeof(bool), typeof(bool), typeof(string))]
    public void WithField_ShouldSetFields(params Type[] types)
    {
        var builder = new SchemaDescriptorBuilder().Indexed();
        foreach (var type in types)
            builder.WithField(type);
        var descriptor = builder.Build();
        Assert.That(descriptor, Is.Not.Null);
        Assert.That(descriptor!.Fields, Has.Length.EqualTo(types.Length));
        foreach (var (field, type) in descriptor.Fields.Zip(types))
            Assert.That(field.RuntimeType, Is.EqualTo(type));
    }

    [Test]
    public void WithFieldGeneric_ShouldSetField()
    {
        var descriptor = new SchemaDescriptorBuilder()
            .Indexed()
            .WithField<int>()
            .Build();
        Assert.That(descriptor, Is.Not.Null);
        Assert.That(descriptor!.Fields, Has.Length.EqualTo(1));
        Assert.That(descriptor.Fields[0].RuntimeType, Is.EqualTo(typeof(int)));
    }

    [Test]
    public void WithFieldsNamed_ShouldSetFields()
    {
        var descriptor = new SchemaDescriptorBuilder()
            .Indexed()
            .WithField<int>((f) => f.WithName("foo"))
            .WithField<bool>((f) => f.WithName("bar").WithFormat("%Y-%M-%d"))
            .Build();
        Assert.That(descriptor, Is.Not.Null);
        Assert.That(descriptor!.Fields, Has.Length.EqualTo(2));
        Assert.That(descriptor.Fields[0].RuntimeType, Is.EqualTo(typeof(int)));
        Assert.That(descriptor.Fields[0].Name, Is.EqualTo("foo"));
        Assert.That(descriptor.Fields[0].Format, Is.Null);
        Assert.That(descriptor.Fields.TryGetValue("foo", out var _), Is.True);
        Assert.That(descriptor.Fields.TryGetValue("qrk", out var _), Is.False);
        Assert.That(descriptor.Fields[1].Name, Is.EqualTo("bar"));
        Assert.That(descriptor.Fields[1].RuntimeType, Is.EqualTo(typeof(bool)));
        Assert.That(descriptor.Fields[1].Format, Is.EqualTo("%Y-%M-%d"));
    }

    [Test]
    public void NamedWithFields_ShouldSetFields()
    {
        var descriptor = new SchemaDescriptorBuilder()
            .Named()
            .WithField<int>("foo")
            .WithField<DateTime>("bar", (f) => f.WithFormat("%Y-%M-%d"))
            .Build();
        Assert.That(descriptor, Is.Not.Null);
        Assert.That(descriptor!.IsMatchingByName, Is.True);
        Assert.That(descriptor!.IsMatchingByIndex, Is.False);
        Assert.That(descriptor!.Fields, Has.Length.EqualTo(2));
        Assert.That(descriptor.Fields["foo"].RuntimeType, Is.EqualTo(typeof(int)));
        Assert.That(descriptor.Fields["foo"].Name, Is.EqualTo("foo"));
        Assert.That(descriptor.Fields["foo"].Format, Is.Null);
        Assert.That(descriptor.Fields["bar"].RuntimeType, Is.EqualTo(typeof(DateTime)));
        Assert.That(descriptor.Fields["bar"].Name, Is.EqualTo("bar"));
        Assert.That(descriptor.Fields["bar"].Format, Is.EqualTo("%Y-%M-%d"));

        foreach (var field in descriptor.Fields)
            Assert.That(field.Name, Is.Not.Null.Or.Empty);
    }

    [Test]
    public void DuplicateNames_ShouldThrow()
    {
        var descriptor = new SchemaDescriptorBuilder()
            .Named()
            .WithField<int>("foo")
            .WithField<bool>("foo");

        var ex = Assert.Throws<DuplicateNameException>(() => descriptor.Build());
        Assert.That(ex!.Message, Does.Contain("'foo'"));
    }

    [Test]
    public void EmptyNames_ShouldThrow()
    {
        var descriptor = new SchemaDescriptorBuilder()
            .Named()
            .WithField<int>("")
            .WithField<bool>("foo");

        var ex = Assert.Throws<ArgumentException>(() => descriptor.Build());
        Assert.That(ex!.Message.ToLower(), Does.Contain("empty or null"));
    }
}
