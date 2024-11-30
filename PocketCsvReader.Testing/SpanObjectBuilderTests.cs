using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestPlatform.Utilities;
using NUnit.Framework;

namespace PocketCsvReader.Testing;
public class SpanObjectBuilderTests
{

    private record struct StringBox(string value);
    private record struct IntBox(int value);
    private record struct FloatBox(float value);
    private record struct DateTimeBox(DateTime value);
    private record struct DateOnlyBox(DateOnly value);
    private record struct TimeOnlyBox(TimeOnly value);

    public class SpanObjectBuilderProxy<T> : SpanObjectBuilder<T>
    {
        public SpanObjectBuilderProxy() : base() { }
        public T Instantiate(char[] array, FieldSpan[] fieldSpans)
            => base.Instantiate(array, fieldSpans);
    }

    [Test]
    [TestCase("foo", "foo", typeof(StringBox))]
    [TestCase("10", 10, typeof(IntBox))]
    [TestCase("10.15", 10.15f, typeof(FloatBox))]
    public void Instantiate_SingleField_Valid(string input, object output, Type type)
    {       
        var builderType = typeof(SpanObjectBuilderProxy<>).MakeGenericType(type);
        var builder = Activator.CreateInstance(builderType);
        var instantiateMethod = builderType.GetMethod("Instantiate", [typeof(char[]), typeof(FieldSpan[])])!;
        var fieldSpans = new[] { new FieldSpan(0, input.Length, false, false) };
        var result = instantiateMethod.Invoke(builder, [input.ToCharArray(), fieldSpans ])!;
        Assert.That(type.GetProperty("value")!.GetValue(result), Is.EqualTo(output));
    }


    [TestCase("2024-12-12T16:12:13", typeof(DateTime), typeof(DateTimeBox))]
    [TestCase("2024-12-12", typeof(DateOnly), typeof(DateOnlyBox))]
    [TestCase("16:12:13", typeof(TimeOnly), typeof(TimeOnlyBox))]
    public void Instantiate_SingleFieldFromText_Valid(string input, Type fromText, Type type)
    {
        var output = fromText.GetMethod("Parse", [typeof(string)])!.Invoke(null, [input])!;
        Instantiate_SingleField_Valid(input, output, type);
    }

    [Test]
    public void Instantiate_SingleFieldButWrongType_FormatException()
    {
        var builder = new SpanObjectBuilder<IntBox>();
        var fieldSpans = new[] { new FieldSpan(0, "2024-12-12".Length, false, false) };
        var ex = Assert.Catch<FormatException>(() => builder.Instantiate(new ReadOnlySpan<char>("2024-12-12".ToCharArray()), fieldSpans));
        Assert.That(ex!.Message, Does.Contain("Int32"));
        Assert.That(ex.Message, Does.Contain("0"));
        Assert.That(ex.Message, Does.Contain("2024-12-12"));
    }
}
