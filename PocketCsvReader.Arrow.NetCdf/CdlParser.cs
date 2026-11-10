using System.Globalization;
using Apache.Arrow.Types;

namespace PocketCsvReader.Arrow.NetCdf;

internal sealed class CdlParser(CdlLexer lexer, CdlStore store, CancellationToken token)
{
    private const string VariablesSection = "variables";
    private const string DataSection = "data";

    private readonly CdlDataset _dataset = new();
    private CdlToken _current;

    public async ValueTask<CdlDataset> ParseAsync()
    {
        await AdvanceAsync().ConfigureAwait(false);
        if (_current.Kind == '\0')
            return _dataset;

        await ReadSectionAsync("netcdf", colon: false).ConfigureAwait(false);
        _dataset.Name = Name();
        await AdvanceAsync().ConfigureAwait(false);
        await ExpectAsync('{').ConfigureAwait(false);

        if (_current.IsWord("dimensions"))
            await ReadDimensionsAsync().ConfigureAwait(false);
        if (_current.IsWord(VariablesSection))
            await ReadVariablesAsync().ConfigureAwait(false);
        if (_current.IsWord(DataSection))
            await ReadDataAsync().ConfigureAwait(false);

        await ExpectAsync('}').ConfigureAwait(false);
        if (_current.Kind != '\0')
            throw _current.Error("Unexpected content after the CDL dataset");
        foreach (var variable in _dataset.Variables.Values)
            CdlDataset.Size(variable.Dimensions, $"variable '{variable.Name}'");
        await store.CompleteAsync(token).ConfigureAwait(false);
        return _dataset;
    }

    private async ValueTask ReadDimensionsAsync()
    {
        await ReadSectionAsync("dimensions").ConfigureAwait(false);
        while (IsDimensionDeclaration())
        {
            await ReadDimensionAsync().ConfigureAwait(false);
            if (_current.Kind is not (';' or ','))
                throw _current.Error("Expected ',' or ';' after dimension extent");
            var separator = _current.Kind;
            await AdvanceAsync().ConfigureAwait(false);
            if (separator == ',' && !IsDimensionDeclaration())
                throw _current.Error("Expected another dimension declaration");
        }
    }

    private bool IsDimensionDeclaration()
        => _current.Kind == 'w' && !_current.IsWord(VariablesSection) && !_current.IsWord(DataSection);

    private async ValueTask ReadDimensionAsync()
    {
        var name = Name();
        await AdvanceAsync().ConfigureAwait(false);
        await ExpectAsync('=').ConfigureAwait(false);

        var unlimited = _current.Kind == 'w' && _current.Text.Span.Equals("UNLIMITED", StringComparison.OrdinalIgnoreCase);
        var extent = 0L;
        if (unlimited && _dataset.Unlimited is not null)
            throw _current.Error("Only one unlimited dimension is supported");
        if (!unlimited && (_current.Kind != 'w' || !long.TryParse(_current.Text.Span, NumberStyles.None, CultureInfo.InvariantCulture, out extent)))
            throw _current.Error($"Expected a nonnegative Int64 extent for dimension '{name}'");

        var dimension = new CdlDimension(name, extent, unlimited);
        if (!_dataset.Dimensions.TryAdd(name, dimension))
            throw _current.Error($"Duplicate dimension '{name}'");
        if (unlimited)
            _dataset.Unlimited = dimension;
        await AdvanceAsync().ConfigureAwait(false);
    }

    private async ValueTask ReadVariablesAsync()
    {
        await ReadSectionAsync(VariablesSection).ConfigureAwait(false);
        while (_current.Kind != '}' && !_current.IsWord(DataSection))
            await ReadVariableEntryAsync().ConfigureAwait(false);
    }

    private async ValueTask ReadVariableEntryAsync()
    {
        if (_current.Kind == ':')
        {
            await ReadAttributeAsync(null, null, false).ConfigureAwait(false);
            return;
        }

        var name = Name();
        var type = CdlNumber.GetType(name.ToLowerInvariant());
        var textType = name.Equals("char", StringComparison.OrdinalIgnoreCase) || name.Equals("string", StringComparison.OrdinalIgnoreCase);
        var hasType = type is not null || textType;
        await AdvanceAsync().ConfigureAwait(false);
        if (_current.Kind == ':')
        {
            await ReadAttributeAsync(hasType ? null : FindVariable(name), type, textType).ConfigureAwait(false);
            return;
        }

        if (!hasType)
            throw _current.Error($"Unsupported CDL variable type or construct '{name}'");

        await ReadTypedVariableEntryAsync(name, type, textType).ConfigureAwait(false);
    }

