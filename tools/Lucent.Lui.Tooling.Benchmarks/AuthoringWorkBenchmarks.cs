using System.Collections.Immutable;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Lucent.Lui.Compiler;
using Lucent.Lui.Preparation;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

internal static class AuthoringWorkBenchmarks
{
    internal static int Run(string[] args, string root)
    {
        if (args.Length != 1)
            throw new ArgumentException("These measurements take no additional arguments.");
        var core = Path.Combine(root, "src/Lucent.Core/bin/Release/net10.0/Lucent.Core.dll");
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Append(core)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create(
            "AuthoringWork",
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        );
        Console.WriteLine(
            JsonSerializer.Serialize(
                new
                {
                    mode = args[0],
                    compilerSha256 = FileHash(typeof(LuiCompiler).Assembly.Location),
                    preparationSha256 = FileHash(typeof(LuiPreparationEngine).Assembly.Location),
                    coreSha256 = FileHash(core),
                    benchmarkSha256 = FileHash(typeof(AuthoringWorkBenchmarks).Assembly.Location),
                    timingScope = "Diagnostic on the active machine; no release threshold.",
                }
            )
        );
        if (args[0] == "sourceMaps")
            Maps(compilation);
        else
            Preparation(compilation);
        return 0;
    }

    private static void Maps(CSharpCompilation compilation)
    {
        foreach (var rows in new[] { 40, 400 })
        {
            var source = Component("Rows", rows);
            var document = LuiParser.Parse(source);
            var identity = new LuiFreshnessIdentity(
                "measure",
                "measure",
                new LuiDocumentIdentity("Rows.lui"),
                "1",
                "preview"
            );
            var result = LuiCompiler.Compile(document, compilation, identity);
            Require(
                result.Success,
                String.Join(" | ", result.Diagnostics.Select(item => item.Message))
            );
            var map = result.Map;
            var sourceQueries = map.Entries.Select(entry => entry.Source).Distinct().ToArray();
            var generatedQueries = map
                .Entries.Select(entry => entry.Generated)
                .Distinct()
                .ToArray();
            var sourceMatches = sourceQueries.Sum(span => map.FromSource(span).Count);
            var generatedMatches = generatedQueries.Sum(span => map.FromGenerated(span).Count);
            Console.WriteLine(
                JsonSerializer.Serialize(
                    new
                    {
                        rows,
                        sourceSha256 = Hash(source),
                        entries = map.Entries.Count,
                        sourceQueryCount = sourceQueries.Length,
                        generatedQueryCount = generatedQueries.Length,
                        sourceMatches,
                        generatedMatches,
                        maximumSourceMatches = sourceQueries.Max(span =>
                            map.FromSource(span).Count
                        ),
                        maximumGeneratedMatches = generatedQueries.Max(span =>
                            map.FromGenerated(span).Count
                        ),
                        compile = Measure(
                            () =>
                                Require(
                                    LuiCompiler.Compile(document, compilation, identity).Success,
                                    "Compilation failed."
                                ),
                            1,
                            5
                        ),
                        fiveSourceQueries = Measure(
                            () =>
                            {
                                for (var index = 0; index < 5; index++)
                                    _ = map.FromSource(
                                        sourceQueries[index * (sourceQueries.Length - 1) / 4]
                                    );
                            },
                            3,
                            15
                        ),
                        construction = Measure(
                            () => _ = new LuiSourceMap(identity, map.Entries),
                            3,
                            15
                        ),
                        constructionAndFirstSourceQuery = Measure(
                            () =>
                                _ = new LuiSourceMap(identity, map.Entries).FromSource(
                                    sourceQueries[sourceQueries.Length / 2]
                                ),
                            3,
                            15
                        ),
                        constructionAndFirstGeneratedQuery = Measure(
                            () =>
                                _ = new LuiSourceMap(identity, map.Entries).FromGenerated(
                                    generatedQueries[generatedQueries.Length / 2]
                                ),
                            3,
                            15
                        ),
                        sourceQueries = Measure(
                            () =>
                                Require(
                                    sourceQueries.Sum(span => map.FromSource(span).Count)
                                        == sourceMatches,
                                    "Source query results changed."
                                ),
                            3,
                            15
                        ),
                        generatedQueries = Measure(
                            () =>
                                Require(
                                    generatedQueries.Sum(span => map.FromGenerated(span).Count)
                                        == generatedMatches,
                                    "Generated query results changed."
                                ),
                            3,
                            15
                        ),
                    }
                )
            );
        }
    }

