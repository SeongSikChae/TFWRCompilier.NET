using System.Diagnostics;
using System.Text;
using System.Text.Json;
using TFWR.Compiler.Emit;
using TFWR.Compiler.Ir;

namespace TFWR.Compiler;

internal static class TsCompiler
{
    public static CompileResult Compile(CompileOptions options)
    {
        var result = new CompileResult();

        if (options.InputFiles.Count == 0)
        {
            result.Diagnostics.Add(new CompileDiagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Message = "At least one input .ts file is required."
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

            if (!full.EndsWith(".ts", StringComparison.OrdinalIgnoreCase) &&
                !full.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase))
            {
                result.Diagnostics.Add(new CompileDiagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    Message = "Only .ts/.tsx files are accepted for --lang ts.",
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

        var bridgeDir = ResolveTsFrontendDir();
        if (bridgeDir is null)
        {
            result.Diagnostics.Add(new CompileDiagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Message =
                    "TsFrontend bridge not found. Ensure TsFrontend/lower.mjs is next to TFWR.Compiler " +
                    "(AppContext.BaseDirectory/TsFrontend) or under the source tree."
            });
            return result;
        }

        var lowerJs = Path.Combine(bridgeDir, "lower.mjs");
        if (!File.Exists(lowerJs))
        {
            result.Diagnostics.Add(new CompileDiagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Message = $"TsFrontend lower.mjs not found at {lowerJs}."
            });
            return result;
        }

        var nodeModules = Path.Combine(bridgeDir, "node_modules", "typescript");
        if (!Directory.Exists(nodeModules))
        {
            result.Diagnostics.Add(new CompileDiagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Message =
                    $"TypeScript package missing under {bridgeDir}. Run 'npm ci' in TsFrontend " +
                    "(required for --lang ts)."
            });
            return result;
        }

        var tfwrDts = ResolveTfwrDtsPath(bridgeDir);
        if (tfwrDts is null)
        {
            result.Diagnostics.Add(new CompileDiagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Message = "tfwr.d.ts not found. Expected next to TsFrontend or under TFWR.Api.Ts."
            });
            return result;
        }

        var request = new
        {
            emitTopLevelEntry = options.EmitTopLevelEntry,
            tfwrDtsPath = tfwrDts,
            files = absoluteInputs.Select(abs => new
            {
                path = abs,
                relativePath = pathMap[abs].Replace('\\', '/')
            }).ToList()
        };

        var requestPath = Path.Combine(Path.GetTempPath(), "tfwrc-ts-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(requestPath, JsonSerializer.Serialize(request));

            var psi = new ProcessStartInfo
            {
                FileName = ResolveNodeExecutable(),
                ArgumentList = { lowerJs, requestPath },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = bridgeDir
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                result.Diagnostics.Add(new CompileDiagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    Message = "Failed to start Node.js. Install Node.js 20+ for --lang ts."
                });
                return result;
            }

            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0)
            {
                result.Diagnostics.Add(new CompileDiagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    Message = string.IsNullOrWhiteSpace(stderr)
                        ? $"TypeScript bridge exited with code {process.ExitCode}."
                        : stderr.Trim()
                });
                return result;
            }

            if (string.IsNullOrWhiteSpace(stdout))
            {
                result.Diagnostics.Add(new CompileDiagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    Message = string.IsNullOrWhiteSpace(stderr)
                        ? "TypeScript bridge returned empty output."
                        : stderr.Trim()
                });
                return result;
            }

            List<TfwrModule> modules;
            List<CompileDiagnostic> bridgeDiagnostics;
            try
            {
                (modules, bridgeDiagnostics) = IrJsonDeserializer.Deserialize(stdout);
            }
            catch (Exception ex)
            {
                result.Diagnostics.Add(new CompileDiagnostic
                {
                    Severity = DiagnosticSeverity.Error,
                    Message = $"Failed to parse TypeScript IR JSON: {ex.Message}"
                });
                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    result.Diagnostics.Add(new CompileDiagnostic
                    {
                        Severity = DiagnosticSeverity.Error,
                        Message = stderr.Trim()
                    });
                }

                return result;
            }

            result.Diagnostics.AddRange(bridgeDiagnostics);
            if (!result.Success)
            {
                return result;
            }

            Directory.CreateDirectory(options.OutputDirectory);
            var emitter = new TfwrEmitter();
            foreach (var module in modules)
            {
                var text = emitter.Emit(module);
                var outPath = Path.Combine(
                    options.OutputDirectory,
                    module.RelativeOutputPath.Replace('/', Path.DirectorySeparatorChar));
                var outDir = Path.GetDirectoryName(outPath);
                if (!string.IsNullOrEmpty(outDir))
                {
                    Directory.CreateDirectory(outDir);
                }

                File.WriteAllText(outPath, text, Encoding.UTF8);
                result.EmittedFiles[module.RelativeOutputPath] = text;
            }

            return result;
        }
        finally
        {
            try
            {
                File.Delete(requestPath);
            }
            catch
            {
                // ignore
            }
        }
    }

    private static string ResolveNodeExecutable()
    {
        var fromEnv = Environment.GetEnvironmentVariable("TFWRC_NODE");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv;
        }

        return OperatingSystem.IsWindows() ? "node.exe" : "node";
    }

    private static string? ResolveTsFrontendDir()
    {
        foreach (var candidate in EnumerateTsFrontendCandidates())
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var full = Path.GetFullPath(candidate);
            var lowerJs = Path.Combine(full, "lower.mjs");
            var typescript = Path.Combine(full, "node_modules", "typescript");
            if (File.Exists(lowerJs) && Directory.Exists(typescript))
            {
                return full;
            }
        }

        // Fall back to a directory that at least has the bridge script (error message will mention npm ci).
        foreach (var candidate in EnumerateTsFrontendCandidates())
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var full = Path.GetFullPath(candidate);
            if (File.Exists(Path.Combine(full, "lower.mjs")))
            {
                return full;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateTsFrontendCandidates()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "TsFrontend");

        var home = Environment.GetEnvironmentVariable("TFWRC_HOME");
        if (!string.IsNullOrWhiteSpace(home))
        {
            yield return Path.Combine(home, "TsFrontend");
            yield return Path.Combine(home, "bin", "TsFrontend");
        }

        // Walk up from BaseDirectory looking for src/TFWR.Compiler/TsFrontend (dev + test hosts).
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent)
        {
            yield return Path.Combine(dir.FullName, "TsFrontend");
            yield return Path.Combine(dir.FullName, "src", "TFWR.Compiler", "TsFrontend");
            yield return Path.Combine(dir.FullName, "TFWR.Compiler", "TsFrontend");
        }
    }

    private static string? ResolveTfwrDtsPath(string bridgeDir)
    {
        foreach (var candidate in new[]
                 {
                     Path.Combine(bridgeDir, "tfwr.d.ts"),
                     Path.GetFullPath(Path.Combine(bridgeDir, "..", "..", "TFWR.Api.Ts", "tfwr.d.ts")),
                     Path.Combine(AppContext.BaseDirectory, "tfwr.d.ts"),
                     Path.Combine(AppContext.BaseDirectory, "TFWR.Api.Ts", "tfwr.d.ts")
                 })
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
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
