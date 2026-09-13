namespace TFWR.Compiler;

/// <summary>
/// Options that control a TFWR compile run.
/// </summary>
public sealed class CompileOptions
{
    /// <summary>Input C# source file paths.</summary>
    public required IReadOnlyList<string> InputFiles { get; init; }

    /// <summary>Directory where emitted <c>.py</c> files are written.</summary>
    public required string OutputDirectory { get; init; }

    /// <summary>Source language. Currently only <c>cs</c> is supported.</summary>
    public string Language { get; init; } = "cs";

    /// <summary>
    /// Emit Main / [TfwrEntry] bodies as module top-level statements (no def),
    /// for play before Unlocks.Functions.
    /// </summary>
    public bool EmitTopLevelEntry { get; init; }
}

/// <summary>
/// A single diagnostic produced during compilation.
/// </summary>
public sealed class CompileDiagnostic
{
    /// <summary>Severity of the diagnostic.</summary>
    public required DiagnosticSeverity Severity { get; init; }

    /// <summary>Human-readable diagnostic message.</summary>
    public required string Message { get; init; }

    /// <summary>Source file path, if known.</summary>
    public string? FilePath { get; init; }

    /// <summary>1-based line number, if known.</summary>
    public int? Line { get; init; }

    /// <summary>1-based column number, if known.</summary>
    public int? Column { get; init; }

    /// <inheritdoc />
    public override string ToString()
    {
        var location = FilePath is null
            ? ""
            : Line is null
                ? $"{FilePath}: "
                : $"{FilePath}({Line},{Column ?? 1}): ";
        return $"{location}{Severity.ToString().ToLowerInvariant()}: {Message}";
    }
}

/// <summary>
/// Severity levels for compile diagnostics.
/// </summary>
public enum DiagnosticSeverity
{
    /// <summary>Informational message.</summary>
    Info,

    /// <summary>Non-fatal warning.</summary>
    Warning,

    /// <summary>Fatal error that prevents successful compilation.</summary>
    Error
}

/// <summary>
/// Result of a compile run, including diagnostics and emitted source text.
/// </summary>
public sealed class CompileResult
{
    /// <summary>
    /// <see langword="true"/> when no error diagnostics were reported.
    /// </summary>
    public bool Success => Diagnostics.All(d => d.Severity != DiagnosticSeverity.Error);

    /// <summary>Diagnostics collected during compilation.</summary>
    public List<CompileDiagnostic> Diagnostics { get; } = [];

    /// <summary>
    /// Emitted file contents keyed by relative output path (forward slashes).
    /// </summary>
    public Dictionary<string, string> EmittedFiles { get; } = new(StringComparer.OrdinalIgnoreCase);
}
