using Basic.Reference.Assemblies;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TFWR.Compiler.Emit;
using TFWR.Compiler.Ir;

namespace TFWR.Compiler;

/// <summary>
/// Compiles C# or TypeScript sources that use TFWR APIs into TFWR Python modules.
/// </summary>
public sealed class TfwrCompiler
{
    /// <summary>
    /// Compiles the given input files and writes emitted <c>.py</c> modules to disk.
    /// </summary>
    /// <param name="options">Compile options including inputs and output directory.</param>
    /// <returns>Compile result with diagnostics and emitted file contents.</returns>
    public static CompileResult Compile(CompileOptions options)
    {
        if (string.Equals(options.Language, "ts", StringComparison.OrdinalIgnoreCase))
        {
            return TsCompiler.Compile(options);
        }

        if (!string.Equals(options.Language, "cs", StringComparison.OrdinalIgnoreCase))
        {
            return new CompileResult
            {
                Diagnostics =
                {
                    new CompileDiagnostic
                    {
                        Severity = DiagnosticSeverity.Error,
                        Message = $"Language '{options.Language}' is not supported. Use --lang cs or --lang ts."
                    }
                }
            };
        }

        return CompileCs(options);
    }

    private static CompileResult CompileCs(CompileOptions options)
    {
        var result = new CompileResult();

        if (options.InputFiles.Count == 0)
        {
            result.Diagnostics.Add(new CompileDiagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Message = "At least one input .cs file is required."
            });
            return result;
        }

        var absoluteInputs = new List<string>();
        foreach (var input in options.InputFiles)
        {
            var full = Path.GetFullPath(input);
            if (!File.Exists(full))
            {
                result.Diagnostics.Add(new CompileDiagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    Message = $"Input file not found: {input}",
                    FilePath = input
                });
                continue;
            }