    private async ValueTask ReadTypedVariableEntryAsync(string typeName, IArrowType? type, bool textType)
    {
        var target = Name();
        await AdvanceAsync().ConfigureAwait(false);
        if (_current.Kind == ':')
            await ReadAttributeAsync(FindVariable(target), type, textType).ConfigureAwait(false);
        else if (type is not null)
            await ReadDeclarationsAsync(type, target).ConfigureAwait(false);
        else
            throw _current.Error($"CDL variables of type '{typeName}' are unsupported");
    }

    private async ValueTask ReadDeclarationsAsync(IArrowType type, string name)
    {
        while (true)
        {
            var dimensions = await ReadVariableDimensionsAsync(name).ConfigureAwait(false);
            if (!_dataset.Variables.TryAdd(name, new CdlVariable(name, type, dimensions)))
                throw _current.Error($"Duplicate variable '{name}'");
            if (_current.Kind != ',')
                break;
            await AdvanceAsync().ConfigureAwait(false);
            name = Name();
            await AdvanceAsync().ConfigureAwait(false);
        }

        await ExpectAsync(';').ConfigureAwait(false);
    }

    private async ValueTask<CdlDimension[]> ReadVariableDimensionsAsync(string name)
    {
        if (_current.Kind != '(')
            return [];

        var dimensions = new List<CdlDimension>();
        do
        {
            await AdvanceAsync().ConfigureAwait(false);
            var dimensionName = Name();
            if (!_dataset.Dimensions.TryGetValue(dimensionName, out var dimension))
                throw _current.Error($"Unknown dimension '{dimensionName}' in variable '{name}'");
            if (dimensions.Contains(dimension))
                throw _current.Error($"Variable '{name}' repeats dimension '{dimensionName}'");
            if (dimension.Unlimited && dimensions.Count > 0)
                throw _current.Error($"Unlimited dimension '{dimensionName}' must be first in variable '{name}'");
            dimensions.Add(dimension);
            await AdvanceAsync().ConfigureAwait(false);
        }
        while (_current.Kind == ',');

        await ExpectAsync(')').ConfigureAwait(false);
        return dimensions.ToArray();
    }

    private async ValueTask ReadAttributeAsync(CdlVariable? variable, IArrowType? type, bool textType)
    {
        await ExpectAsync(':').ConfigureAwait(false);
        var name = Name();
        var attributes = variable?.Attributes ?? _dataset.Attributes;
        if (attributes.ContainsKey(name))
            throw _current.Error($"Duplicate attribute '{name}'");
        await AdvanceAsync().ConfigureAwait(false);
        await ExpectAsync('=').ConfigureAwait(false);

        var sentinel = name is "_FillValue" or "missing_value" ? variable : null;
        bool? quoted = textType || type is not null ? textType : null;
        var value = await ReadAttributeValuesAsync(name, type, quoted, sentinel).ConfigureAwait(false);
        attributes.Add(name, value);
        await ExpectAsync(';').ConfigureAwait(false);
    }

    private async ValueTask<string> ReadAttributeValuesAsync(string name, IArrowType? type, bool? quoted, CdlVariable? sentinel)
    {
        var literals = new List<string>();
        string? singleText = null;
        while (true)
        {
            quoted ??= _current.Kind == '"';
            var literal = ReadAttributeLiteral(name, type, quoted.Value, sentinel);
            literals.Add(literal.Raw);
            singleText = literal.Text;
            await AdvanceAsync().ConfigureAwait(false);
            if (_current.Kind != ',')
                break;

            await AdvanceAsync().ConfigureAwait(false);
            if (sentinel is not null && name == "_FillValue")
                throw _current.Error($"Variable '{sentinel.Name}' requires a single _FillValue");
        }

        return literals.Count == 1 && singleText is not null ? singleText : string.Join(", ", literals);
    }

    private (string Raw, string? Text) ReadAttributeLiteral(string name, IArrowType? type, bool quoted, CdlVariable? sentinel)
    {
        if (sentinel is not null)
            sentinel.MissingValues.Add(CdlNumber.Parse(_current, sentinel.Type));
        if (quoted != (_current.Kind == '"'))
            throw _current.Error($"Attribute '{name}' mixes numeric and text values");
        if (quoted)
            return ($"\"{_current.Text.ToString()}\"", DecodeText(_current));

        CdlNumber.Parse(_current, type ?? sentinel?.Type ?? CdlNumber.InferType(_current.Text.Span));
        return (_current.Text.ToString(), null);
    }

