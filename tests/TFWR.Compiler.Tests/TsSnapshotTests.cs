using TFWR.Compiler;

namespace TFWR.Compiler.Tests;

public class TsSnapshotTests
{
    [Fact]
    public void HarvestLoop_EmitsWhileAndGameCalls()
    {
        var result = CompileSingle("Program.ts", """
            import { canHarvest, harvest, move, Direction } from "tfwr";

            export function main() {
                while (true) {
                    if (canHarvest()) {
                        harvest();
                    }
                    move(Direction.North);
                }
            }
            """);

        AssertSuccess(result);
        var py = GetModule(result, "Program.py");
        Assert.Contains("def main():", py);
        Assert.Contains("while True:", py);
        Assert.Contains("if can_harvest():", py);
        Assert.Contains("harvest()", py);
        Assert.Contains("move(North)", py);
        Assert.Contains("if __name__ == \"__main__\":", py);
        Assert.Contains("main()", py);
        AssertForbidden(py);
    }

    [Fact]
    public void HelperFunction_EmitsDefsBeforeEntry()
    {
        var result = CompileSingle("Helpers.ts", """
            import { move, Direction } from "tfwr";

            export function MoveN(n: number, dir: Direction) {
                for (let i = 0; i < n; i++) {
                    move(dir);
                }
            }

            export function main() {
                MoveN(3, Direction.East);
            }
            """);

        AssertSuccess(result);
        var py = GetModule(result, "Helpers.py");
        Assert.Contains("def MoveN(n, dir):", py);
        Assert.Contains("for i in range(n):", py);
        Assert.Contains("move(dir)", py);
        Assert.Contains("MoveN(3, East)", py);
        AssertForbidden(py);
    }

    [Fact]
    public void SimpleClass_LowersToDictAndFunctions()
    {
        var result = CompileSingle("Point.ts", """
            import { print } from "tfwr";

            export class Point {
                X: number;
                Y: number;

                constructor(x: number, y: number) {
                    this.X = x;
                    this.Y = y;
                }

                Move(dx: number, dy: number) {
                    this.X = this.X + dx;
                    this.Y = this.Y + dy;
                }
            }

            export function main() {
                const p = new Point(1, 2);
                p.Move(3, 4);
                print(p.X);
            }
            """);

        AssertSuccess(result);
        var py = GetModule(result, "Point.py");
        Assert.Contains("def Point_new(x, y):", py);
        Assert.Contains("self[\"X\"]", py);
        Assert.Contains("def Point_Move(self, dx, dy):", py);
        Assert.Contains("Point_new(1, 2)", py);
        Assert.Contains("Point_Move(p, 3, 4)", py);
        Assert.Contains("print(", py);
        AssertForbidden(py);
    }

    [Fact]
    public void MultiFile_GeneratesImport()
    {
        var result = TfwrCompiler.CompileToMemory(new Dictionary<string, string>
        {
            ["Utils.ts"] = """
                import { move, Direction } from "tfwr";

                export class Utils {
                    static Step(dir: Direction) {
                        move(dir);
                    }
                }
                """,
            ["Program.ts"] = """
                import { Direction } from "tfwr";
                import { Utils } from "./Utils";

                export function main() {
                    Utils.Step(Direction.South);
                }
                """
        }, language: "ts");

        AssertSuccess(result);
        var program = GetModule(result, "Program.py");
        Assert.Contains("import Utils", program);
        Assert.Contains("Utils.Utils_Step(South)", program);
        AssertForbidden(program);
        AssertForbidden(GetModule(result, "Utils.py"));
    }

    [Fact]
    public void Collections_MapToListDictSet()
    {
        var result = CompileSingle("Collections.ts", """
            import { print, len } from "tfwr";

            export function main() {
                const list = [1, 2, 3];
                list.push(4);
                const dict: Record<string, number> = { a: 1 };
                dict["b"] = 2;
                const set = new Set([1, 2]);
                set.add(3);
                if (set.has(3)) {
                    print(len(list));
                }
            }
            """);

        AssertSuccess(result);
        var py = GetModule(result, "Collections.py");
        Assert.Contains("list = [1, 2, 3]", py);
        Assert.Contains("list.append(4)", py);
        Assert.Contains("dict = {\"a\": 1}", py);
        Assert.Contains("dict[\"b\"] = 2", py);
        Assert.Contains("set = {1, 2}", py);
        Assert.Contains("set.add(3)", py);
        Assert.Contains("3 in set", py);
        Assert.Contains("len(list)", py);
        AssertForbidden(py);
    }

