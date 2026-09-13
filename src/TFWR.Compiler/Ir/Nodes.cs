namespace TFWR.Compiler.Ir;

/// <summary>
/// Root IR container for a compile unit made of one or more modules.
/// </summary>
public sealed class TfwrProgram
{
    /// <summary>Modules produced by lowering.</summary>
    public List<TfwrModule> Modules { get; } = [];
}

/// <summary>
/// A single TFWR Python module corresponding to one input C# file.
/// </summary>
public sealed class TfwrModule
{
    /// <summary>Python module name derived from the source file name.</summary>
    public required string ModuleName { get; init; }

    /// <summary>Relative output path for the emitted <c>.py</c> file.</summary>
    public required string RelativeOutputPath { get; init; }

    /// <summary>Imported sibling module names.</summary>
    public List<string> Imports { get; } = [];

    /// <summary>Functions defined in this module.</summary>
    public List<TfwrFunction> Functions { get; } = [];

    /// <summary>
    /// Statements emitted at module scope (used with top-level entry mode).
    /// </summary>
    public List<TfwrStmt> TopLevelStatements { get; } = [];

    /// <summary>
    /// Entry function name to invoke under <c>if __name__ == "__main__"</c>, if any.
    /// </summary>
    public string? EntryFunctionName { get; set; }
}

/// <summary>
/// A Python <c>def</c> function in the IR.
/// </summary>
public sealed class TfwrFunction
{
    /// <summary>Function name.</summary>
    public required string Name { get; init; }

    /// <summary>Formal parameters.</summary>
    public List<TfwrParameter> Parameters { get; } = [];

    /// <summary>Function body statements.</summary>
    public List<TfwrStmt> Body { get; } = [];
}

/// <summary>
/// A function parameter, optionally with a default value expression.
/// </summary>
public sealed class TfwrParameter
{
    /// <summary>Parameter name.</summary>
    public required string Name { get; init; }

    /// <summary>Default value expression, or <see langword="null"/> if required.</summary>
    public TfwrExpr? DefaultValue { get; init; }
}

/// <summary>
/// Base type for IR statements.
/// </summary>
public abstract class TfwrStmt;

/// <summary>
/// Expression used as a statement.
/// </summary>
/// <param name="expression">Expression to evaluate for side effects.</param>
public sealed class ExprStmt(TfwrExpr expression) : TfwrStmt
{
    /// <summary>Expression to evaluate.</summary>
    public TfwrExpr Expression { get; } = expression;
}

/// <summary>
/// Simple assignment: <c>target = value</c>.
/// </summary>
/// <param name="target">Assignment target.</param>
/// <param name="value">Assigned value.</param>
public sealed class AssignStmt(TfwrExpr target, TfwrExpr value) : TfwrStmt
{
    /// <summary>Assignment target.</summary>
    public TfwrExpr Target { get; } = target;

    /// <summary>Assigned value.</summary>
    public TfwrExpr Value { get; } = value;
}

/// <summary>
/// Augmented assignment: <c>target op= value</c>.
/// </summary>
/// <param name="target">Assignment target.</param>
/// <param name="op">Operator without <c>=</c> (for example <c>+</c>).</param>
/// <param name="value">Right-hand side value.</param>
public sealed class AugAssignStmt(TfwrExpr target, string op, TfwrExpr value) : TfwrStmt
{
    /// <summary>Assignment target.</summary>
    public TfwrExpr Target { get; } = target;

    /// <summary>Operator without the trailing <c>=</c>.</summary>
    public string Op { get; } = op;

    /// <summary>Right-hand side value.</summary>
    public TfwrExpr Value { get; } = value;
}

/// <summary>
/// Conditional statement with optional <c>elif</c>/<c>else</c> branches.
/// </summary>
public sealed class IfStmt : TfwrStmt
{
    /// <summary>Primary <c>if</c> condition.</summary>
    public required TfwrExpr Condition { get; init; }

