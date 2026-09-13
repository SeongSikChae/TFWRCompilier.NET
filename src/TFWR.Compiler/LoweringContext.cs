using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TFWR.Compiler.Ir;

namespace TFWR.Compiler;

internal sealed class LoweringContext(
    CSharpCompilation compilation,
    SemanticModel model,
    string moduleName,
    IReadOnlyDictionary<INamedTypeSymbol, string> typeModuleMap,
    List<CompileDiagnostic> diagnostics,
    bool emitTopLevelEntry = false)
{
    private readonly CSharpCompilation _compilation = compilation;
    private readonly SemanticModel _model = model;
    private readonly string _moduleName = moduleName;
    private readonly IReadOnlyDictionary<INamedTypeSymbol, string> _typeModuleMap = typeModuleMap;
    private readonly List<CompileDiagnostic> _diagnostics = diagnostics;
    private readonly bool _emitTopLevelEntry = emitTopLevelEntry;
    private readonly HashSet<string> _imports = new(StringComparer.Ordinal);
    private int _tempCounter;
    private List<TfwrStmt>? _hoist;
    private IMethodSymbol? _currentMethod;

    private static readonly HashSet<string> GameTypeNames =
    [
        "Game", "Direction", "Entities", "Items", "Grounds", "Hats", "Unlocks", "Leaderboards", "Drone"
    ];

    public TfwrModule LowerCompilationUnit(CompilationUnitSyntax root, string relativeCsPath)
    {
        RejectUnsupported(root);

        var module = new TfwrModule
        {
            ModuleName = _moduleName,
            RelativeOutputPath = NameUtil.OutputPyPath(relativeCsPath)
        };

        foreach (var type in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
        {
            if (type is not ClassDeclarationSyntax and not StructDeclarationSyntax and not RecordDeclarationSyntax)
            {
                Error(type, $"Type kind '{type.Keyword}' is not supported.");
                continue;
            }

            LowerType(type, module);
        }

        var globalStatements = root.Members.OfType<GlobalStatementSyntax>().ToList();
        if (globalStatements.Count > 0)
        {
            var stmts = new List<TfwrStmt>();
            foreach (var global in globalStatements)
            {
                stmts.AddRange(LowerStatement(global.Statement));
            }

            if (_emitTopLevelEntry)
            {
                module.TopLevelStatements.AddRange(stmts);
            }
            else
            {
                var fn = new TfwrFunction { Name = "main" };
                fn.Body.AddRange(stmts);
                module.Functions.Add(fn);
                module.EntryFunctionName = "main";
            }
        }

        foreach (var import in _imports.OrderBy(x => x, StringComparer.Ordinal))
        {
            if (!string.Equals(import, _moduleName, StringComparison.Ordinal))
            {
                module.Imports.Add(import);
            }
        }

        if (_emitTopLevelEntry && module.Functions.Count > 0)
        {
            Error(root,
                "--toplevel emits entry code without def, but this module still has functions. " +
                "Remove other methods/constructors or unlock Unlocks.Functions.");
        }

        return module;
    }

    private void LowerType(TypeDeclarationSyntax type, TfwrModule module)
    {
        var symbol = _model.GetDeclaredSymbol(type);
        if (symbol is null)
        {
            return;
        }

        if (symbol.IsGenericType)
        {
            Error(type, "User-defined generic types are not supported.");
            return;
        }

        if (symbol.Interfaces.Length > 0)
        {
            // Interfaces are compile-time only; ignore at emit time.
        }

        if (symbol.BaseType is { SpecialType: not SpecialType.System_Object } baseType
            && !IsFrameworkType(baseType))
        {
            // Single inheritance: fields merged in ctor; methods use most-derived naming for v1.
            if (baseType.BaseType is { SpecialType: not SpecialType.System_Object }
                && !IsFrameworkType(baseType.BaseType))
            {
                // deeper chains still ok if single path
            }
        }

        var fields = symbol.GetMembers().OfType<IFieldSymbol>()
            .Where(f => !f.IsStatic && !f.IsImplicitlyDeclared)
            .ToList();
        var autoProps = symbol.GetMembers().OfType<IPropertySymbol>()
            .Where(p => !p.IsStatic && p.GetMethod is not null)
            .ToList();

        foreach (var ctor in type.Members.OfType<ConstructorDeclarationSyntax>())
        {
            LowerConstructor(symbol, ctor, module);
        }

        if (type.ParameterList is { Parameters.Count: > 0 } primaryParams
            && !type.Members.OfType<ConstructorDeclarationSyntax>().Any())
        {
            // Positional record / C# primary constructor — no explicit ctor body in source.
            LowerPrimaryConstructor(symbol, primaryParams, module);
        }

        var hasInstanceMethods = type.Members.OfType<MethodDeclarationSyntax>().Any(m =>
            _model.GetDeclaredSymbol(m) is { IsStatic: false });
        var hasCtor = type.Members.OfType<ConstructorDeclarationSyntax>().Any()
                      || type.ParameterList is { Parameters.Count: > 0 };
        var needsDefaultCtor = !symbol.IsStatic
            && !hasCtor
            && (fields.Count > 0 || autoProps.Count > 0 || hasInstanceMethods);

        if (needsDefaultCtor)
        {
            // synthesize default ctor only when instances are meaningful
            var fn = new TfwrFunction { Name = $"{symbol.Name}_new" };
            fn.Body.Add(new ReturnStmt(BuildInstanceDict(symbol, [])));
            module.Functions.Add(fn);
        }

        foreach (var method in type.Members.OfType<MethodDeclarationSyntax>())
        {
            LowerMethod(symbol, method, module);
        }

        foreach (var prop in type.Members.OfType<PropertyDeclarationSyntax>())
        {
            LowerProperty(symbol, prop, module);
        }
    }

    private void LowerConstructor(
        INamedTypeSymbol type,
        ConstructorDeclarationSyntax ctor,
        TfwrModule module)
    {
        var methodSymbol = _model.GetDeclaredSymbol(ctor);
        _currentMethod = methodSymbol;
        var fn = new TfwrFunction { Name = $"{type.Name}_new" };
        if (ctor.ParameterList is { } pl)
        {
            foreach (var p in pl.Parameters)
            {
                fn.Parameters.Add(new TfwrParameter
                {
                    Name = NameUtil.SanitizeIdentifier(p.Identifier.Text),
                    DefaultValue = p.Default?.Value is { } dv ? LowerExpression(dv) : null
                });
            }
        }

        var body = ctor.Body ?? (ctor.ExpressionBody is { } arrow
            ? SyntaxFactory.Block(SyntaxFactory.ExpressionStatement(arrow.Expression))
            : null);

        if (body is null)
        {
            Error(ctor, "Constructor body required.");
            return;
        }

        // Create self dict first, then run body with self assignments, then return self.
        fn.Body.Add(new AssignStmt(new NameExpr("self"), BuildInstanceDict(type, [])));
        foreach (var stmt in body.Statements)
        {
            fn.Body.AddRange(LowerStatement(stmt));
        }

        // Rewrite: assignments to fields without self in ctor body already use this. - handled in member access
        fn.Body.Add(new ReturnStmt(new NameExpr("self")));
        module.Functions.Add(fn);
        _currentMethod = null;
    }

    private void LowerPrimaryConstructor(
        INamedTypeSymbol type,
        ParameterListSyntax primaryParams,
        TfwrModule module)
    {
        var fn = new TfwrFunction { Name = $"{type.Name}_new" };
        var overrides = new Dictionary<string, TfwrExpr>(StringComparer.Ordinal);

        foreach (var p in primaryParams.Parameters)
        {
            var name = NameUtil.SanitizeIdentifier(p.Identifier.Text);
            fn.Parameters.Add(new TfwrParameter
            {
                Name = name,
                DefaultValue = p.Default?.Value is { } dv ? LowerExpression(dv) : null
            });
            overrides[p.Identifier.Text] = new NameExpr(name);
        }

        fn.Body.Add(new ReturnStmt(BuildInstanceDict(type, overrides)));
        module.Functions.Add(fn);
    }

    private static DictExpr BuildInstanceDict(
        INamedTypeSymbol type,
        Dictionary<string, TfwrExpr> overrides)
    {
        var entries = new List<(TfwrExpr Key, TfwrExpr Value)>();
        foreach (var f in GetInstanceStorage(type))
        {
            var key = new LiteralExpr($"\"{f}\"");
            entries.Add((key, overrides.TryGetValue(f, out var v) ? v : new LiteralExpr("None")));
        }

        return new DictExpr(entries);
    }

    private static List<string> GetInstanceStorage(INamedTypeSymbol type)
    {
        var names = new List<string>();
        void Walk(INamedTypeSymbol t)
        {
            if (t.BaseType is { SpecialType: not SpecialType.System_Object } b && !IsFrameworkType(b))
            {
                Walk(b);
            }

            foreach (var f in t.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic && !f.IsImplicitlyDeclared))
            {
                names.Add(f.Name);
            }

            foreach (var p in t.GetMembers().OfType<IPropertySymbol>()
                         .Where(p => !p.IsStatic && IsInstanceDictProperty(p)))
            {
                names.Add(p.Name);
            }
        }

        Walk(type);
        return [.. names.Distinct(StringComparer.Ordinal)];
    }

    private static bool IsInstanceDictProperty(IPropertySymbol p)
    {
        if (p.IsIndexer || p.GetMethod is null)
        {
            return false;
        }

        if (IsAutoProperty(p))
        {
            return true;
        }

        // Positional record / primary-constructor properties are declared via ParameterSyntax.
        foreach (var r in p.DeclaringSyntaxReferences)
        {
            if (r.GetSyntax() is ParameterSyntax)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAutoProperty(IPropertySymbol p)
    {
        foreach (var r in p.DeclaringSyntaxReferences)
        {
            if (r.GetSyntax() is PropertyDeclarationSyntax { AccessorList.Accessors: { } accessors }
                && accessors.All(a => a.Body is null && a.ExpressionBody is null))
            {
                return true;
            }
        }

        return false;
    }

    private void LowerMethod(INamedTypeSymbol type, MethodDeclarationSyntax method, TfwrModule module)
    {
        var symbol = _model.GetDeclaredSymbol(method);
        if (symbol is null)
        {
            return;
        }

        _currentMethod = symbol;

        var bodyStmts = new List<TfwrStmt>();
        if (method.Body is { } body)
        {
            foreach (var stmt in body.Statements)
            {
                bodyStmts.AddRange(LowerStatement(stmt));
            }
        }
        else if (method.ExpressionBody is { } arrow)
        {
            if (symbol.ReturnsVoid)
            {
                bodyStmts.AddRange(LowerStatement(SyntaxFactory.ExpressionStatement(arrow.Expression)));
            }
            else
            {
                var value = LowerExpressionHoisted(arrow.Expression, bodyStmts);
                bodyStmts.Add(new ReturnStmt(value));
            }
        }
        else
        {
            bodyStmts.Add(new PassStmt());
        }

        if (_emitTopLevelEntry && IsEntryPoint(symbol))
        {
            if (!symbol.IsStatic)
            {
                Error(method, "--toplevel entry must be a static Main or [TfwrEntry] method.");
                _currentMethod = null;
                return;
            }

            if (method.ParameterList.Parameters.Count > 0)
            {
                Error(method, "--toplevel entry must not take parameters.");
                _currentMethod = null;
                return;
            }

            module.TopLevelStatements.AddRange(bodyStmts);
            _currentMethod = null;
            return;
        }

        if (IsEntryPoint(symbol))
        {
            module.EntryFunctionName = GetFunctionName(type, symbol);
        }

        var fn = new TfwrFunction { Name = GetFunctionName(type, symbol) };
        if (!symbol.IsStatic)
        {
            fn.Parameters.Add(new TfwrParameter { Name = "self" });
        }

        foreach (var p in method.ParameterList.Parameters)
        {
            fn.Parameters.Add(new TfwrParameter
            {
                Name = NameUtil.SanitizeIdentifier(p.Identifier.Text),
                DefaultValue = p.Default?.Value is { } dv ? LowerExpression(dv) : null
            });
        }

        fn.Body.AddRange(bodyStmts);
        module.Functions.Add(fn);
        _currentMethod = null;
    }

    private void LowerProperty(INamedTypeSymbol type, PropertyDeclarationSyntax prop, TfwrModule module)
    {
        var symbol = _model.GetDeclaredSymbol(prop);
        if (symbol is null || symbol.IsStatic)
        {
            return;
        }

        if (prop.AccessorList is null)
        {
            return;
        }

        foreach (var accessor in prop.AccessorList.Accessors)
        {
            if (accessor.Body is null && accessor.ExpressionBody is null)
            {
                continue; // auto-prop: dict field
            }

            var isGet = accessor.IsKind(SyntaxKind.GetAccessorDeclaration);
            var name = isGet ? $"{type.Name}_get_{symbol.Name}" : $"{type.Name}_set_{symbol.Name}";
            var fn = new TfwrFunction { Name = name };
            fn.Parameters.Add(new TfwrParameter { Name = "self" });
            if (!isGet)
            {
                fn.Parameters.Add(new TfwrParameter { Name = "value" });
            }

            if (accessor.Body is { } body)
            {
                foreach (var stmt in body.Statements)
                {
                    fn.Body.AddRange(LowerStatement(stmt));
                }
            }
            else if (accessor.ExpressionBody is { } arrow)
            {
                if (isGet)
                {
                    var value = LowerExpressionHoisted(arrow.Expression, fn.Body);
                    fn.Body.Add(new ReturnStmt(value));
                }
                else
                {
                    fn.Body.AddRange(LowerStatement(SyntaxFactory.ExpressionStatement(arrow.Expression)));
                }
            }

            module.Functions.Add(fn);
        }
    }

    private static string GetFunctionName(INamedTypeSymbol type, IMethodSymbol method)
    {
        if (method.MethodKind == MethodKind.Constructor)
        {
            return $"{type.Name}_new";
        }

        if (IsEntryPoint(method) && method.Name is "Main")
        {
            return "main";
        }

        return method.IsStatic
            ? $"{type.Name}_{method.Name}"
            : $"{type.Name}_{method.Name}";
    }

    private static bool IsEntryPoint(IMethodSymbol method)
    {
        if (!method.IsStatic)
        {
            return false;
        }

        if (method.Name == "Main")
        {
            return true;
        }

        return method.GetAttributes().Any(a =>
            a.AttributeClass?.Name is "TfwrEntryAttribute" or "TfwrEntry");
    }

    private IEnumerable<TfwrStmt> LowerStatement(StatementSyntax stmt)
    {
        switch (stmt)
        {
            case BlockSyntax block:
                return [.. block.Statements.SelectMany(LowerStatement)];
            case ExpressionStatementSyntax exprStmt:
                return LowerExpressionStatement(exprStmt.Expression);
            case LocalDeclarationStatementSyntax local:
                return LowerLocalDeclaration(local);
            case IfStatementSyntax ifStmt:
                return LowerIf(ifStmt);
            case WhileStatementSyntax whileStmt:
                return LowerWhile(whileStmt);
            case ForStatementSyntax forStmt:
                return LowerFor(forStmt);
            case ForEachStatementSyntax foreachStmt:
                return LowerForEach(foreachStmt);
            case ForEachVariableStatementSyntax foreachVarStmt:
                return LowerForEachVariable(foreachVarStmt);
            case ReturnStatementSyntax ret:
                return LowerReturn(ret);
            case BreakStatementSyntax:
                return [new BreakStmt()];
            case ContinueStatementSyntax:
                return [new ContinueStmt()];
            case EmptyStatementSyntax:
                return [new PassStmt()];
            case SwitchStatementSyntax sw:
                return LowerSwitch(sw);
            case ThrowStatementSyntax throwStmt:
                Error(throwStmt, "throw is not supported in TFWR.");
                return [new PassStmt()];
            case TryStatementSyntax tryStmt:
                Error(tryStmt, "try/catch is not supported in TFWR.");
                return [new PassStmt()];
            case UsingStatementSyntax usingStmt:
                Error(usingStmt, "using statement is not supported.");
                return [new PassStmt()];
            case LockStatementSyntax lockStmt:
                Error(lockStmt, "lock is not supported.");
                return [new PassStmt()];
            case CheckedStatementSyntax or UnsafeStatementSyntax:
                Error(stmt, "checked/unsafe statements are not supported.");
                return [new PassStmt()];
            case LocalFunctionStatementSyntax localFn:
                Error(localFn, "Local functions must be lowered as nested defs — not supported in v1; use private methods.");
                return [new PassStmt()];
            default:
                Error(stmt, $"Statement '{stmt.Kind()}' is not supported.");
                return [new PassStmt()];
        }
    }

    private List<TfwrStmt> LowerExpressionStatement(ExpressionSyntax expression)
    {
        if (expression is AssignmentExpressionSyntax assign)
        {
            return LowerAssignment(assign);
        }

        if (expression is PrefixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.PreIncrementExpression or (int)SyntaxKind.PreDecrementExpression } pre)
        {
            var stmts = new List<TfwrStmt>();
            var op = pre.IsKind(SyntaxKind.PreIncrementExpression) ? "+" : "-";
            var operand = LowerExpressionHoisted(pre.Operand, stmts);
            stmts.Add(new AugAssignStmt(operand, op, new LiteralExpr("1")));
            return stmts;
        }

        if (expression is PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.PostIncrementExpression or (int)SyntaxKind.PostDecrementExpression } post)
        {
            var stmts = new List<TfwrStmt>();
            var op = post.IsKind(SyntaxKind.PostIncrementExpression) ? "+" : "-";
            var operand = LowerExpressionHoisted(post.Operand, stmts);
            stmts.Add(new AugAssignStmt(operand, op, new LiteralExpr("1")));
            return stmts;
        }

        {
            var stmts = new List<TfwrStmt>();
            var expr = LowerExpressionHoisted(expression, stmts);
            stmts.Add(new ExprStmt(expr));
            return stmts;
        }
    }

    private List<TfwrStmt> LowerAssignment(AssignmentExpressionSyntax assign)
    {
        var stmts = new List<TfwrStmt>();
        var opKind = assign.Kind();
        if (opKind == SyntaxKind.SimpleAssignmentExpression)
        {
            TfwrExpr left;
            TfwrExpr right;
            var prev = _hoist;
            _hoist = stmts;
            try
            {
                left = LowerExpression(assign.Left);
                right = LowerExpression(assign.Right);
            }
            finally
            {
                _hoist = prev;
            }

            stmts.Add(new AssignStmt(left, right));
            return stmts;
        }

        var op = opKind switch
        {
            SyntaxKind.AddAssignmentExpression => "+",
            SyntaxKind.SubtractAssignmentExpression => "-",
            SyntaxKind.MultiplyAssignmentExpression => "*",
            SyntaxKind.DivideAssignmentExpression => "/",
            SyntaxKind.ModuloAssignmentExpression => "%",
            _ => null
        };

        if (op is null)
        {
            Error(assign, $"Assignment operator '{assign.OperatorToken.Text}' is not supported.");
            return [new PassStmt()];
        }

        {
            TfwrExpr left;
            TfwrExpr right;
            var prev = _hoist;
            _hoist = stmts;
            try
            {
                left = LowerExpression(assign.Left);
                right = LowerExpression(assign.Right);
            }
            finally
            {
                _hoist = prev;
            }

            stmts.Add(new AugAssignStmt(left, op, right));
            return stmts;
        }
    }

    private List<TfwrStmt> LowerLocalDeclaration(LocalDeclarationStatementSyntax local)
    {
        var list = new List<TfwrStmt>();
        foreach (var v in local.Declaration.Variables)
        {
            var name = NameUtil.SanitizeIdentifier(v.Identifier.Text);
            var value = v.Initializer?.Value is { } init
                ? LowerExpressionHoisted(init, list)
                : new LiteralExpr("None");
            list.Add(new AssignStmt(new NameExpr(name), value));
        }

        return list;
    }

    private List<TfwrStmt> LowerIf(IfStatementSyntax ifStmt)
    {
        var stmts = new List<TfwrStmt>();
        var result = new IfStmt
        {
            Condition = LowerExpressionHoisted(ifStmt.Condition, stmts)
        };
        result.ThenBody.AddRange(LowerStatement(ifStmt.Statement));
        AttachElseChain(result, ifStmt.Else, result);
        stmts.Add(result);
        return stmts;
    }

    private void AttachElseChain(IfStmt target, ElseClauseSyntax? elseClause, IfStmt elifOwner)
    {
        // C# else-if → Python elif when the condition needs no hoisted prelude.
        // If hoisting is required, nest from that point so preludes stay branch-local.
        while (elseClause is not null)
        {
            if (elseClause.Statement is IfStatementSyntax elseIf)
            {
                var prelude = new List<TfwrStmt>();
                var cond = LowerExpressionHoisted(elseIf.Condition, prelude);
                var thenBody = LowerStatement(elseIf.Statement).ToList();

                if (prelude.Count == 0)
                {
                    elifOwner.Elifs.Add((cond, thenBody));
                    elseClause = elseIf.Else;
                    continue;
                }

                var nested = new IfStmt { Condition = cond };
                nested.ThenBody.AddRange(thenBody);
                AttachElseChain(nested, elseIf.Else, nested);
                var elseBody = new List<TfwrStmt>();
                elseBody.AddRange(prelude);
                elseBody.Add(nested);
                target.ElseBody = elseBody;
                return;
            }

            target.ElseBody = [.. LowerStatement(elseClause.Statement)];
            return;
        }
    }

    private IEnumerable<TfwrStmt> LowerWhile(WhileStatementSyntax whileStmt)
    {
        var condPrelude = new List<TfwrStmt>();
        var cond = LowerExpressionHoisted(whileStmt.Condition, condPrelude);
        var body = LowerStatement(whileStmt.Statement).ToList();

        if (condPrelude.Count == 0)
        {
            return
            [
                new WhileStmt
                {
                    Condition = cond,
                    Body = body
                }
            ];
        }

        // Re-evaluate condition each iteration when it required statement hoisting.
        var loopBody = new List<TfwrStmt>();
        loopBody.AddRange(condPrelude);
        loopBody.Add(new IfStmt
        {
            Condition = new UnaryExpr("not", cond),
            ThenBody = { new BreakStmt() }
        });
        loopBody.AddRange(body);
        return
        [
            new WhileStmt
            {
                Condition = new LiteralExpr("True"),
                Body = loopBody
            }
        ];
    }

    private List<TfwrStmt> LowerForEach(ForEachStatementSyntax foreachStmt)
    {
        var stmts = new List<TfwrStmt>();
        var iterable = LowerExpressionHoisted(foreachStmt.Expression, stmts);
        stmts.Add(new ForStmt
        {
            Variable = NameUtil.SanitizeIdentifier(foreachStmt.Identifier.Text),
            Iterable = iterable,
            Body = [.. LowerStatement(foreachStmt.Statement)]
        });
        return stmts;
    }

    private List<TfwrStmt> LowerForEachVariable(ForEachVariableStatementSyntax foreachStmt)
    {
        if (!TryFormatForeachVariablePattern(foreachStmt.Variable, parenthesize: false, out var pattern))
        {
            Error(foreachStmt.Variable, "Unsupported foreach deconstruction pattern.");
            return [new PassStmt()];
        }

        var stmts = new List<TfwrStmt>();
        var iterable = LowerExpressionHoisted(foreachStmt.Expression, stmts);
        stmts.Add(new ForStmt
        {
            Variable = pattern,
            Iterable = iterable,
            Body = [.. LowerStatement(foreachStmt.Statement)]
        });
        return stmts;
    }

    private static bool TryFormatForeachVariablePattern(ExpressionSyntax variable, bool parenthesize, out string pattern)
    {
        switch (variable)
        {
            case DeclarationExpressionSyntax decl:
                return TryFormatDesignation(decl.Designation, parenthesize, out pattern);
            case TupleExpressionSyntax tuple:
            {
                var parts = new List<string>();
                foreach (var arg in tuple.Arguments)
                {
                    if (!TryFormatForeachVariablePattern(arg.Expression, parenthesize: true, out var part))
                    {
                        pattern = "";
                        return false;
                    }

                    parts.Add(part);
                }

                var joined = string.Join(", ", parts);
                pattern = parenthesize ? $"({joined})" : joined;
                return parts.Count > 0;
            }
            case IdentifierNameSyntax id:
                pattern = NameUtil.SanitizeIdentifier(id.Identifier.Text);
                return true;
            default:
                pattern = "";
                return false;
        }
    }

    private static bool TryFormatDesignation(VariableDesignationSyntax designation, bool parenthesize, out string pattern)
    {
        switch (designation)
        {
            case SingleVariableDesignationSyntax single:
                pattern = NameUtil.SanitizeIdentifier(single.Identifier.Text);
                return true;
            case DiscardDesignationSyntax:
                pattern = "_";
                return true;
            case ParenthesizedVariableDesignationSyntax paren:
            {
                var parts = new List<string>();
                foreach (var child in paren.Variables)
                {
                    if (!TryFormatDesignation(child, parenthesize: true, out var part))
                    {
                        pattern = "";
                        return false;
                    }

                    parts.Add(part);
                }

                var joined = string.Join(", ", parts);
                pattern = parenthesize ? $"({joined})" : joined;
                return parts.Count > 0;
            }
            default:
                pattern = "";
                return false;
        }
    }

    private List<TfwrStmt> LowerReturn(ReturnStatementSyntax ret)
    {
        if (ret.Expression is null)
        {
            return [new ReturnStmt(null)];
        }

        var stmts = new List<TfwrStmt>();
        var value = LowerExpressionHoisted(ret.Expression, stmts);
        stmts.Add(new ReturnStmt(value));
        return stmts;
    }

    private List<TfwrStmt> LowerFor(ForStatementSyntax forStmt)
    {
        // Pattern: for (int i = 0; i < n; i++) => for i in range(n)
        if (TryLowerRangeFor(forStmt, out var rangeFor))
        {
            return rangeFor;
        }

        var stmts = new List<TfwrStmt>();
        if (forStmt.Declaration is { } decl)
        {
            foreach (var v in decl.Variables)
            {
                var name = NameUtil.SanitizeIdentifier(v.Identifier.Text);
                var value = v.Initializer?.Value is { } init
                    ? LowerExpressionHoisted(init, stmts)
                    : new LiteralExpr("None");
                stmts.Add(new AssignStmt(new NameExpr(name), value));
            }
        }

        foreach (var init in forStmt.Initializers)
        {
            stmts.AddRange(LowerExpressionStatement(init));
        }

        var whileBody = LowerStatement(forStmt.Statement).ToList();
        foreach (var incr in forStmt.Incrementors)
        {
            whileBody.AddRange(LowerExpressionStatement(incr));
        }

        var condPrelude = new List<TfwrStmt>();
        var cond = forStmt.Condition is null
            ? new LiteralExpr("True")
            : LowerExpressionHoisted(forStmt.Condition, condPrelude);

        if (condPrelude.Count == 0)
        {
            stmts.Add(new WhileStmt
            {
                Condition = cond,
                Body = whileBody
            });
        }
        else
        {
            var loopBody = new List<TfwrStmt>();
            loopBody.AddRange(condPrelude);
            loopBody.Add(new IfStmt
            {
                Condition = new UnaryExpr("not", cond),
                ThenBody = { new BreakStmt() }
            });
            loopBody.AddRange(whileBody);
            stmts.Add(new WhileStmt
            {
                Condition = new LiteralExpr("True"),
                Body = loopBody
            });
        }

        return stmts;
    }

    private bool TryLowerRangeFor(ForStatementSyntax forStmt, out List<TfwrStmt> result)
    {
        result = null!;
        if (forStmt.Declaration?.Variables.Count != 1)
        {
            return false;
        }

        var variable = forStmt.Declaration.Variables[0];
        if (variable.Initializer?.Value is not LiteralExpressionSyntax { Token.Value: 0 or 0L or 0d })
        {
            return false;
        }

        if (forStmt.Condition is not BinaryExpressionSyntax
            {
                RawKind: (int)SyntaxKind.LessThanExpression,
                Left: IdentifierNameSyntax leftId
            } cond
            || leftId.Identifier.Text != variable.Identifier.Text)
        {
            return false;
        }

        if (forStmt.Incrementors.Count != 1)
        {
            return false;
        }

        var incr = forStmt.Incrementors[0];
        var isInc = incr is PostfixUnaryExpressionSyntax post
                        && post.IsKind(SyntaxKind.PostIncrementExpression)
                        && post.Operand is IdentifierNameSyntax id1
                        && id1.Identifier.Text == variable.Identifier.Text
                    || incr is PrefixUnaryExpressionSyntax pre
                        && pre.IsKind(SyntaxKind.PreIncrementExpression)
                        && pre.Operand is IdentifierNameSyntax id2
                        && id2.Identifier.Text == variable.Identifier.Text
                    || incr is AssignmentExpressionSyntax
                    {
                        RawKind: (int)SyntaxKind.AddAssignmentExpression,
                        Left: IdentifierNameSyntax id3,
                        Right: LiteralExpressionSyntax { Token.Value: 1 or 1L or 1d }
                    } && id3.Identifier.Text == variable.Identifier.Text;

        if (!isInc)
        {
            return false;
        }

        var stmts = new List<TfwrStmt>();
        var bound = LowerExpressionHoisted(cond.Right, stmts);
        stmts.Add(new ForStmt
        {
            Variable = NameUtil.SanitizeIdentifier(variable.Identifier.Text),
            Iterable = new CallExpr(new NameExpr("range"), [bound]),
            Body = [.. LowerStatement(forStmt.Statement)]
        });
        result = stmts;
        return true;
    }

    private List<TfwrStmt> LowerSwitch(SwitchStatementSyntax sw)
    {
        var temp = NewTemp("switch");
        var stmts = new List<TfwrStmt>();
        var switchValue = LowerExpressionHoisted(sw.Expression, stmts);
        stmts.Add(new AssignStmt(new NameExpr(temp), switchValue));

        IfStmt? root = null;
        IfStmt? current = null;
        List<TfwrStmt>? defaultBody = null;

        foreach (var section in sw.Sections)
        {
            var body = section.Statements.SelectMany(LowerStatement)
                .Where(s => s is not BreakStmt)
                .ToList();

            var labels = section.Labels.ToList();
            if (labels.Any(l => l.IsKind(SyntaxKind.DefaultSwitchLabel)))
            {
                defaultBody = body;
                continue;
            }

            TfwrExpr? cond = null;
            foreach (var label in labels.OfType<CaseSwitchLabelSyntax>())
            {
                // Case labels are constants; no statement hoist expected.
                var part = new BinaryExpr(new NameExpr(temp), "==", LowerExpression(label.Value));
                cond = cond is null ? part : new BinaryExpr(cond, "or", part);
            }

            foreach (var label in labels.OfType<CasePatternSwitchLabelSyntax>())
            {
                Error(label, "Pattern switch cases are not supported in v1.");
            }

            if (cond is null)
            {
                continue;
            }

            var ifStmt = new IfStmt { Condition = cond };
            ifStmt.ThenBody.AddRange(body);
            if (root is null)
            {
                root = ifStmt;
                current = ifStmt;
            }
            else
            {
                current!.Elifs.Add((cond, body));
            }
        }

        if (root is null)
        {
            if (defaultBody is not null)
            {
                stmts.AddRange(defaultBody);
            }

            return stmts;
        }

        if (defaultBody is not null)
        {
            root.ElseBody = defaultBody;
        }

        stmts.Add(root);
        return stmts;
    }

    private TfwrExpr LowerExpression(ExpressionSyntax expr)
    {
        switch (expr)
        {
            case IdentifierNameSyntax id:
                return LowerIdentifier(id);
            case MemberAccessExpressionSyntax member:
                return LowerMemberAccess(member);
            case LiteralExpressionSyntax lit:
                return LowerLiteral(lit);
            case BinaryExpressionSyntax bin:
                return LowerBinary(bin);
            case PrefixUnaryExpressionSyntax unary:
                return LowerPrefix(unary);
            case PostfixUnaryExpressionSyntax { RawKind: (int)SyntaxKind.SuppressNullableWarningExpression } bang:
                // C# null-forgiving `expr!` — compile-time only; strip for TFWR.
                return LowerExpression(bang.Operand);
            case ParenthesizedExpressionSyntax paren:
                return LowerExpression(paren.Expression);
            case InvocationExpressionSyntax call:
                return LowerInvocation(call);
            case ObjectCreationExpressionSyntax create:
                return LowerObjectCreation(create);
            case ImplicitObjectCreationExpressionSyntax implicitCreate:
                return LowerImplicitObjectCreation(implicitCreate);
            case ElementAccessExpressionSyntax index:
                return LowerElementAccess(index);
            case ImplicitElementAccessSyntax implicitIndex:
                Error(implicitIndex, "Implicit element access is only supported inside collection initializers.");
                return new LiteralExpr("None");
            case ThisExpressionSyntax:
                return new NameExpr("self");
            case BaseExpressionSyntax:
                Error(expr, "base is not fully supported; use explicit calls.");
                return new NameExpr("self");
            case ConditionalExpressionSyntax ternary:
                return LowerTernary(ternary);
            case InterpolatedStringExpressionSyntax interp:
                return LowerInterpolatedString(interp);
            case TupleExpressionSyntax tuple:
                return new TupleExpr([.. tuple.Arguments.Select(a => LowerExpression(a.Expression))]);
            case CollectionExpressionSyntax collection:
                return LowerCollectionExpression(collection);
            case IsPatternExpressionSyntax isPattern:
                return LowerIsPattern(isPattern);
            case CastExpressionSyntax cast:
                return LowerExpression(cast.Expression);
            case DefaultExpressionSyntax:
                return new LiteralExpr("None");
            case SizeOfExpressionSyntax sizeOf:
                Error(sizeOf, "sizeof is not supported.");
                return new LiteralExpr("0");
            case TypeOfExpressionSyntax typeOf:
                Error(typeOf, "typeof is not supported.");
                return new LiteralExpr("None");
            case AnonymousObjectCreationExpressionSyntax anon:
                Error(anon, "Anonymous objects are not supported.");
                return new DictExpr([]);
            case LambdaExpressionSyntax lambda:
                Error(lambda, "Lambdas are not supported in TFWR.");
                return new NameExpr("None");
            case AwaitExpressionSyntax awaitExpr:
                Error(awaitExpr, "async/await is not supported.");
                return new LiteralExpr("None");
            case SwitchExpressionSyntax switchExpr:
                return LowerSwitchExpression(switchExpr);
            case AssignmentExpressionSyntax:
                Error(expr, "Assignment expressions are not supported in this position.");
                return new LiteralExpr("None");
            default:
                Error(expr, $"Expression '{expr.Kind()}' is not supported.");
                return new LiteralExpr("None");
        }
    }

    private TfwrExpr LowerIdentifier(IdentifierNameSyntax id)
    {
        var symbol = _model.GetSymbolInfo(id).Symbol;
        if (symbol is IParameterSymbol or ILocalSymbol)
        {
            return new NameExpr(NameUtil.SanitizeIdentifier(id.Identifier.Text));
        }

        if (symbol is IFieldSymbol field)
        {
            if (field.IsStatic)
            {
                EnsureImportForType(field.ContainingType);
                return Qualify(field.ContainingType, new NameExpr($"{field.ContainingType.Name}_{field.Name}"));
            }

            return new IndexExpr(new NameExpr("self"), new LiteralExpr($"\"{field.Name}\""));
        }

        if (symbol is IPropertySymbol prop)
        {
            if (IsGameApi(prop.ContainingType))
            {
                return LowerGameEnumMember(prop);
            }

            if (prop.IsStatic)
            {
                EnsureImportForType(prop.ContainingType);
                return Qualify(prop.ContainingType, new CallExpr(new NameExpr($"{prop.ContainingType.Name}_get_{prop.Name}"), []));
            }

            if (IsAutoProperty(prop))
            {
                return new IndexExpr(new NameExpr("self"), new LiteralExpr($"\"{prop.Name}\""));
            }

            return new CallExpr(new NameExpr($"{prop.ContainingType.Name}_get_{prop.Name}"), [new NameExpr("self")]);
        }

        if (symbol is IMethodSymbol method)
        {
            EnsureImportForType(method.ContainingType);
            return Qualify(method.ContainingType, new NameExpr(GetFunctionName(method.ContainingType, method)));
        }

        if (symbol is INamedTypeSymbol type)
        {
            return new NameExpr(type.Name);
        }

        return new NameExpr(NameUtil.SanitizeIdentifier(id.Identifier.Text));
    }

    private TfwrExpr LowerMemberAccess(MemberAccessExpressionSyntax member)
    {
        var symbol = _model.GetSymbolInfo(member).Symbol;

        if (symbol is IFieldSymbol field)
        {
            if (field.ContainingType?.TypeKind == TypeKind.Enum && IsGameApi(field.ContainingType))
            {
                return LowerGameEnumMember(field);
            }

            if (IsGameApi(field.ContainingType) || IsGameApi(field.Type as INamedTypeSymbol))
            {
                // Direction.North etc.
            }

            if (field.IsStatic)
            {
                if (IsGameApi(field.ContainingType))
                {
                    return LowerGameEnumMember(field);
                }

                EnsureImportForType(field.ContainingType);
                return Qualify(field.ContainingType, new NameExpr($"{field.ContainingType!.Name}_{field.Name}"));
            }

            return new IndexExpr(LowerExpression(member.Expression), new LiteralExpr($"\"{field.Name}\""));
        }

        if (symbol is IPropertySymbol prop)
        {
            if (IsGameApi(prop.ContainingType))
            {
                return LowerGameEnumMember(prop);
            }

            if (IsNullableType(prop.ContainingType))
            {
                var receiver = LowerExpression(member.Expression);
                return prop.Name switch
                {
                    // T? is just a value or None in TFWR.
                    "HasValue" => new BinaryExpr(receiver, "!=", new LiteralExpr("None")),
                    "Value" => receiver,
                    _ => new AttributeExpr(receiver, prop.Name)
                };
            }

            if (prop.ContainingType?.SpecialType == SpecialType.System_String && prop.Name == "Length")
            {
                return new CallExpr(new NameExpr("len"), [LowerExpression(member.Expression)]);
            }

            if (prop.ContainingType is not null && IsCollectionType(prop.ContainingType) && prop.Name == "Count")
            {
                return new CallExpr(new NameExpr("len"), [LowerExpression(member.Expression)]);
            }

            if (prop.IsStatic)
            {
                EnsureImportForType(prop.ContainingType);
                return Qualify(prop.ContainingType, new CallExpr(new NameExpr($"{prop.ContainingType!.Name}_get_{prop.Name}"), []));
            }

            if (IsInstanceDictProperty(prop))
            {
                return new IndexExpr(LowerExpression(member.Expression), new LiteralExpr($"\"{prop.Name}\""));
            }

            var getter = new NameExpr($"{prop.ContainingType!.Name}_get_{prop.Name}");
            EnsureImportForType(prop.ContainingType);
            return new CallExpr(Qualify(prop.ContainingType, getter), [LowerExpression(member.Expression)]);
        }

        if (symbol is IMethodSymbol method)
        {
            // method group
            EnsureImportForType(method.ContainingType);
            var name = GetFunctionName(method.ContainingType, method);
            return Qualify(method.ContainingType, new NameExpr(name));
        }

        // Fallback: enum-like Game.X.Y already handled; attribute style
        if (member.Expression is IdentifierNameSyntax { Identifier.Text: var typeName }
            && GameTypeNames.Contains(typeName))
        {
            if (typeName == "Direction")
            {
                return new NameExpr(member.Name.Identifier.Text);
            }

            return new AttributeExpr(new NameExpr(typeName), member.Name.Identifier.Text);
        }

        return new AttributeExpr(LowerExpression(member.Expression), member.Name.Identifier.Text);
    }

    private static TfwrExpr LowerGameEnumMember(ISymbol symbol)
    {
        var containing = symbol.ContainingType?.Name;
        if (containing == "Direction")
        {
            // TFWR exposes directions as globals: North, East, South, West
            return new NameExpr(symbol.Name);
        }

        if (containing is "Entities" or "Items" or "Grounds" or "Hats" or "Unlocks" or "Leaderboards")
        {
            return new AttributeExpr(new NameExpr(containing), symbol.Name);
        }

        return new NameExpr(symbol.Name);
    }

    private static LiteralExpr LowerLiteral(LiteralExpressionSyntax lit)
    {
        if (lit.IsKind(SyntaxKind.TrueLiteralExpression))
        {
            return new LiteralExpr("True");
        }

        if (lit.IsKind(SyntaxKind.FalseLiteralExpression))
        {
            return new LiteralExpr("False");
        }

        if (lit.IsKind(SyntaxKind.NullLiteralExpression))
        {
            return new LiteralExpr("None");
        }

        if (lit.IsKind(SyntaxKind.StringLiteralExpression) || lit.IsKind(SyntaxKind.CharacterLiteralExpression))
        {
            var value = lit.Token.ValueText;
            return new LiteralExpr("\"" + EscapeString(value) + "\"");
        }

        if (lit.Token.Value is float or double or decimal)
        {
            return new LiteralExpr(Convert.ToString(lit.Token.Value, System.Globalization.CultureInfo.InvariantCulture)!);
        }

        if (lit.Token.Value is int or long or uint or ulong or short or ushort or byte or sbyte)
        {
            return new LiteralExpr(Convert.ToString(lit.Token.Value, System.Globalization.CultureInfo.InvariantCulture)!);
        }

        return new LiteralExpr(lit.Token.Text);
    }

    private static string EscapeString(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);

    private TfwrExpr LowerBinary(BinaryExpressionSyntax bin)
    {
        if (bin.IsKind(SyntaxKind.CoalesceExpression))
        {
            Error(bin, "Null-coalescing ?? is not supported; use explicit null checks.");
            return LowerExpression(bin.Left);
        }

        if (bin.IsKind(SyntaxKind.AsExpression) || bin.IsKind(SyntaxKind.IsExpression))
        {
            if (bin.IsKind(SyntaxKind.IsExpression) && bin.Right is LiteralExpressionSyntax { RawKind: (int)SyntaxKind.NullLiteralExpression })
            {
                // x is null → x == None — but C# uses `is null` pattern more often
            }

            Error(bin, "is/as type checks are limited. Use == None for null checks.");
            return new BinaryExpr(LowerExpression(bin.Left), "==", new LiteralExpr("None"));
        }

        var op = bin.Kind() switch
        {
            SyntaxKind.AddExpression => "+",
            SyntaxKind.SubtractExpression => "-",
            SyntaxKind.MultiplyExpression => "*",
            SyntaxKind.DivideExpression => IsIntegerDivision(bin) ? "//" : "/",
            SyntaxKind.ModuloExpression => "%",
            SyntaxKind.EqualsExpression => "==",
            SyntaxKind.NotEqualsExpression => "!=",
            SyntaxKind.LessThanExpression => "<",
            SyntaxKind.LessThanOrEqualExpression => "<=",
            SyntaxKind.GreaterThanExpression => ">",
            SyntaxKind.GreaterThanOrEqualExpression => ">=",
            SyntaxKind.LogicalAndExpression => "and",
            SyntaxKind.LogicalOrExpression => "or",
            SyntaxKind.BitwiseAndExpression => "and", // approximate
            SyntaxKind.BitwiseOrExpression => "or",
            SyntaxKind.ExclusiveOrExpression => "!=", // bool xor approx
            _ => null
        };

        if (op is null)
        {
            Error(bin, $"Operator '{bin.OperatorToken.Text}' is not supported.");
            return new LiteralExpr("None");
        }

        // null comparisons
        if (bin.Right.IsKind(SyntaxKind.NullLiteralExpression) || bin.Left.IsKind(SyntaxKind.NullLiteralExpression))
        {
            // already == / !=
        }

        return new BinaryExpr(LowerExpression(bin.Left), op, LowerExpression(bin.Right));
    }

    private bool IsIntegerDivision(BinaryExpressionSyntax bin)
    {
        var type = _model.GetTypeInfo(bin).ConvertedType ?? _model.GetTypeInfo(bin).Type;
        if (type is null)
        {
            return false;
        }

        return type.SpecialType is SpecialType.System_Int32 or SpecialType.System_Int64
            or SpecialType.System_UInt32 or SpecialType.System_UInt64
            or SpecialType.System_Int16 or SpecialType.System_Byte
            or SpecialType.System_SByte or SpecialType.System_UInt16;
    }

    private TfwrExpr LowerPrefix(PrefixUnaryExpressionSyntax unary)
    {
        if (unary.IsKind(SyntaxKind.LogicalNotExpression))
        {
            return new UnaryExpr("not", LowerExpression(unary.Operand));
        }

        if (unary.IsKind(SyntaxKind.UnaryMinusExpression))
        {
            return new UnaryExpr("-", LowerExpression(unary.Operand));
        }

        if (unary.IsKind(SyntaxKind.UnaryPlusExpression))
        {
            return LowerExpression(unary.Operand);
        }

        Error(unary, $"Unary operator '{unary.OperatorToken.Text}' is not supported.");
        return LowerExpression(unary.Operand);
    }

    private TfwrExpr LowerInvocation(InvocationExpressionSyntax call)
    {
        var symbol = _model.GetSymbolInfo(call).Symbol as IMethodSymbol
                     ?? _model.GetSymbolInfo(call).CandidateSymbols.OfType<IMethodSymbol>().FirstOrDefault();

        var args = call.ArgumentList.Arguments.Select(a => LowerExpression(a.Expression)).ToList();

        if (symbol is not null && IsGameApi(symbol.ContainingType) && symbol.ContainingType?.Name == "Game")
        {
            var snake = NameUtil.PascalToSnake(symbol.Name);
            return new CallExpr(new NameExpr(snake), args);
        }

        // Collection methods
        if (symbol is not null && call.Expression is MemberAccessExpressionSyntax member)
        {
            var receiver = LowerExpression(member.Expression);
            if (TryLowerCollectionMethod(symbol, receiver, args, out var collectionCall))
            {
                return collectionCall;
            }

            if (IsNullableType(symbol.ContainingType))
            {
                // GetValueOrDefault() → value-or-None; with arg → value if present else arg (via != None ? receiver : arg)
                if (symbol.Name == "GetValueOrDefault")
                {
                    if (args.Count == 0)
                    {
                        return receiver;
                    }

                    if (_hoist is null)
                    {
                        Error(call, "Nullable.GetValueOrDefault(default) requires a statement context.");
                        return receiver;
                    }

                    var temp = NewTemp("nval");
                    var ifStmt = new IfStmt
                    {
                        Condition = new BinaryExpr(receiver, "!=", new LiteralExpr("None"))
                    };
                    ifStmt.ThenBody.Add(new AssignStmt(new NameExpr(temp), receiver));
                    ifStmt.ElseBody =
                    [
                        new AssignStmt(new NameExpr(temp), args[0])
                    ];
                    _hoist.Add(ifStmt);
                    return new NameExpr(temp);
                }
            }

            if (symbol.ContainingType?.SpecialType == SpecialType.System_String && symbol.Name == "Format")
            {
                Error(call, "string.Format is not supported; use interpolation.");
                return new LiteralExpr("\"\"");
            }
        }

        if (symbol is not null)
        {
            EnsureImportForType(symbol.ContainingType);
            if (symbol.MethodKind == MethodKind.Constructor)
            {
                var ctorName = Qualify(symbol.ContainingType, new NameExpr($"{symbol.ContainingType!.Name}_new"));
                return new CallExpr(ctorName, args);
            }

            var fnName = GetFunctionName(symbol.ContainingType!, symbol);
            var callee = Qualify(symbol.ContainingType, new NameExpr(fnName));
            if (!symbol.IsStatic && call.Expression is MemberAccessExpressionSyntax instanceMember)
            {
                var instanceArgs = new List<TfwrExpr> { LowerExpression(instanceMember.Expression) };
                instanceArgs.AddRange(args);
                return new CallExpr(callee, instanceArgs);
            }

            if (!symbol.IsStatic && call.Expression is IdentifierNameSyntax)
            {
                var instanceArgs = new List<TfwrExpr> { new NameExpr("self") };
                instanceArgs.AddRange(args);
                return new CallExpr(callee, instanceArgs);
            }

            return new CallExpr(callee, args);
        }

        // Fallback
        if (call.Expression is MemberAccessExpressionSyntax ma)
        {
            return new CallExpr(new AttributeExpr(LowerExpression(ma.Expression), ma.Name.Identifier.Text), args);
        }

        return new CallExpr(LowerExpression(call.Expression), args);
    }

    private bool TryLowerCollectionMethod(IMethodSymbol symbol, TfwrExpr receiver, List<TfwrExpr> args, out TfwrExpr result)
    {
        result = null!;
        var type = symbol.ContainingType;
        if (type is null || !IsCollectionType(type) && type.Name != "HashSet")
        {
            // also check constructed types List<T>
            if (type?.OriginalDefinition is not { } orig || !IsCollectionType(orig) && orig.Name != "HashSet")
            {
                if (type?.AllInterfaces.Any(i => i.Name is "IList" or "ICollection" or "IDictionary") != true
                    && type?.Name is not ("List" or "Dictionary" or "HashSet"))
                {
                    return false;
                }
            }
        }

        switch (symbol.Name)
        {
            case "Add" when args.Count == 1 && (type!.Name == "List" || type.Name.Contains("List", StringComparison.Ordinal)):
                result = new CallExpr(new AttributeExpr(receiver, "append"), args);
                return true;
            case "Add" when args.Count == 1:
                result = new CallExpr(new AttributeExpr(receiver, "add"), args);
                return true;
            case "Add" when args.Count == 2 && type!.Name.Contains("Dictionary", StringComparison.Ordinal):
                Error(null, "Dictionary.Add should be written as dict[key] = value.");
                result = new LiteralExpr("None");
                return true;
            case "Remove":
                result = new CallExpr(new AttributeExpr(receiver, "remove"), args);
                return true;
            case "Contains" or "ContainsKey":
                result = new BinaryExpr(args[0], "in", receiver);
                return true;
            case "Clear":
                Error(null, "Clear() is not available in TFWR collections; reassign a new empty collection.");
                result = new LiteralExpr("None");
                return true;
            case "Insert":
                result = new CallExpr(new AttributeExpr(receiver, "insert"), args);
                return true;
            case "Pop" or "Dequeue":
                result = new CallExpr(new AttributeExpr(receiver, "pop"), args);
                return true;
            default:
                if (symbol.Name is "ToList" or "ToArray" or "Where" or "Select" or "Any" or "All" or "First" or "Count")
                {
                    Error(null, $"LINQ/collection method '{symbol.Name}' is not supported.");
                    result = new LiteralExpr("None");
                    return true;
                }

                return false;
        }
    }

    private TfwrExpr LowerObjectCreation(ObjectCreationExpressionSyntax create)
    {
        var type = _model.GetTypeInfo(create).Type as INamedTypeSymbol
                   ?? _model.GetSymbolInfo(create.Type).Symbol as INamedTypeSymbol;
        var args = create.ArgumentList?.Arguments.Select(a => LowerExpression(a.Expression)).ToList()
                   ?? [];

        if (type is not null && IsCollectionType(type))
        {
            return LowerNewCollection(type, create.Initializer);
        }

        if (type is not null)
        {
            EnsureImportForType(type);
            return new CallExpr(Qualify(type, new NameExpr($"{type.Name}_new")), args);
        }

        Error(create, "Unable to resolve constructed type.");
        return new LiteralExpr("None");
    }

    private TfwrExpr LowerImplicitObjectCreation(ImplicitObjectCreationExpressionSyntax create)
    {
        var type = _model.GetTypeInfo(create).Type as INamedTypeSymbol;
        var args = create.ArgumentList.Arguments.Select(a => LowerExpression(a.Expression)).ToList();
        if (type is not null && IsCollectionType(type))
        {
            return LowerNewCollection(type, create.Initializer);
        }

        if (type is not null)
        {
            EnsureImportForType(type);
            return new CallExpr(Qualify(type, new NameExpr($"{type.Name}_new")), args);
        }

        Error(create, "Unable to resolve constructed type.");
        return new LiteralExpr("None");
    }

    private TfwrExpr LowerNewCollection(INamedTypeSymbol type, InitializerExpressionSyntax? initializer)
    {
        var def = type.OriginalDefinition.SpecialType != SpecialType.None
            ? type
            : type.OriginalDefinition;

        if (def.Name == "List" || def.AllInterfaces.Any(i => i.Name == "IList"))
        {
            var elements = initializer?.Expressions.Select(LowerExpression).ToList() ?? [];
            return new ListExpr(elements);
        }

        if (def.Name == "Dictionary" || def.AllInterfaces.Any(i => i.Name == "IDictionary"))
        {
            var entries = new List<(TfwrExpr, TfwrExpr)>();
            if (initializer is not null)
            {
                foreach (var expr in initializer.Expressions)
                {
                    if (expr is InitializerExpressionSyntax { Expressions.Count: 2 } complex)
                    {
                        entries.Add((LowerExpression(complex.Expressions[0]), LowerExpression(complex.Expressions[1])));
                    }
                    else if (expr is AssignmentExpressionSyntax assign)
                    {
                        var keyExpr = assign.Left switch
                        {
                            ImplicitElementAccessSyntax implicitAccess
                                when implicitAccess.ArgumentList.Arguments.Count == 1
                                => LowerExpression(implicitAccess.ArgumentList.Arguments[0].Expression),
                            ElementAccessExpressionSyntax elementAccess
                                when elementAccess.ArgumentList.Arguments.Count == 1
                                => LowerExpression(elementAccess.ArgumentList.Arguments[0].Expression),
                            _ => LowerExpression(assign.Left)
                        };
                        entries.Add((keyExpr, LowerExpression(assign.Right)));
                    }
                }
            }

            return new DictExpr(entries);
        }

        if (def.Name == "HashSet")
        {
            var elements = initializer?.Expressions.Select(LowerExpression).ToList() ?? [];
            return elements.Count == 0 ? new SetExpr([]) : new SetExpr(elements);
        }

        Error(null, $"Collection type '{type.Name}' is not supported.");
        return new ListExpr([]);
    }

    private TfwrExpr LowerCollectionExpression(CollectionExpressionSyntax collection)
    {
        var type = _model.GetTypeInfo(collection).ConvertedType as INamedTypeSymbol
                   ?? _model.GetTypeInfo(collection).Type as INamedTypeSymbol;
        var elements = collection.Elements
            .OfType<ExpressionElementSyntax>()
            .Select(e => LowerExpression(e.Expression))
            .ToList();

        if (type is not null && (type.Name == "HashSet" || type.OriginalDefinition.Name == "HashSet"))
        {
            return new SetExpr(elements);
        }

        if (type is not null && (type.Name == "Dictionary" || type.OriginalDefinition.Name == "Dictionary"))
        {
            Error(collection, "Dictionary collection expressions are not supported in v1.");
            return new DictExpr([]);
        }

        return new ListExpr(elements);
    }

    private TfwrExpr LowerElementAccess(ElementAccessExpressionSyntax index)
    {
        if (index.ArgumentList.Arguments.Count != 1)
        {
            Error(index, "Multi-dimensional indexers are not supported.");
            return new LiteralExpr("None");
        }

        return new IndexExpr(LowerExpression(index.Expression), LowerExpression(index.ArgumentList.Arguments[0].Expression));
    }

    private TfwrExpr LowerInterpolatedString(InterpolatedStringExpressionSyntax interp)
    {
        TfwrExpr? acc = null;
        foreach (var content in interp.Contents)
        {
            TfwrExpr part = content switch
            {
                InterpolatedStringTextSyntax text => new LiteralExpr("\"" + EscapeString(text.TextToken.ValueText) + "\""),
                InterpolationSyntax interpolation => new CallExpr(new NameExpr("str"), [LowerExpression(interpolation.Expression)]),
                _ => new LiteralExpr("\"\"")
            };
            acc = acc is null ? part : new BinaryExpr(acc, "+", part);
        }

        return acc ?? new LiteralExpr("\"\"");
    }

    private TfwrExpr LowerIsPattern(IsPatternExpressionSyntax isPattern)
    {
        if (isPattern.Pattern is ConstantPatternSyntax { Expression: LiteralExpressionSyntax lit }
            && lit.IsKind(SyntaxKind.NullLiteralExpression))
        {
            return new BinaryExpr(LowerExpression(isPattern.Expression), "==", new LiteralExpr("None"));
        }

        if (isPattern.Pattern is UnaryPatternSyntax
            {
                OperatorToken.Text: "not",
                Pattern: ConstantPatternSyntax { Expression: LiteralExpressionSyntax lit2 }
            }
            && lit2.IsKind(SyntaxKind.NullLiteralExpression))
        {
            return new BinaryExpr(LowerExpression(isPattern.Expression), "!=", new LiteralExpr("None"));
        }

        Error(isPattern, "Only 'is null' / 'is not null' patterns are supported.");
        return new LiteralExpr("False");
    }

    private TfwrExpr LowerTernary(ConditionalExpressionSyntax ternary)
    {
        if (_hoist is null)
        {
            Error(ternary, "Ternary ?: requires a statement context (not parameter defaults).");
            return new LiteralExpr("None");
        }

        // Condition side-effects / nested ternaries hoist before the if.
        var cond = LowerExpression(ternary.Condition);
        var temp = NewTemp("tern");

        var thenBody = new List<TfwrStmt>();
        var elseBody = new List<TfwrStmt>();
        var prev = _hoist;

        _hoist = thenBody;
        var whenTrue = LowerExpression(ternary.WhenTrue);
        thenBody.Add(new AssignStmt(new NameExpr(temp), whenTrue));

        _hoist = elseBody;
        var whenFalse = LowerExpression(ternary.WhenFalse);
        elseBody.Add(new AssignStmt(new NameExpr(temp), whenFalse));

        _hoist = prev;
        var ifStmt = new IfStmt { Condition = cond };
        ifStmt.ThenBody.AddRange(thenBody);
        ifStmt.ElseBody = elseBody;
        _hoist.Add(ifStmt);

        return new NameExpr(temp);
    }

    private TfwrExpr LowerExpressionHoisted(ExpressionSyntax expr, List<TfwrStmt> stmts)
    {
        var prev = _hoist;
        _hoist = stmts;
        try
        {
            return LowerExpression(expr);
        }
        finally
        {
            _hoist = prev;
        }
    }

    private TfwrExpr LowerSwitchExpression(SwitchExpressionSyntax switchExpr)
    {
        Error(switchExpr, "Switch expressions are lowered only in limited form; prefer switch statements.");
        // crude: first arm
        if (switchExpr.Arms.Count > 0)
        {
            return LowerExpression(switchExpr.Arms[0].Expression);
        }

        return new LiteralExpr("None");
    }

    private TfwrExpr Qualify(INamedTypeSymbol? type, TfwrExpr nameExpr)
    {
        if (type is null)
        {
            return nameExpr;
        }

        if (_typeModuleMap.TryGetValue(type, out var module) &&
            !string.Equals(module, _moduleName, StringComparison.Ordinal))
        {
            if (nameExpr is NameExpr n)
            {
                return new AttributeExpr(new NameExpr(module), n.Name);
            }

            if (nameExpr is CallExpr { Callee: NameExpr cn } call)
            {
                return new CallExpr(new AttributeExpr(new NameExpr(module), cn.Name), call.Arguments);
            }
        }

        return nameExpr;
    }

    private void EnsureImportForType(INamedTypeSymbol? type)
    {
        if (type is null || IsGameApi(type) || IsFrameworkType(type))
        {
            return;
        }

        if (_typeModuleMap.TryGetValue(type, out var module) &&
            !string.Equals(module, _moduleName, StringComparison.Ordinal))
        {
            _imports.Add(module);
        }
    }

    private static bool IsGameApi(INamedTypeSymbol? type)
    {
        if (type is null)
        {
            return false;
        }

        return type.ContainingNamespace?.ToDisplayString() == "TFWR.Api"
               || GameTypeNames.Contains(type.Name);
    }

    private static bool IsFrameworkType(INamedTypeSymbol type)
    {
        var ns = type.ContainingNamespace?.ToDisplayString() ?? "";
        return ns.StartsWith("System", StringComparison.Ordinal);
    }

    private static bool IsNullableType(INamedTypeSymbol? type) =>
        type?.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;

    private static bool IsCollectionType(INamedTypeSymbol type)
    {
        var name = type.OriginalDefinition.Name;
        return name is "List" or "Dictionary" or "HashSet" or "IList" or "IDictionary" or "ICollection"
               || type.AllInterfaces.Any(i => i.Name is "IList" or "IDictionary");
    }

    private string NewTemp(string prefix) => $"_{prefix}{++_tempCounter}";

    private void RejectUnsupported(SyntaxNode root)
    {
        foreach (var node in root.DescendantNodes())
        {
            switch (node)
            {
                case TryStatementSyntax n:
                    Error(n, "try/catch/finally is not supported.");
                    break;
                case UnsafeStatementSyntax n:
                    Error(n, "unsafe code is not supported.");
                    break;
                case YieldStatementSyntax n:
                    Error(n, "yield is not supported.");
                    break;
                case QueryExpressionSyntax n:
                    Error(n, "LINQ query syntax is not supported.");
                    break;
                case AnonymousMethodExpressionSyntax n:
                    Error(n, "Anonymous methods are not supported.");
                    break;
            }
        }
    }

    private void Error(SyntaxNode? node, string message)
    {
        if (node is null)
        {
            _diagnostics.Add(new CompileDiagnostic
            {
                Severity = DiagnosticSeverity.Error,
                Message = message
            });
            return;
        }

        var span = node.GetLocation().GetLineSpan();
        _diagnostics.Add(new CompileDiagnostic
        {
            Severity = DiagnosticSeverity.Error,
            Message = message,
            FilePath = span.Path,
            Line = span.StartLinePosition.Line + 1,
            Column = span.StartLinePosition.Character + 1
        });
    }
}