    [Fact]
    public void TopLevelEntry_EmitsStatementsWithoutDef()
    {
        var result = TfwrCompiler.CompileToMemory(
            new Dictionary<string, string>
            {
                ["Program.ts"] = """
                    import { harvest } from "tfwr";

                    export function main() {
                        harvest();
                    }
                    """
            },
            emitTopLevelEntry: true,
            language: "ts");

        AssertSuccess(result);
        var py = GetModule(result, "Program.py");
        Assert.Contains("harvest()", py);
        Assert.DoesNotContain("def ", py);
        Assert.DoesNotContain("__name__", py);
        AssertForbidden(py);
    }

    [Fact]
    public void EnumMembers_EmitTfwrNames()
    {
        var result = CompileSingle("Enums.ts", """
            import { plant, useItem, getEntityType, move, Entities, Items, Direction } from "tfwr";

            export function main() {
                plant(Entities.Bush);
                useItem(Items.Water);
                if (getEntityType() == Entities.Grass) {
                    move(Direction.North);
                }
            }
            """);

        AssertSuccess(result);
        var py = GetModule(result, "Enums.py");
        Assert.Contains("plant(Entities.Bush)", py);
        Assert.Contains("use_item(Items.Water)", py);
        Assert.Contains("Entities.Grass", py);
        Assert.Contains("move(North)", py);
        AssertForbidden(py);
    }

    [Fact]
    public void TopLevelStatements_WithToplevelFlag()
    {
        var result = TfwrCompiler.CompileToMemory(
            new Dictionary<string, string>
            {
                ["Program.ts"] = """
                    import { clear, canHarvest, harvest, move, Direction } from "tfwr";

                    clear();
                    while (true) {
                        if (canHarvest()) {
                            harvest();
                        }
                        move(Direction.North);
                    }
                    """
            },
            emitTopLevelEntry: true,
            language: "ts");

        AssertSuccess(result);
        var py = GetModule(result, "Program.py");
        Assert.Contains("clear()", py);
        Assert.Contains("while True:", py);
        Assert.Contains("can_harvest()", py);
        Assert.Contains("harvest()", py);
        Assert.Contains("move(North)", py);
        Assert.DoesNotContain("def ", py);
        AssertForbidden(py);
    }

    [Fact]
    public void Ternary_LowersToIfElseWithTemp()
    {
        var result = CompileSingle("Ternary.ts", """
            import { print } from "tfwr";

            export function main() {
                let n = 1;
                const x = n > 0 ? 10 : 20;
                print(x);
                print(n == 0 ? 1 : 2);
            }
            """);

        AssertSuccess(result);
        var py = GetModule(result, "Ternary.py");
        Assert.Contains("if (n > 0):", py);
        Assert.Contains("_tern1 = 10", py);
        Assert.Contains("_tern1 = 20", py);
        Assert.Contains("x = _tern1", py);
        Assert.Contains("if (n == 0):", py);
        Assert.Contains("print(_tern2)", py);
        AssertForbidden(py);
    }

    [Fact]
    public void ElseIf_EmitsElifNotNestedElseIf()
    {
        var result = CompileSingle("ElseIf.ts", """
            import { canMove, move, Direction } from "tfwr";

            export function main() {
                let direction = 0;
                const left = 3;
                const right = 1;
                const back = 2;
                const directions = [Direction.North, Direction.East, Direction.South, Direction.West];

                if (canMove(directions[left])) {
                    direction = left;
                } else if (canMove(directions[direction])) {
                } else if (canMove(directions[right])) {
                    direction = right;
                } else {
                    direction = back;
                }

                move(directions[direction]);
            }
            """);

        AssertSuccess(result);
        var py = GetModule(result, "ElseIf.py");
        Assert.Contains("if can_move(directions[left]):", py);
        Assert.Contains("elif can_move(directions[direction]):", py);
        Assert.Contains("elif can_move(directions[right]):", py);
        Assert.Contains("else:", py);
        Assert.DoesNotContain("else:\n    if can_move", py.Replace("\r\n", "\n"));
        AssertForbidden(py);
    }