    /// <summary>Body executed when <see cref="Condition"/> is true.</summary>
    public List<TfwrStmt> ThenBody { get; } = [];

    /// <summary>Additional <c>elif</c> branches as (condition, body) pairs.</summary>
    public List<(TfwrExpr Condition, List<TfwrStmt> Body)> Elifs { get; } = [];

    /// <summary>Optional <c>else</c> body.</summary>
    public List<TfwrStmt>? ElseBody { get; set; }
}

/// <summary>
/// <c>while</c> loop.
/// </summary>
public sealed class WhileStmt : TfwrStmt
{
    /// <summary>Loop condition.</summary>
    public required TfwrExpr Condition { get; init; }

    /// <summary>Loop body.</summary>
    public List<TfwrStmt> Body { get; init; } = [];
}

/// <summary>
/// <c>for</c> loop over an iterable.
/// </summary>
public sealed class ForStmt : TfwrStmt
{
    /// <summary>Loop variable name or deconstruction pattern.</summary>
    public required string Variable { get; init; }

    /// <summary>Iterable expression.</summary>
    public required TfwrExpr Iterable { get; init; }

    /// <summary>Loop body.</summary>
    public List<TfwrStmt> Body { get; init; } = [];
}

/// <summary>
/// <c>return</c> statement.
/// </summary>
/// <param name="value">Returned expression, or <see langword="null"/> for bare <c>return</c>.</param>
public sealed class ReturnStmt(TfwrExpr? value) : TfwrStmt
{
    /// <summary>Returned value, or <see langword="null"/> for bare <c>return</c>.</summary>
    public TfwrExpr? Value { get; } = value;
}

/// <summary>
/// <c>break</c> statement.
/// </summary>
public sealed class BreakStmt : TfwrStmt;

/// <summary>
/// <c>continue</c> statement.
/// </summary>
public sealed class ContinueStmt : TfwrStmt;

/// <summary>
/// <c>pass</c> statement.
/// </summary>
public sealed class PassStmt : TfwrStmt;

/// <summary>
/// <c>global</c> declaration.
/// </summary>
/// <param name="names">Names declared global.</param>
public sealed class GlobalStmt(IReadOnlyList<string> names) : TfwrStmt
{
    /// <summary>Names declared global.</summary>
    public IReadOnlyList<string> Names { get; } = names;
}

/// <summary>
/// Base type for IR expressions.
/// </summary>
public abstract class TfwrExpr;

/// <summary>
/// Identifier / name expression.
/// </summary>
/// <param name="name">Identifier text.</param>
public sealed class NameExpr(string name) : TfwrExpr
{
    /// <summary>Identifier text.</summary>
    public string Name { get; } = name;
}

/// <summary>
/// Literal value already formatted for Python emission.
/// </summary>
/// <param name="text">Python source text of the literal.</param>
public sealed class LiteralExpr(string text) : TfwrExpr
{
    /// <summary>Python source text of the literal.</summary>
    public string Text { get; } = text;
}

/// <summary>
/// Unary operator expression.
/// </summary>
/// <param name="op">Operator text (for example <c>not</c> or <c>-</c>).</param>
/// <param name="operand">Operand expression.</param>
public sealed class UnaryExpr(string op, TfwrExpr operand) : TfwrExpr
{
    /// <summary>Operator text.</summary>
    public string Op { get; } = op;

    /// <summary>Operand expression.</summary>
    public TfwrExpr Operand { get; } = operand;
}

/// <summary>
/// Binary operator expression.
/// </summary>
/// <param name="left">Left operand.</param>
/// <param name="op">Operator text.</param>
/// <param name="right">Right operand.</param>
public sealed class BinaryExpr(TfwrExpr left, string op, TfwrExpr right) : TfwrExpr
{
    /// <summary>Left operand.</summary>
    public TfwrExpr Left { get; } = left;

