using NUnit.Framework;

namespace PocketCsvReader.Testing;

public class FieldMetadataBufferTests
{
    [Test]
    public void BorrowedPublication_ReusesExactShapeWhileOwnedCopiesStayIndependent()
    {
        var buffer = new FieldMetadataBuffer();
        FieldSpan[] scratch = [new(0, 3), new(4, 2)];
        var first = buffer.PublishBorrowed(scratch);
        var retained = FieldMetadataBuffer.CopyOwned(first);
        scratch[0] = new(0, 10);
        var next = buffer.PublishBorrowed(scratch);
        Assert.That(next, Is.SameAs(first));
        Assert.That(next[0].Value.Length, Is.EqualTo(10));
        Assert.That(retained[0].Value.Length, Is.EqualTo(3));
        Assert.That(buffer.PublishBorrowed(scratch.AsSpan(0, 1)), Has.Length.EqualTo(1));
        Assert.That(buffer.PublishBorrowed(ReadOnlySpan<FieldSpan>.Empty), Is.Empty);
    }
}
