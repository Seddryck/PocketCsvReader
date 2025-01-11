using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using PocketCsvReader.Configuration;

namespace PocketCsvReader.Testing.Configuration;
public class SequenceCollectionTest
{
    [Test]
    public void Add_DistinctValues_AllAreIncluded()
    {
        var sequences = new SequenceCollection();
        sequences.Add("foo", "-");
        sequences.Add("bar", "/");
        Assert.That(sequences.Count(), Is.EqualTo(2));
        Assert.That(sequences.TryGetValue("foo", out var sequence), Is.True);
        Assert.That(sequence.ToString(), Is.EqualTo("-"));
        Assert.That(sequences.TryGetValue("bar", out sequence), Is.True);
        Assert.That(sequence.ToString(), Is.EqualTo("/"));
    }

    [Test]
    public void Add_SameKey_LastOverride()
    {
        var sequences = new SequenceCollection();
        sequences.Add("foo", "-");
        Assert.That(sequences.Count(), Is.EqualTo(1));
        Assert.That(sequences.TryGetValue("foo", out var sequence), Is.True);
        Assert.That(sequence.ToString(), Is.EqualTo("-"));

        sequences.Add("foo", "/");
        Assert.That(sequences.Count(), Is.EqualTo(1));
        Assert.That(sequences.TryGetValue("foo", out sequence), Is.True);
        Assert.That(sequence.ToString(), Is.EqualTo("/"));
    }

}