    /// <summary>Operator text.</summary>
    public string Op { get; } = op;

    /// <summary>Right operand.</summary>
    public TfwrExpr Right { get; } = right;
}

/// <summary>
/// Function or method call expression.
/// </summary>
/// <param name="callee">Callee expression.</param>
/// <param name="arguments">Argument expressions.</param>
public sealed class CallExpr(TfwrExpr callee, IReadOnlyList<TfwrExpr> arguments) : TfwrExpr
{
    /// <summary>Callee expression.</summary>
    public TfwrExpr Callee { get; } = callee;

    /// <summary>Argument expressions.</summary>
    public IReadOnlyList<TfwrExpr> Arguments { get; } = arguments;
}

/// <summary>
/// Attribute access: <c>target.name</c>.
/// </summary>
/// <param name="target">Object or module being accessed.</param>
/// <param name="name">Attribute name.</param>
public sealed class AttributeExpr(TfwrExpr target, string name) : TfwrExpr
{
    /// <summary>Object or module being accessed.</summary>
    public TfwrExpr Target { get; } = target;

    /// <summary>Attribute name.</summary>
    public string Name { get; } = name;
}

/// <summary>
/// Indexing expression: <c>target[index]</c>.
/// </summary>
/// <param name="target">Indexed expression.</param>
/// <param name="index">Index expression.</param>
public sealed class IndexExpr(TfwrExpr target, TfwrExpr index) : TfwrExpr
{
    /// <summary>Indexed expression.</summary>
    public TfwrExpr Target { get; } = target;

    /// <summary>Index expression.</summary>
    public TfwrExpr Index { get; } = index;
}

/// <summary>
/// List literal expression.
/// </summary>
/// <param name="elements">List elements.</param>
public sealed class ListExpr(IReadOnlyList<TfwrExpr> elements) : TfwrExpr
{
    /// <summary>List elements.</summary>
    public IReadOnlyList<TfwrExpr> Elements { get; } = elements;
}

/// <summary>
/// Dictionary literal expression.
/// </summary>
/// <param name="entries">Key/value entries.</param>
public sealed class DictExpr(IReadOnlyList<(TfwrExpr Key, TfwrExpr Value)> entries) : TfwrExpr
{
    /// <summary>Key/value entries.</summary>
    public IReadOnlyList<(TfwrExpr Key, TfwrExpr Value)> Entries { get; } = entries;
}

/// <summary>
/// Set literal expression.
/// </summary>
/// <param name="elements">Set elements.</param>
public sealed class SetExpr(IReadOnlyList<TfwrExpr> elements) : TfwrExpr
{
    /// <summary>Set elements.</summary>
    public IReadOnlyList<TfwrExpr> Elements { get; } = elements;
}

/// <summary>
/// Tuple literal expression.
/// </summary>
/// <param name="elements">Tuple elements.</param>
public sealed class TupleExpr(IReadOnlyList<TfwrExpr> elements) : TfwrExpr
{
    /// <summary>Tuple elements.</summary>
    public IReadOnlyList<TfwrExpr> Elements { get; } = elements;
}

/// <summary>
/// Conditional expression. Must be lowered to statements before emit.
/// </summary>
/// <param name="condition">Condition expression.</param>
/// <param name="whenTrue">Value when the condition is true.</param>
/// <param name="whenFalse">Value when the condition is false.</param>
public sealed class ConditionalExpr(TfwrExpr condition, TfwrExpr whenTrue, TfwrExpr whenFalse) : TfwrExpr
{
    /// <summary>Condition expression.</summary>
    public TfwrExpr Condition { get; } = condition;

    /// <summary>Value when the condition is true.</summary>
    public TfwrExpr WhenTrue { get; } = whenTrue;

    /// <summary>Value when the condition is false.</summary>
    public TfwrExpr WhenFalse { get; } = whenFalse;
}
