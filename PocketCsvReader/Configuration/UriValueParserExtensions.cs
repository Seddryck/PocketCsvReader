using System;

namespace PocketCsvReader.Configuration;

/// <summary>Registers the built-in .NET parser for URI values.</summary>
public static class UriValueParserExtensions
{
    /// <summary>Enables typed URI parsing for the specified kind, which defaults to absolute URIs.</summary>
    /// <remarks>Explicit field and runtime parsers take precedence. System.Uri requires a string input.</remarks>
    public static RuntimeParsersDescriptorBuilder UseUriValues(
        this RuntimeParsersDescriptorBuilder options, UriKind kind = UriKind.Absolute)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.WithDefaultParser<Uri>(text => new Uri(text.ToString(), kind));
    }
}
