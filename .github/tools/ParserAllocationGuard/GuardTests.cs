namespace ParserAllocationGuard;

internal static class GuardTests
{
    internal static int Run()
    {
        var checks = 0;
        try
        {
            Rules("class P { void Read() { reader.ReadLine(); reader.ReadLineAsync(token); } }", "PA001", "PA001");
            Rules("class P { void Read() { reader?.ReadLine(); } }", "PA001");
            Rules("class P { void Read() { var x = new RecordSpan(line, fields); var y = new RecordMemory(line.AsSpan(), fields); } }", "PA002", "PA002");
            Rules("class P { void Read() { RecordMemory x = new(line, fields); } }", "PA002");
            Rules("class P { void Read() { var x = new global::PocketCsvReader.RecordSpan(line, fields); } }", "PA002");
            Rules("class P { void Read() { var x = new List<FieldSpan>(); List<FieldSpan> y = new(); } }", "PA003", "PA003");
            Rules("class P { void Read() { List<FieldSpan> fields = []; } }", "PA003");
            Rules("class P { readonly List<FieldSpan> fields = new(); void Read() { fields.Clear(); } }");
            Rules("class P { List<FieldSpan> Fields => new List<FieldSpan>(); }", "PA003");
            Rules("class P { List<FieldSpan> Fields { get; } = new List<FieldSpan>(); }");
            Rules("class P { Func<object> factory = () => new List<FieldSpan>(); }", "PA003");
            Rules("class P { StringBuilder record = new(); void Read() { record.ToString(); } }", "PA004");
            Rules("class P { void Read() { var record = new StringBuilder(); record.ToString(); } }", "PA004");
            Rules("class P { void Read() { new StringBuilder().ToString(); } }", "PA004");
            Rules("class P { void Read() { Record = row.AsMemory(); } }", "PA005");
            Rules("class P { void Read() { this.Record = row.AsMemory(); } }", "PA005");
            Rules("class P { void Read() { var memory = input.AsMemory(); Record = RecordMemory.Empty; } }");
            Rules("class P { RecordMemory Export() => row.AsMemory(); }");
            Rules("class P { void Read() { new RecordSpan([], []); new RecordMemory([], []); RecordSpan.FromMemory(memory, fields); reader.ReadLineFrame(); } }");
            Rules("class P { // reader.ReadLine(); new List<FieldSpan>();\n string note = \"new RecordSpan(line, fields)\"; string raw = \"\"\"reader.ReadLine();\"\"\"; }");

            var finding = Guard.Scan("PocketCsvReader.Example/Source.cs", "class P { void Read() { reader.ReadLine(); } }").Single();
            var exception = new ExceptionEntry(finding.Path, finding.Rule, finding.Code, 1, "Existing line materialization awaiting migration.");
            Check(Guard.Check([finding], []).Count == 1, "Unreviewed finding fails");
            Check(Guard.Check([finding], [exception]).Count == 0, "Exact reviewed finding passes");
            Check(Guard.Check([finding, finding], [exception]).Count == 1, "Duplicated occurrence fails");
            Check(Guard.Check([], [exception]).Count == 1, "Removed occurrence requires removing exception");
            Check(Guard.Check([finding], [exception with { Reason = " " }]).Count > 0, "Exception needs a rationale");
            Check(Guard.Check([finding], [exception, exception]).Count > 0, "Duplicate exceptions fail");
            Check(Guard.Check([finding with { Path = "PocketCsvReader.New/Source.cs" }], [exception]).Count > 0, "Exception cannot cover another parser");
            var reformatted = Guard.Scan(finding.Path, "class P { void Read() { reader /* comment */ . ReadLine ( ); } }").Single();
            Check(Guard.Check([reformatted], [exception]).Count == 0, "Formatting does not invalidate review");

            var root = Path.Combine(Path.GetTempPath(), "parser-allocation-guard-" + Guid.NewGuid().ToString("N"));
            try
            {
                Write("PocketCsvReader.New/PocketCsvReader.New.csproj", "<Project />");
                Write("PocketCsvReader.New/Parsing/UnusualName.cs", "class P { void Read() { reader.ReadLine(); } }");
                Write("PocketCsvReader.New/obj/Generated.cs", "class Generated { }");
                Write("PocketCsvReader.New/bin/Generated.cs", "class Generated { }");
                Write("PocketCsvReader.New.Testing/PocketCsvReader.New.Testing.csproj", "<Project />");
                Write("PocketCsvReader.New.Testing/Test.cs", "class Test { }");
                var files = Program.ProductionFiles(root).ToArray();
                Check(files.Length == 1 && files[0].EndsWith("UnusualName.cs"), "Discover new packages and helpers; exclude tests/build output");
                Check(Guard.Scan("PocketCsvReader.New/Parsing/UnusualName.cs", File.ReadAllText(files[0])).Length == 1, "New parser is checked without registration");

                void Write(string path, string text)
                {
                    var fullPath = Path.Combine(root, path);
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                    File.WriteAllText(fullPath, text);
                }
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
            Console.WriteLine($"Parser allocation guard self-tests passed ({checks} checks).");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }

        void Rules(string source, params string[] expected)
            => Check(Guard.Scan("PocketCsvReader.Example/Source.cs", source).Select(finding => finding.Rule).Order().SequenceEqual(expected.Order()), source);

        void Check(bool success, string description)
        {
            if (!success)
                throw new InvalidOperationException("Failed: " + description);
            checks++;
        }
    }
}
