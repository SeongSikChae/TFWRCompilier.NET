using System.Text.Json;
using TFWR.Compiler.Ir;

namespace TFWR.Compiler;

/// <summary>
/// Deserializes TFWR IR JSON produced by the TypeScript frontend bridge.
/// </summary>
internal static class IrJsonDeserializer
{
    public static (List<TfwrModule> Modules, List<CompileDiagnostic> Diagnostics) Deserialize(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var diagnostics = new List<CompileDiagnostic>();

        if (root.TryGetProperty("diagnostics", out var diags))
        {
            foreach (var d in diags.EnumerateArray())
            {
                diagnostics.Add(new CompileDiagnostic
                {
                    Severity = ParseSeverity(GetString(d, "severity")),
                    Message = GetString(d, "message") ?? "Unknown diagnostic",
                    FilePath = GetString(d, "filePath"),
                    Line = GetInt(d, "line"),
                    Column = GetInt(d, "column")
                });
            }
        }

        var modules = new List<TfwrModule>();
        if (root.TryGetProperty("modules", out var mods))
        {
            foreach (var m in mods.EnumerateArray())
            {
                modules.Add(ReadModule(m));
            }
        }

        return (modules, diagnostics);
    }

    private static TfwrModule ReadModule(JsonElement m)
    {
        var module = new TfwrModule
        {
            ModuleName = GetString(m, "moduleName") ?? "module",
            RelativeOutputPath = GetString(m, "relativeOutputPath") ?? "module.py",
            EntryFunctionName = GetString(m, "entryFunctionName")
        };

        if (m.TryGetProperty("imports", out var imports))
        {
            foreach (var i in imports.EnumerateArray())
            {
                var name = i.GetString();
                if (!string.IsNullOrEmpty(name))
                {
                    module.Imports.Add(name);
                }
            }
        }

        if (m.TryGetProperty("functions", out var functions))
        {
            foreach (var f in functions.EnumerateArray())
            {
                module.Functions.Add(ReadFunction(f));
            }
        }

        if (m.TryGetProperty("topLevelStatements", out var top))
        {
            foreach (var s in top.EnumerateArray())
            {
                module.TopLevelStatements.Add(ReadStmt(s));
            }
        }

        return module;
    }

    private static TfwrFunction ReadFunction(JsonElement f)
    {
        var fn = new TfwrFunction
        {
            Name = GetString(f, "name") ?? "fn"
        };

        if (f.TryGetProperty("parameters", out var parameters))
        {
            foreach (var p in parameters.EnumerateArray())
            {
                fn.Parameters.Add(new TfwrParameter
                {
                    Name = GetString(p, "name") ?? "_",
                    DefaultValue = p.TryGetProperty("defaultValue", out var dv) && dv.ValueKind is not JsonValueKind.Null
                        ? ReadExpr(dv)
                        : null
                });
            }
        }

        if (f.TryGetProperty("body", out var body))
        {
            foreach (var s in body.EnumerateArray())
            {
                fn.Body.Add(ReadStmt(s));
            }
        }

        return fn;
    }

    private static TfwrStmt ReadStmt(JsonElement s)
    {
        var kind = GetString(s, "kind") ?? throw new JsonException("Statement missing kind.");
        return kind switch
        {
            "ExprStmt" => new ExprStmt(ReadExpr(s.GetProperty("expression"))),
            "AssignStmt" => new AssignStmt(ReadExpr(s.GetProperty("target")), ReadExpr(s.GetProperty("value"))),
            "AugAssignStmt" => new AugAssignStmt(
                ReadExpr(s.GetProperty("target")),
                GetString(s, "op") ?? "+",
                ReadExpr(s.GetProperty("value"))),
            "IfStmt" => ReadIf(s),
            "WhileStmt" => new WhileStmt
            {
                Condition = ReadExpr(s.GetProperty("condition")),
                Body = ReadStmtList(s, "body")
            },
            "ForStmt" => new ForStmt
            {
                Variable = GetString(s, "variable") ?? "_",
                Iterable = ReadExpr(s.GetProperty("iterable")),
                Body = ReadStmtList(s, "body")
            },
            "ReturnStmt" => new ReturnStmt(
                s.TryGetProperty("value", out var rv) && rv.ValueKind is not JsonValueKind.Null
                    ? ReadExpr(rv)
                    : null),
            "BreakStmt" => new BreakStmt(),
            "ContinueStmt" => new ContinueStmt(),
            "PassStmt" => new PassStmt(),
            "GlobalStmt" => new GlobalStmt(
                s.TryGetProperty("names", out var names)
                    ? names.EnumerateArray().Select(n => n.GetString() ?? "_").ToList()
                    : []),
            _ => throw new JsonException($"Unknown statement kind '{kind}'.")
        };
    }