    private static void Preparation(CSharpCompilation compilation)
    {
        const int count = 10;
        var documents = Enumerable
            .Range(0, count)
            .Select(index => new LuiPreparationDocument(
                Path.GetFullPath($"artifacts/review324-preparation/Document{index}.lui"),
                $"Document{index}.lui",
                Component($"Document{index}", 20),
                "1"
            ))
            .ToImmutableArray();
        var request = new LuiPreparationRequest(
            compilation,
            documents,
            [],
            [],
            new CSharpParseOptions(LanguageVersion.Preview),
            new EmptyOptionsProvider(),
            "measure",
            "measure",
            "preview",
            "",
            "",
            "Sample"
        );
        var before = LuiPreparationEngine.Prepare(request);
        Require(
            before.Success,
            String.Join(" | ", before.LuiDiagnostics.Select(item => item.Diagnostic.Message))
        );
        var edited = documents.SetItem(
            0,
            documents[0] with
            {
                Source = documents[0]
                    .Source.Replace("Row 0", "Edited row 0", StringComparison.Ordinal),
                Version = "2",
            }
        );
        var changedRequest = request with
        {
            Documents = edited,
            PreviousDriverState = before.DriverState,
        };
        var after = LuiPreparationEngine.Prepare(changedRequest);
        Require(after.Success, "The body-edit preparation failed.");
        var changedFinalSources = before
            .Payload.FinalSources.Zip(after.Payload.FinalSources)
            .Count(pair => pair.First.Source != pair.Second.Source);
        Require(
            changedFinalSources == 1,
            "A single body edit must change exactly one final source."
        );
        Require(
            before.Payload.EarlyDeclarations.SequenceEqual(after.Payload.EarlyDeclarations),
            "A body edit changed early declarations."
        );
        var lowered = after.Documents.Count(item => item.Compilation is not null);
        Require(lowered == count, "The fixed preparation fixture lost documents.");
        Console.WriteLine(
            JsonSerializer.Serialize(
                new
                {
                    documents = count,
                    rowsPerDocument = 20,
                    sourceSha256 = Hash(String.Join("\n", documents.Select(item => item.Source))),
                    editedSourceSha256 = Hash(
                        String.Join("\n", edited.Select(item => item.Source))
                    ),
                    changedFinalSources,
                    loweredResultsAfterOneBodyEdit = lowered,
                    retainedCompilationResults = before
                        .Documents.Zip(after.Documents)
                        .Count(pair =>
                            ReferenceEquals(pair.First.Compilation, pair.Second.Compilation)
                        ),
                    retainedProjections = before
                        .Documents.Zip(after.Documents)
                        .Count(pair =>
                            ReferenceEquals(pair.First.Projection, pair.Second.Projection)
                        ),
                    eagerDocumentParsing = Measure(
                        () =>
                        {
                            foreach (var item in edited)
                                _ = new LuiProjectDocument(
                                    item.PhysicalPath,
                                    item.LogicalPath,
                                    item.Source,
                                    item.Version
                                );
                        },
                        3,
                        15
                    ),
                    projection = Measure(
                        () =>
                        {
                            foreach (var item in edited)
                                _ = LuiAuthoredSourceProjection.Project(item.Source);
                        },
                        3,
                        15
                    ),
                    unchangedPreparation = Measure(
                        () =>
                            Require(
                                LuiPreparationEngine
                                    .Prepare(
                                        request with
                                        {
                                            PreviousDriverState = before.DriverState,
                                        }
                                    )
                                    .Success,
                                "Unchanged preparation failed."
                            ),
                        1,
                        5
                    ),
                    bodyEditPreparation = Measure(
                        () =>
                            Require(
                                LuiPreparationEngine.Prepare(changedRequest).Success,
                                "Edited preparation failed."
                            ),
                        1,
                        5
                    ),
                }
            )
        );
    }

    private static string Component(string name, int rows) =>
        "namespace Sample; using Lucent.Core; internal component "
        + name
        + "() {\n<Column>\n"
        + String.Concat(
            Enumerable.Range(0, rows).Select(row => $"<Text content=\"Row {row}\" />\n")
        )
        + "</Column>\n}";

    private static object Measure(Action run, int warmups, int samples)
    {
        for (var index = 0; index < warmups; index++)
            run();
        var watch = new Stopwatch();
        var times = new double[samples];
        var allocation = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < samples; index++)
        {
            watch.Restart();
            run();
            times[index] = watch.Elapsed.TotalMilliseconds;
        }
        var bytes = (GC.GetAllocatedBytesForCurrentThread() - allocation) / samples;
        Array.Sort(times);
        return new
        {
            warmups,
            samples,
            medianMs = times[samples / 2],
            allocatedBytes = bytes,
        };
    }

    private static string FileHash(string path) =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static string Hash(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private sealed class EmptyOptionsProvider : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new EmptyOptions();

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;

        private sealed class EmptyOptions : AnalyzerConfigOptions
        {
            public override bool TryGetValue(string key, out string value)
            {
                value = "";
                return false;
            }
        }
    }
}
