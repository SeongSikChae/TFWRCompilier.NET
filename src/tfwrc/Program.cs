using TFWR.Compiler;

namespace tfwrc;

public static class Program
{
    public static int Main(string[] args)
    {
        string? lang = null;
        string? outDir = null;
        var emitTopLevelEntry = false;
        var inputs = new List<string>();

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is "-h" or "--help")
            {
                PrintHelp();
                return 0;
            }

            if (arg == "--lang")
            {
                if (i + 1 >= args.Length)
                {
                    Console.Error.WriteLine("error: --lang requires a value.");
                    return 1;
                }

                lang = args[++i];
                continue;
            }

            if (arg.StartsWith("--lang=", StringComparison.Ordinal))
            {
                lang = arg["--lang=".Length..];
                continue;
            }

            if (arg == "--out")
            {
                if (i + 1 >= args.Length)
                {
                    Console.Error.WriteLine("error: --out requires a value.");
                    return 1;
                }

                outDir = args[++i];
                continue;
            }

            if (arg.StartsWith("--out=", StringComparison.Ordinal))
            {
                outDir = arg["--out=".Length..];
                continue;
            }

            if (arg is "--toplevel" or "--top-level")
            {
                emitTopLevelEntry = true;
                continue;
            }

            if (arg.StartsWith('-'))
            {
                Console.Error.WriteLine($"error: unknown option '{arg}'.");
                PrintHelp();
                return 1;
            }

            inputs.Add(arg);
        }

        if (string.IsNullOrWhiteSpace(lang))
        {
            Console.Error.WriteLine("error: --lang is required.");
            PrintHelp();
            return 1;
        }

        if (string.IsNullOrWhiteSpace(outDir))
        {
            Console.Error.WriteLine("error: --out is required.");
            PrintHelp();
            return 1;
        }

        if (inputs.Count == 0)
        {
            Console.Error.WriteLine("error: at least one input file is required.");
            PrintHelp();
            return 1;
        }

        var result = TfwrCompiler.Compile(new CompileOptions
        {
            Language = lang,
            OutputDirectory = Path.GetFullPath(outDir),
            InputFiles = inputs,
            EmitTopLevelEntry = emitTopLevelEntry
        });

        foreach (var diagnostic in result.Diagnostics)
        {
            var writer = diagnostic.Severity == DiagnosticSeverity.Error
                ? Console.Error
                : Console.Out;
            writer.WriteLine(diagnostic.ToString());
        }

        if (!result.Success)
        {
            return 1;
        }

        Console.WriteLine($"Wrote {result.EmittedFiles.Count} file(s) to {Path.GetFullPath(outDir)}");
        return 0;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("""
            tfwrc — compile source code to The Farmer Was Replaced scripts

            Usage:
              tfwrc --lang cs --out <output-dir> [--toplevel] <input.cs> [input.cs ...]
              tfwrc --lang ts --out <output-dir> [--toplevel] <input.ts> [input.ts ...]

            Options:
              --lang cs|ts    Source language (cs = C# / Roslyn, ts = TypeScript / Node)
              --out <dir>     Output directory for generated .py modules
              --toplevel      Emit Main/[TfwrEntry]/main as top-level statements (no def)
                              Useful before Unlocks.Functions is unlocked
              -h, --help      Show this help

            TypeScript (--lang ts) requires Node.js 20+ and the bundled TsFrontend bridge.
            """);
    }
}
