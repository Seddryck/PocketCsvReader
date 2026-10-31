using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ParserAllocationGuard;

internal static class Program
{
    internal static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static int Main(string[] args)
    {
        if (args is ["--self-test"])
            return GuardTests.Run();
        if (args.Length is < 1 or > 2 || (args.Length == 2 && args[1] != "--inventory"))
        {
            Console.Error.WriteLine("Usage: ParserAllocationGuard <repository-root> [--inventory] | --self-test");
            return 2;
        }

        try
        {
            var root = Path.GetFullPath(args[0]);
            if (!File.Exists(Path.Combine(root, "PocketCsvReader", "PocketCsvReader.csproj")))
                throw new InvalidDataException("The root must contain PocketCsvReader/PocketCsvReader.csproj.");
            var findings = ProductionFiles(root)
                .SelectMany(path => Guard.Scan(Path.GetRelativePath(root, path).Replace('\\', '/'), File.ReadAllText(path)))
                .ToArray();
            if (args.Length == 2)
            {
                Console.WriteLine(JsonSerializer.Serialize(findings, JsonOptions));
                return 0;
            }

            var baselinePath = Path.Combine(root, ".github", "tools", "ParserAllocationGuard", "exceptions.json");
            var exceptions = JsonSerializer.Deserialize<ExceptionEntry[]>(File.ReadAllText(baselinePath), JsonOptions)
                ?? throw new InvalidDataException("The exception list is missing.");
            var errors = Guard.Check(findings, exceptions);
            foreach (var error in errors)
                Console.Error.WriteLine(error);
            if (errors.Count != 0)
            {
                Console.Error.WriteLine("Review .github/skills/file-parser-design/SKILL.md. Prefer shared buffers and explicit memory ownership; do not regenerate exceptions to silence this check.");
                return 1;
            }
            Console.WriteLine($"Parser allocation guard passed ({findings.Length} reviewed occurrences).");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 2;
        }
    }

    internal static IEnumerable<string> ProductionFiles(string root)
    {
        foreach (var directory in Directory.EnumerateDirectories(root, "PocketCsvReader*"))
        {
            var name = Path.GetFileName(directory);
            if (name.EndsWith(".Testing") || name.EndsWith(".Benchmark") || name.EndsWith(".Profiler"))
                continue;
            if (!File.Exists(Path.Combine(directory, name + ".csproj")))
                continue;
            foreach (var file in Walk(directory))
                yield return file;
        }

        static IEnumerable<string> Walk(string directory)
        {
            foreach (var file in Directory.EnumerateFiles(directory, "*.cs"))
                yield return file;
            foreach (var child in Directory.EnumerateDirectories(directory))
            {
                if (Path.GetFileName(child) is "bin" or "obj" or ".git" or ".worktrees")
                    continue;
                foreach (var file in Walk(child))
                    yield return file;
            }
        }
    }
}

internal sealed record Finding(string Path, string Rule, string Code, int Line);
internal sealed record ExceptionEntry(string Path, string Rule, string Code, int Count, string Reason);