    private async ValueTask ReadDataAsync()
    {
        await ReadSectionAsync(DataSection).ConfigureAwait(false);
        while (_current.Kind != '}')
        {
            var variable = FindVariable(Name());
            if (variable.Assigned)
                throw _current.Error($"Duplicate data assignment for variable '{variable.Name}'");

            var unlimited = variable.Dimensions.Length > 0 && variable.Dimensions[0].Unlimited;
            var size = CdlDataset.Size(variable.Dimensions.AsSpan(unlimited ? 1 : 0), $"variable '{variable.Name}'");
            variable.Assigned = true;
            variable.Offset = store.Position;
            await AdvanceAsync().ConfigureAwait(false);
            await ExpectAsync('=').ConfigureAwait(false);

            var limit = unlimited && size > 0 ? long.MaxValue : size;
            var count = await ReadAssignedValuesAsync(variable, limit).ConfigureAwait(false);
            await ExpectAsync(';').ConfigureAwait(false);
            variable.ValueCount = count;
            _dataset.HasValues = true;
            if (unlimited)
            {
                var records = ((count - 1) / size) + 1;
                var dimension = variable.Dimensions[0];
                dimension.Extent = Math.Max(dimension.Extent, records);
            }
        }
    }

    private async ValueTask<long> ReadAssignedValuesAsync(CdlVariable variable, long limit)
    {
        var count = 0L;
        var current = _current;
        while (true)
        {
            if (count >= limit)
                throw current.Error($"Too many values for variable '{variable.Name}' with dimensions [{string.Join(", ", variable.Dimensions.Select(dimension => dimension.Name))}]");
            await store.AppendAsync(ReadValue(variable, current), token).ConfigureAwait(false);
            count++;

            var separator = await lexer.ReadAsync().ConfigureAwait(false);
            if (separator.Kind != ',')
            {
                _current = separator;
                return count;
            }

            current = await lexer.ReadAsync().ConfigureAwait(false);
        }
    }

    internal static CdlValue ReadValue(CdlVariable variable, CdlToken current)
    {
        if (current.IsWord("_"))
            return default;

        var bits = CdlNumber.Parse(current, variable.Type, variable.Name);
        foreach (var sentinel in variable.MissingValues)
        {
            if (CdlNumber.Equal(variable.Type, bits, sentinel))
                return default;
        }
        return new(bits);
    }

    private static string DecodeText(CdlToken literal)
    {
        var text = literal.Text.Span;
        if (!text.Contains('\\'))
            return text.ToString();

        var decoded = new char[text.Length];
        var count = 0;
        for (var index = 0; index < text.Length; index++)
        {
            var next = text[index];
            if (next == '\\')
            {
                next = text[++index] switch
                {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    'a' => '\a',
                    'b' => '\b',
                    'f' => '\f',
                    'v' => '\v',
                    '0' when index + 1 == text.Length || !char.IsAsciiDigit(text[index + 1]) => '\0',
                    '"' or '\'' or '\\' => text[index],
                    _ => throw literal.Error($"Unsupported escape '\\{text[index]}'"),
                };
            }

            decoded[count++] = next;
        }

        return new string(decoded, 0, count);
    }

    private string Name()
    {
        var text = _current.Text.Span;
        if (_current.Kind != 'w' || text.IsEmpty || !(char.IsLetter(text[0]) || text[0] == '_'))
            throw _current.Error("Expected a CDL name");
        foreach (var character in text[1..])
        {
            if (!(char.IsLetterOrDigit(character) || character is '_' or '.' or '-' or '+' or '@'))
                throw _current.Error("Unsupported character in CDL name");
        }

        if (text is "types" or "group")
            throw _current.Error($"Unsupported NetCDF-4 construct '{text.ToString()}'");
        return text.ToString();
    }

    private CdlVariable FindVariable(string name)
        => _dataset.Variables.TryGetValue(name, out var variable)
            ? variable
            : throw _current.Error($"Unknown variable '{name}'");

    private async ValueTask ReadSectionAsync(string name, bool colon = true)
    {
        if (!_current.IsWord(name))
            throw _current.Error($"Expected '{name}'");
        await AdvanceAsync().ConfigureAwait(false);
        if (colon)
            await ExpectAsync(':').ConfigureAwait(false);
    }

    private async ValueTask ExpectAsync(char kind)
    {
        if (_current.Kind != kind)
        {
            if (_current.IsWord("types") || _current.IsWord("group"))
                throw _current.Error($"Unsupported NetCDF-4 construct '{_current.Text.ToString()}'");
            throw _current.Error($"Expected '{kind}'");
        }

        await AdvanceAsync().ConfigureAwait(false);
    }

    private async ValueTask AdvanceAsync() => _current = await lexer.ReadAsync().ConfigureAwait(false);
}
