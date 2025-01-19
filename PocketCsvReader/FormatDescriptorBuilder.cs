using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Design;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace PocketCsvReader.Configuration;

public abstract class FormatDescriptorBuilder
{
    protected abstract IFormatProvider BuildCulture();

    public abstract IFormatDescriptor Build();

    public static FormatDescriptorBuilder None
        => new NoneFormatDescriptorBuilder();

    public class NoneFormatDescriptorBuilder : FormatDescriptorBuilder
    {
        public override IFormatDescriptor Build()
            => new NoneFormatDescriptor();
        protected override IFormatProvider BuildCulture()
            => CultureInfo.InvariantCulture;
    }
}

public class IntegerFormatDescriptorBuilder : FormatDescriptorBuilder
{
    private string? _groupChar = CultureInfo.InvariantCulture.NumberFormat.NumberGroupSeparator;

    public IntegerFormatDescriptorBuilder WithGroupChar(char groupChar)
        => WithGroupChar(groupChar.ToString());

    public IntegerFormatDescriptorBuilder WithGroupChar(string groupChar)
    {
        _groupChar = groupChar;
        return this;
    }

    public IntegerFormatDescriptorBuilder WithoutGroupChar()
        => WithGroupChar(string.Empty);

    protected override IFormatProvider BuildCulture()
    {
        var format = CultureInfo.InvariantCulture.NumberFormat.Clone() as NumberFormatInfo ?? throw new NotSupportedException();
        format.NumberGroupSeparator = string.IsNullOrEmpty(_groupChar) ? string.Empty : _groupChar;
        return format;
    }

    protected virtual NumberStyles BuildNumberStyle()
    {
        var style = NumberStyles.AllowLeadingSign | NumberStyles.AllowExponent;
        if (_groupChar is not null)
            style |= NumberStyles.AllowThousands;
        return style;
    }

    public override IFormatDescriptor Build()
        => new NumericFormatDescriptor(BuildNumberStyle(), BuildCulture());
}

public class NumberFormatDescriptorBuilder : IntegerFormatDescriptorBuilder
{
    private string _decimaChar = CultureInfo.InvariantCulture.NumberFormat.NumberDecimalSeparator;

    public new NumberFormatDescriptorBuilder WithGroupChar(char groupChar)
        => WithGroupChar(groupChar.ToString());

    public new NumberFormatDescriptorBuilder WithGroupChar(string groupChar)
        => (NumberFormatDescriptorBuilder)base.WithGroupChar(groupChar);

    public new NumberFormatDescriptorBuilder WithoutGroupChar()
        => (NumberFormatDescriptorBuilder)base.WithoutGroupChar();

    public NumberFormatDescriptorBuilder WithDecimalChar(char groupChar)
        => WithDecimalChar(groupChar.ToString());

    public NumberFormatDescriptorBuilder WithDecimalChar(string decimaChar)
    {
        _decimaChar = decimaChar;
        return this;
    }

    protected override IFormatProvider BuildCulture()
    {
        var format = base.BuildCulture() as NumberFormatInfo ?? throw new InvalidCastException();
        format.NumberDecimalSeparator = _decimaChar;
        return format;
    }

    protected override NumberStyles BuildNumberStyle()
        => base.BuildNumberStyle() | NumberStyles.AllowDecimalPoint;
}

public class TemporalFormatDescriptorBuilder : FormatDescriptorBuilder
{
    private string _pattern;
    private string _dateSeparator = CultureInfo.InvariantCulture.DateTimeFormat.DateSeparator;
    private string _timeSeparator = CultureInfo.InvariantCulture.DateTimeFormat.TimeSeparator;

    public TemporalFormatDescriptorBuilder(string pattern)
    {
        _pattern = pattern;
    }

    public TemporalFormatDescriptorBuilder WithDateSeparator(string separator)
    {
        _dateSeparator = separator;
        return this;
    }

    public TemporalFormatDescriptorBuilder WithTimeSeparator(string separator)
    {
        _timeSeparator = separator;
        return this;
    }

    protected override IFormatProvider BuildCulture()
    {
        var format = CultureInfo.InvariantCulture.DateTimeFormat.Clone() as DateTimeFormatInfo ?? throw new NotSupportedException();
        format.DateSeparator = _dateSeparator;
        format.TimeSeparator = _timeSeparator;
        return format;
    }

    public override IFormatDescriptor Build()
        => new TemporalFormatDescriptor(_pattern, BuildCulture());
}

public class CustomFormatDescriptorBuilder : FormatDescriptorBuilder
{
    private IFormatProvider? _formatProvider;

    public CustomFormatDescriptorBuilder(IFormatProvider? formatProvider)
    {
        _formatProvider = formatProvider;
    }

    public override IFormatDescriptor Build()
        => new CustomFormatDescriptor(BuildCulture());
    protected override IFormatProvider BuildCulture()
        => _formatProvider ?? CultureInfo.InvariantCulture;
}
