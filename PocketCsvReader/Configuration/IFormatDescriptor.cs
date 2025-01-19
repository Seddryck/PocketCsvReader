using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PocketCsvReader.Configuration;
public interface IFormatDescriptor
{
    private readonly static IFormatDescriptor _none = new NoneFormatDescriptor();
    static IFormatDescriptor None => _none;
}

public interface ICultureFormatDescriptor : IFormatDescriptor
{
    IFormatProvider Culture { get; }
}

public record NoneFormatDescriptor()
    : IFormatDescriptor, IEquatable<NoneFormatDescriptor>
{ }

public record NumericFormatDescriptor (NumberStyles Style, IFormatProvider Culture)
    : ICultureFormatDescriptor
{ }

public record TemporalFormatDescriptor(string Pattern, IFormatProvider Culture)
    : ICultureFormatDescriptor
{ }