            if (!full.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                result.Diagnostics.Add(new CompileDiagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    Message = "Only .cs files are accepted for --lang cs.",
                    FilePath = full
                });
                continue;
            }

            absoluteInputs.Add(full);
        }

        if (!result.Success)
        {
            return result;
        }

        var commonRoot = FindCommonRoot(absoluteInputs);
        var pathMap = absoluteInputs.ToDictionary(
            f => f,
            f => MakeRelative(commonRoot, f),
            StringComparer.OrdinalIgnoreCase);

        var moduleNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (abs, rel) in pathMap)
        {
            var moduleName = NameUtil.ModuleNameFromPath(rel);
            if (moduleNames.TryGetValue(moduleName, out var other))
            {
                result.Diagnostics.Add(new CompileDiagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    Message = $"Duplicate module name '{moduleName}' from '{rel}' and '{other}'.",
                    FilePath = abs
                });
            }
            else
            {
                moduleNames[moduleName] = rel;
            }
        }

        if (!result.Success)
        {
            return result;
        }

        var trees = absoluteInputs
            .Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f), path: f))
            .ToList();

        var hasCSharpTopLevelStatements = trees.Any(t =>
            t.GetCompilationUnitRoot().Members.OfType<GlobalStatementSyntax>().Any());
        var outputKind = hasCSharpTopLevelStatements
            ? OutputKind.ConsoleApplication
            : OutputKind.DynamicallyLinkedLibrary;

        var metadataRefs = GetMetadataReferences(result);
        if (metadataRefs is null)
        {
            return result;
        }

        var compilation = CSharpCompilation.Create(
            "TFWRUserScript",
            trees,
            metadataRefs,
            new CSharpCompilationOptions(outputKind)
                .WithNullableContextOptions(NullableContextOptions.Enable)
                .WithOverflowChecks(false));

        foreach (var diag in compilation.GetDiagnostics().Where(d => d.Severity >= Microsoft.CodeAnalysis.DiagnosticSeverity.Warning))
        {
            var lineSpan = diag.Location.GetLineSpan();
            result.Diagnostics.Add(new CompileDiagnostic
            {
                Severity = diag.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error
                    ? DiagnosticSeverity.Error
                    : DiagnosticSeverity.Warning,
                Message = diag.GetMessage(),
                FilePath = lineSpan.Path,
                Line = lineSpan.StartLinePosition.Line + 1,
                Column = lineSpan.StartLinePosition.Character + 1
            });
        }

        if (result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            return result;
        }

        var typeModuleMap = new Dictionary<INamedTypeSymbol, string>(SymbolEqualityComparer.Default);
        foreach (var tree in trees)
        {
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetCompilationUnitRoot();
            var moduleName = NameUtil.ModuleNameFromPath(pathMap[tree.FilePath!]);
            foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
            {
                if (model.GetDeclaredSymbol(type) is { } symbol)
                {
                    typeModuleMap[symbol] = moduleName;
                }
            }
        }

        var program = new TfwrProgram();
        var emitter = new TfwrEmitter();

        foreach (var tree in trees)
        {
            var abs = tree.FilePath!;
            var rel = pathMap[abs];
            var moduleName = NameUtil.ModuleNameFromPath(rel);
            var model = compilation.GetSemanticModel(tree);
            var lowering = new LoweringContext(
                compilation, model, moduleName, typeModuleMap, result.Diagnostics, options.EmitTopLevelEntry);
            var module = lowering.LowerCompilationUnit(tree.GetCompilationUnitRoot(), rel);
            program.Modules.Add(module);
        }

        if (!result.Success)
        {
            return result;
        }

        Directory.CreateDirectory(options.OutputDirectory);
        foreach (var module in program.Modules)
        {
            var text = emitter.Emit(module);
            var outPath = Path.Combine(options.OutputDirectory, module.RelativeOutputPath.Replace('/', Path.DirectorySeparatorChar));
            var outDir = Path.GetDirectoryName(outPath);
            if (!string.IsNullOrEmpty(outDir))
            {
                Directory.CreateDirectory(outDir);
            }

            File.WriteAllText(outPath, text);
            result.EmittedFiles[module.RelativeOutputPath] = text;
        }

        return result;
    }

    /// <summary>
    /// Compiles in-memory sources without requiring caller-managed temp files.
    /// Intended for tests and tooling.
    /// </summary>
    /// <param name="sourcesByRelativePath">
    /// Source text keyed by relative path (used for module naming and diagnostics).
    /// </param>
    /// <param name="emitTopLevelEntry">
    /// When <see langword="true"/>, emits entry bodies as top-level statements.
    /// </param>
    /// <param name="language">Source language (<c>cs</c> or <c>ts</c>).</param>
    /// <returns>Compile result with diagnostics and emitted file contents.</returns>
    public static CompileResult CompileToMemory(
        IReadOnlyDictionary<string, string> sourcesByRelativePath,
        bool emitTopLevelEntry = false,
        string language = "cs")
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "tfwrc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        try
        {
            var inputs = new List<string>();
            foreach (var (rel, source) in sourcesByRelativePath)
            {
                var path = Path.Combine(tempRoot, rel.Replace('/', Path.DirectorySeparatorChar));
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(path, source);
                inputs.Add(path);
            }

            var outDir = Path.Combine(tempRoot, "_out");
            return Compile(new CompileOptions
            {
                InputFiles = inputs,
                OutputDirectory = outDir,
                Language = language,
                EmitTopLevelEntry = emitTopLevelEntry
            });
        }
        finally
        {
            try
            {
                Directory.Delete(tempRoot, recursive: true);
            }
            catch
            {
                // ignore cleanup failures in tests
            }
        }
    }

    /// <summary>
    /// Builds Roslyn metadata references from embedded net10.0 reference assemblies
    /// (<c>Basic.Reference.Assemblies.Net100</c>) plus on-disk <c>TFWR.Api.dll</c>.
    /// Avoids <see cref="System.Reflection.Assembly.Location"/> / TPA file paths, which break under single-file publish.
    /// </summary>
    private static List<MetadataReference>? GetMetadataReferences(CompileResult result)
    {
        var refs = new List<MetadataReference>();
        refs.AddRange(Net100.References.All);

        var apiPath = ResolveTfwrApiPath();
        if (apiPath is null)
        {
            result.Diagnostics.Add(new CompileDiagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Message =
                    "TFWR.Api.dll not found. Place it next to tfwrc (see AppContext.BaseDirectory) " +
                    "or under %TFWRC_HOME%\\bin\\TFWR.Api.dll."
            });
            return null;
        }

        refs.Add(MetadataReference.CreateFromFile(apiPath));
        return refs;
    }

    private static string? ResolveTfwrApiPath()
    {
        foreach (var candidate in EnumerateTfwrApiCandidates())
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateTfwrApiCandidates()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "TFWR.Api.dll");

        var home = Environment.GetEnvironmentVariable("TFWRC_HOME");
        if (!string.IsNullOrWhiteSpace(home))
        {
            yield return Path.Combine(home, "bin", "TFWR.Api.dll");
        }
    }

    private static string FindCommonRoot(List<string> files)
    {
        if (files.Count == 1)
        {
            return Path.GetDirectoryName(files[0])!;
        }

        var split = files.Select(f => Path.GetFullPath(f).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)).ToList();
        var minLen = split.Min(p => p.Length);
        var index = 0;
        for (; index < minLen - 1; index++)
        {
            var part = split[0][index];
            if (split.Any(p => !string.Equals(p[index], part, StringComparison.OrdinalIgnoreCase)))
            {
                break;
            }
        }

        return string.Join(Path.DirectorySeparatorChar, split[0].Take(index));
    }

    private static string MakeRelative(string root, string fullPath)
    {
        var rootFull = Path.GetFullPath(root);
        if (!rootFull.EndsWith(Path.DirectorySeparatorChar))
        {
            rootFull += Path.DirectorySeparatorChar;
        }

        var uriRoot = new Uri(rootFull);
        var uriFile = new Uri(Path.GetFullPath(fullPath));
        return Uri.UnescapeDataString(uriRoot.MakeRelativeUri(uriFile).ToString()).Replace('/', Path.DirectorySeparatorChar);
    }
}
