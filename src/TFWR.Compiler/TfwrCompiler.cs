using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TFWR.Api;
using TFWR.Compiler.Emit;
using TFWR.Compiler.Ir;

namespace TFWR.Compiler;

/// <summary>
/// Compiles C# sources that use <c>TFWR.Api</c> into TFWR Python modules.
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
        var result = new CompileResult();

        if (!string.Equals(options.Language, "cs", StringComparison.OrdinalIgnoreCase))
        {
            result.Diagnostics.Add(new CompileDiagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Message = $"Language '{options.Language}' is not supported. Use --lang cs."
            });
            return result;
        }

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

        var compilation = CSharpCompilation.Create(
            "TFWRUserScript",
            trees,
            GetMetadataReferences(),
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
    /// <returns>Compile result with diagnostics and emitted file contents.</returns>
    public static CompileResult CompileToMemory(
        IReadOnlyDictionary<string, string> sourcesByRelativePath,
        bool emitTopLevelEntry = false)
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
                Language = "cs",
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

    private static List<MetadataReference> GetMetadataReferences()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var refs = new List<MetadataReference>();

        void Add(string? path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path) || !set.Add(path))
            {
                return;
            }

            refs.Add(MetadataReference.CreateFromFile(path));
        }

        var tpa = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES");
        if (!string.IsNullOrEmpty(tpa))
        {
            foreach (var path in tpa.Split(Path.PathSeparator))
            {
                Add(path);
            }
        }
        else
        {
            var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
            foreach (var name in new[]
                     {
                         "System.Runtime.dll", "System.Collections.dll", "System.Linq.dll",
                         "System.Console.dll", "netstandard.dll", "System.Private.CoreLib.dll"
                     })
            {
                Add(Path.Combine(runtimeDir, name));
            }

            Add(typeof(object).Assembly.Location);
            Add(typeof(Enumerable).Assembly.Location);
            Add(typeof(List<>).Assembly.Location);
        }

        Add(typeof(Game).Assembly.Location);
        return refs;
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