internal static class Guard
{
    internal static Finding[] Scan(string path, string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var root = tree.GetRoot();
        var builders = root.DescendantNodes().OfType<VariableDeclarationSyntax>()
            .Where(declaration => SimpleName(declaration.Type) == "StringBuilder" ||
                declaration.Variables.Any(variable => variable.Initializer?.Value is ObjectCreationExpressionSyntax creation && SimpleName(creation.Type) == "StringBuilder"))
            .SelectMany(declaration => declaration.Variables.Select(variable => variable.Identifier.ValueText))
            .ToHashSet(StringComparer.Ordinal);
        var findings = new List<Finding>();
        foreach (var node in root.DescendantNodes())
        {
            if (node is InvocationExpressionSyntax { Expression: MemberBindingExpressionSyntax binding } conditional &&
                binding.Name.Identifier.ValueText is "ReadLine" or "ReadLineAsync")
                Add("PA001", conditional);
            if (node is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member } invocation)
            {
                if (member.Name.Identifier.ValueText is "ReadLine" or "ReadLineAsync")
                    Add("PA001", invocation);
                if (member.Name.Identifier.ValueText == "ToString" &&
                    (member.Expression is IdentifierNameSyntax identifier && builders.Contains(identifier.Identifier.ValueText) ||
                     member.Expression is MemberAccessExpressionSyntax access && builders.Contains(access.Name.Identifier.ValueText) ||
                     member.Expression is ObjectCreationExpressionSyntax builder && SimpleName(builder.Type) == "StringBuilder"))
                    Add("PA004", invocation);
            }

            TypeSyntax? type = node switch
            {
                ObjectCreationExpressionSyntax creation => creation.Type,
                ImplicitObjectCreationExpressionSyntax { Parent: EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax declaration } } } => declaration.Type,
                CollectionExpressionSyntax { Parent: EqualsValueClauseSyntax { Parent: VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax declaration } } } => declaration.Type,
                _ => null
            };
            if (type is null)
                continue;
            var arguments = node switch
            {
                ObjectCreationExpressionSyntax creation => creation.ArgumentList,
                ImplicitObjectCreationExpressionSyntax creation => creation.ArgumentList,
                _ => null
            };
            if (SimpleName(type) is "RecordSpan" or "RecordMemory" && arguments?.Arguments.FirstOrDefault()?.Expression is { } input &&
                input is not CollectionExpressionSyntax { Elements.Count: 0 })
                Add("PA002", node);
            if (type.DescendantNodesAndSelf().OfType<GenericNameSyntax>().Any(generic =>
                generic.Identifier.ValueText == "List" && generic.TypeArgumentList.Arguments.Count == 1 &&
                SimpleName(generic.TypeArgumentList.Arguments[0]) == "FieldSpan") && !IsReusableInitializer(node))
                Add("PA003", node);
        }
        return [.. findings];

        void Add(string rule, SyntaxNode node)
            => findings.Add(new(path, rule, string.Concat(node.DescendantTokens().Select(token => token.Text)),
                tree.GetLineSpan(node.Span).StartLinePosition.Line + 1));
    }

    private static bool IsReusableInitializer(SyntaxNode node)
    {
        foreach (var ancestor in node.Ancestors())
        {
            if (ancestor is AnonymousFunctionExpressionSyntax or BaseMethodDeclarationSyntax or AccessorDeclarationSyntax)
                return false;
            if (ancestor is FieldDeclarationSyntax)
                return true;
            if (ancestor is PropertyDeclarationSyntax property)
                return property.Initializer is not null && property.Initializer.Span.Contains(node.Span);
        }
        return false;
    }

    private static string SimpleName(TypeSyntax type) => type switch
    {
        QualifiedNameSyntax qualified => SimpleName(qualified.Right),
        AliasQualifiedNameSyntax alias => SimpleName(alias.Name),
        SimpleNameSyntax name => name.Identifier.ValueText,
        _ => type.ToString()
    };

    internal static List<string> Check(IReadOnlyList<Finding> findings, IReadOnlyList<ExceptionEntry> exceptions)
    {
        var errors = new List<string>();
        var actual = findings.GroupBy(finding => (finding.Path, finding.Rule, finding.Code)).ToDictionary(group => group.Key, group => group.ToArray());
        var allowed = new Dictionary<(string, string, string), ExceptionEntry>();
        foreach (var entry in exceptions)
        {
            var key = (entry.Path, entry.Rule, entry.Code);
            if (entry.Count <= 0 || string.IsNullOrWhiteSpace(entry.Reason) || !allowed.TryAdd(key, entry))
                errors.Add($"Invalid or duplicate exception: {entry.Path} {entry.Rule} {entry.Code}");
        }
        foreach (var (key, occurrences) in actual)
        {
            var count = allowed.TryGetValue(key, out var entry) ? entry.Count : 0;
            if (occurrences.Length > count)
                errors.Add($"{key.Path}:{occurrences[0].Line}: {key.Rule}: new allocation-risk occurrence ({occurrences.Length} found, {count} reviewed): {key.Code}");
        }
        foreach (var entry in exceptions)
        {
            var count = actual.TryGetValue((entry.Path, entry.Rule, entry.Code), out var occurrences) ? occurrences.Length : 0;
            if (count < entry.Count)
                errors.Add($"Remove or reduce stale exception: {entry.Path} {entry.Rule} {entry.Code} ({count} remain, {entry.Count} reviewed).");
        }
        return errors;
    }
}