    private static IfStmt ReadIf(JsonElement s)
    {
        var ifStmt = new IfStmt
        {
            Condition = ReadExpr(s.GetProperty("condition"))
        };
        ifStmt.ThenBody.AddRange(ReadStmtList(s, "thenBody"));

        if (s.TryGetProperty("elifs", out var elifs))
        {
            foreach (var e in elifs.EnumerateArray())
            {
                var cond = ReadExpr(e.GetProperty("condition"));
                var body = ReadStmtList(e, "body");
                ifStmt.Elifs.Add((cond, body));
            }
        }

        if (s.TryGetProperty("elseBody", out var elseBody) && elseBody.ValueKind is JsonValueKind.Array)
        {
            ifStmt.ElseBody = ReadStmtList(s, "elseBody");
        }

        return ifStmt;
    }

    private static List<TfwrStmt> ReadStmtList(JsonElement parent, string name)
    {
        var list = new List<TfwrStmt>();
        if (!parent.TryGetProperty(name, out var arr) || arr.ValueKind is not JsonValueKind.Array)
        {
            return list;
        }

        foreach (var s in arr.EnumerateArray())
        {
            list.Add(ReadStmt(s));
        }

        return list;
    }

    private static TfwrExpr ReadExpr(JsonElement e)
    {
        if (e.ValueKind is JsonValueKind.Null)
        {
            return new LiteralExpr("None");
        }

        var kind = GetString(e, "kind") ?? throw new JsonException("Expression missing kind.");
        return kind switch
        {
            "NameExpr" => new NameExpr(GetString(e, "name") ?? "_"),
            "LiteralExpr" => new LiteralExpr(GetString(e, "text") ?? "None"),
            "UnaryExpr" => new UnaryExpr(GetString(e, "op") ?? "-", ReadExpr(e.GetProperty("operand"))),
            "BinaryExpr" => new BinaryExpr(
                ReadExpr(e.GetProperty("left")),
                GetString(e, "op") ?? "+",
                ReadExpr(e.GetProperty("right"))),
            "CallExpr" => new CallExpr(
                ReadExpr(e.GetProperty("callee")),
                e.TryGetProperty("arguments", out var args)
                    ? args.EnumerateArray().Select(ReadExpr).ToList()
                    : []),
            "AttributeExpr" => new AttributeExpr(
                ReadExpr(e.GetProperty("target")),
                GetString(e, "name") ?? "_"),
            "IndexExpr" => new IndexExpr(
                ReadExpr(e.GetProperty("target")),
                ReadExpr(e.GetProperty("index"))),
            "ListExpr" => new ListExpr(
                e.TryGetProperty("elements", out var le)
                    ? le.EnumerateArray().Select(ReadExpr).ToList()
                    : []),
            "SetExpr" => new SetExpr(
                e.TryGetProperty("elements", out var se)
                    ? se.EnumerateArray().Select(ReadExpr).ToList()
                    : []),
            "TupleExpr" => new TupleExpr(
                e.TryGetProperty("elements", out var te)
                    ? te.EnumerateArray().Select(ReadExpr).ToList()
                    : []),
            "DictExpr" => ReadDict(e),
            "ConditionalExpr" => new ConditionalExpr(
                ReadExpr(e.GetProperty("condition")),
                ReadExpr(e.GetProperty("whenTrue")),
                ReadExpr(e.GetProperty("whenFalse"))),
            _ => throw new JsonException($"Unknown expression kind '{kind}'.")
        };
    }

    private static DictExpr ReadDict(JsonElement e)
    {
        var entries = new List<(TfwrExpr Key, TfwrExpr Value)>();
        if (e.TryGetProperty("entries", out var arr))
        {
            foreach (var entry in arr.EnumerateArray())
            {
                entries.Add((ReadExpr(entry.GetProperty("key")), ReadExpr(entry.GetProperty("value"))));
            }
        }

        return new DictExpr(entries);
    }

    private static DiagnosticSeverity ParseSeverity(string? severity) =>
        severity?.ToLowerInvariant() switch
        {
            "warning" => DiagnosticSeverity.Warning,
            "info" => DiagnosticSeverity.Info,
            _ => DiagnosticSeverity.Error
        };

    private static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind is JsonValueKind.String ? p.GetString() : null;

    private static int? GetInt(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind is JsonValueKind.Number ? p.GetInt32() : null;
}