    [Fact]
    public void Ternary_WithSideEffects_KeepsBranchesLocal()
    {
        var result = CompileSingle("TernSide.ts", """
            import { print } from "tfwr";

            export function main() {
                let a = 0;
                let b = 0;
                const cond = false;
                const x = cond ? (a = 1) : (b = 2);
                print(x);
                print(a);
                print(b);
            }
            """);

        AssertSuccess(result);
        var py = GetModule(result, "TernSide.py");
        // Assignments must live inside if/else, not before the condition
        var ifIdx = py.IndexOf("if cond:", StringComparison.Ordinal);
        Assert.True(ifIdx >= 0, py);
        var beforeIf = py.Substring(0, ifIdx);
        Assert.DoesNotContain("a = 1", beforeIf);
        Assert.DoesNotContain("b = 2", beforeIf);
        Assert.Contains("a = 1", py);
        Assert.Contains("b = 2", py);
        AssertForbidden(py);
    }

    [Fact]
    public void ElseIf_WithTernaryCondition_NestsInsteadOfBrokenElif()
    {
        var result = CompileSingle("ElifHoist.ts", """
            import { print } from "tfwr";

            export function main() {
                let n = 1;
                if (n < 0) {
                    print(0);
                } else if (n > 0 ? true : false) {
                    print(1);
                } else {
                    print(2);
                }
            }
            """);

        AssertSuccess(result);
        var py = GetModule(result, "ElifHoist.py");
        // Must not use a temp in elif condition before it is defined
        Assert.DoesNotContain("elif _tern", py);
        Assert.Contains("else:", py);
        Assert.Contains("_tern", py);
        AssertForbidden(py);
    }

    [Fact]
    public void StaticField_IsRejected()
    {
        var result = CompileSingle("Static.ts", """
            import { print } from "tfwr";

            export class Program {
                static Size = 12;

                static main() {
                    print(Program.Size);
                }
            }
            """);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("Static fields", StringComparison.Ordinal));
    }

    [Fact]
    public void DuplicateFunctionNames_AreRejected()
    {
        var result = CompileSingle("Dup.ts", """
            import { harvest } from "tfwr";

            export class Helper {
                Move() {
                    harvest();
                }
            }

            export function Helper_Move() {
                harvest();
            }

            export function main() {
                Helper_Move();
            }
            """);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("Duplicate function name", StringComparison.Ordinal));
    }

    [Fact]
    public void MultipleEntryPoints_AreRejected()
    {
        var result = CompileSingle("Entries.ts", """
            import { harvest } from "tfwr";

            export function main() {
                harvest();
            }

            /** @tfwrEntry */
            export function boot() {
                harvest();
            }
            """);

        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains("Multiple entry points", StringComparison.Ordinal));
    }

    private static CompileResult CompileSingle(string fileName, string source) =>
        TfwrCompiler.CompileToMemory(new Dictionary<string, string> { [fileName] = source }, language: "ts");

    private static void AssertSuccess(CompileResult result)
    {
        Assert.True(result.Success,
            string.Join(Environment.NewLine, result.Diagnostics.Select(d => d.ToString())));
    }

    private static string GetModule(CompileResult result, string relativePath)
    {
        Assert.True(result.EmittedFiles.TryGetValue(relativePath, out var text),
            $"Missing module {relativePath}. Have: {string.Join(", ", result.EmittedFiles.Keys)}");
        return text!;
    }

    private static void AssertForbidden(string py)
    {
        Assert.DoesNotContain("lambda ", py);
        Assert.DoesNotContain("f\"", py);
        Assert.DoesNotContain(" is None", py);
        Assert.DoesNotContain("\nclass ", py);
        Assert.DoesNotContain("try:", py);
    }
}
