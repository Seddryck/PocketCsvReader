using System;
using System.Collections.Generic;
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
            .WithField(type)
            .Build();
        Assert.That(descriptor.Fields, Has.Length.EqualTo(1));
        Assert.That(descriptor.Fields![0].Type, Is.EqualTo(type));
    }

    [Test]
    [TestCase(typeof(int), typeof(bool))]
    [TestCase(typeof(bool), typeof(bool), typeof(string))]
    public void WithField_ShouldSetFields(params Type[] types)
    {
        var builder = new SchemaDescriptorBuilder();
        foreach (var type in types)
            builder.WithField(type);
        var descriptor = builder.Build();
        Assert.That(descriptor.Fields, Has.Length.EqualTo(types.Length));
        for (var i = 0; i < types.Length; i++)
            Assert.That(descriptor.Fields![i].Type, Is.EqualTo(types[i]));
    }

    [Test]
    public void WithFieldGeneric_ShouldSetField()
    {
        var descriptor = new SchemaDescriptorBuilder()
            .WithField<int>()
            .Build();
        Assert.That(descriptor.Fields, Has.Length.EqualTo(1));
        Assert.That(descriptor.Fields![0].Type, Is.EqualTo(typeof(int)));
    }


    [Test]
    public void WithFieldsNamed_ShouldSetFields()
    {
        var descriptor = new SchemaDescriptorBuilder()
            .WithField<int>("foo")
            .WithField(typeof(bool), "bar")
            .Build();
        Assert.That(descriptor.Fields, Has.Length.EqualTo(2));
        Assert.That(descriptor.Fields![0].Type, Is.EqualTo(typeof(int)));
        Assert.That(descriptor.Fields![0].Name, Is.EqualTo("foo"));
        Assert.That(descriptor.Fields![1].Type, Is.EqualTo(typeof(bool)));
        Assert.That(descriptor.Fields![1].Name, Is.EqualTo("bar"));
    }
}
