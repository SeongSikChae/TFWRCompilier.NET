using TFWR.Compiler;

namespace TFWR.Compiler.Tests;

public class SnapshotTests
{
    [Fact]
    public void HarvestLoop_EmitsWhileAndGameCalls()
    {
        var result = CompileSingle("Program.cs", """
            using static TFWR.Api.Game;
            using TFWR.Api;

            public static class Program
            {
                public static void Main()
                {
                    while (true)
                    {
                        if (CanHarvest())
                        {
                            Harvest();
                        }
                        Move(Direction.North);
                    }
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
        var result = CompileSingle("Helpers.cs", """
            using static TFWR.Api.Game;
            using TFWR.Api;

            public static class Helpers
            {
                public static void MoveN(int n, Direction dir)
                {
                    for (int i = 0; i < n; i++)
                    {
                        Move(dir);
                    }
                }

                public static void Main()
                {
                    MoveN(3, Direction.East);
                }
            }
            """);

        AssertSuccess(result);
        var py = GetModule(result, "Helpers.py");
        Assert.Contains("def Helpers_MoveN(n, dir):", py);
        Assert.Contains("for i in range(n):", py);
        Assert.Contains("move(dir)", py);
        Assert.Contains("Helpers_MoveN(3, East)", py);
        AssertForbidden(py);
    }

    [Fact]
    public void SimpleClass_LowersToDictAndFunctions()
    {
        var result = CompileSingle("Point.cs", """
            using static TFWR.Api.Game;

            public class Point
            {
                public double X;
                public double Y;

                public Point(double x, double y)
                {
                    this.X = x;
                    this.Y = y;
                }

                public void Move(double dx, double dy)
                {
                    this.X = this.X + dx;
                    this.Y = this.Y + dy;
                }
            }

            public static class Program
            {
                public static void Main()
                {
                    var p = new Point(1, 2);
                    p.Move(3, 4);
                    Print(p.X);
                }
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
            ["Utils.cs"] = """
                using static TFWR.Api.Game;
                using TFWR.Api;

                public static class Utils
                {
                    public static void Step(Direction dir)
                    {
                        Move(dir);
                    }
                }
                """,
            ["Program.cs"] = """
                using TFWR.Api;

                public static class Program
                {
                    public static void Main()
                    {
                        Utils.Step(Direction.South);
                    }
                }
                """
        });

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
        var result = CompileSingle("Collections.cs", """
            using static TFWR.Api.Game;
            using System.Collections.Generic;

            public static class Program
            {
                public static void Main()
                {
                    var list = new List<int> { 1, 2, 3 };
                    list.Add(4);
                    var dict = new Dictionary<string, int> { ["a"] = 1 };
                    dict["b"] = 2;
                    var set = new HashSet<int> { 1, 2 };
                    set.Add(3);
                    if (set.Contains(3))
                    {
                        Print(Len(list));
                    }
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
                ["Program.cs"] = """
                    using TFWR.Api;

                    namespace TFWR
                    {
                        public class Program
                        {
                            public static void Main()
                            {
                                Game.Harvest();
                            }
                        }
                    }
                    """
            },
            emitTopLevelEntry: true);

        AssertSuccess(result);
        var py = GetModule(result, "Program.py");
        Assert.Contains("harvest()", py);
        Assert.DoesNotContain("def ", py);
        Assert.DoesNotContain("__name__", py);
        Assert.DoesNotContain("Program_new", py);
        AssertForbidden(py);
    }

    [Fact]
    public void EnumMembers_EmitTfwrNames()
    {
        var result = CompileSingle("Enums.cs", """
            using TFWR.Api;

            public static class Program
            {
                public static void Main()
                {
                    Game.Plant(Entities.Bush);
                    Game.UseItem(Items.Water);
                    if (Game.GetEntityType() == Entities.Grass)
                    {
                        Game.Move(Direction.North);
                    }
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
    public void CSharpTopLevelStatements_WithToplevelFlag()
    {
        var result = TfwrCompiler.CompileToMemory(
            new Dictionary<string, string>
            {
                ["Program.cs"] = """
                    using TFWR.Api;

                    Game.Clear();
                    while (true)
                    {
                        if (Game.CanHarvest())
                        {
                            Game.Harvest();
                        }
                        Game.Move(Direction.North);
                    }
                    """
            },
            emitTopLevelEntry: true);

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
        var result = CompileSingle("Ternary.cs", """
            using static TFWR.Api.Game;

            public static class Program
            {
                public static void Main()
                {
                    var n = 1;
                    var x = n > 0 ? 10 : 20;
                    Print(x);
                    Print(n == 0 ? 1 : 2);
                }
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
        Assert.DoesNotContain(" and ", py);
        AssertForbidden(py);
    }

    [Fact]
    public void NestedTernary_UsesBranchLocalTemps()
    {
        var result = CompileSingle("NestedTernary.cs", """
            using static TFWR.Api.Game;

            public static class Program
            {
                public static void Main()
                {
                    var a = 1;
                    var x = a > 0 ? (a > 1 ? 3 : 4) : 5;
                    Print(x);
                }
            }
            """);

        AssertSuccess(result);
        var py = GetModule(result, "NestedTernary.py");
        Assert.Contains("if (a > 0):", py);
        Assert.Contains("if (a > 1):", py);
        Assert.Contains("x = _tern", py);
        AssertForbidden(py);
    }

    [Fact]
    public void NullForgivingAndCast_AreStripped()
    {
        var result = CompileSingle("NullForgiving.cs", """
            using TFWR.Api;

            public static class Program
            {
                public static void Main()
                {
                    Entities entity = (Entities)Game.GetEntityType()!;
                    Game.Plant(entity);
                }
            }
            """);

        AssertSuccess(result);
        var py = GetModule(result, "NullForgiving.py");
        Assert.Contains("entity = get_entity_type()", py);
        Assert.Contains("plant(entity)", py);
        AssertForbidden(py);
    }

    [Fact]
    public void NullableHasValueAndValue_LowerToNoneChecks()
    {
        var result = CompileSingle("Nullable.cs", """
            using TFWR.Api;

            public static class Program
            {
                public static void Main()
                {
                    Entities? entity = Game.GetEntityType();
                    Game.Harvest();
                    if (entity.HasValue)
                    {
                        Game.Plant(entity.Value);
                    }
                }
            }
            """);

        AssertSuccess(result);
        var py = GetModule(result, "Nullable.py");
        Assert.Contains("entity = get_entity_type()", py);
        Assert.Contains("if (entity != None):", py);
        Assert.Contains("plant(entity)", py);
        Assert.DoesNotContain("Nullable_get_HasValue", py);
        Assert.DoesNotContain("Nullable_get_Value", py);
        AssertForbidden(py);
    }

    [Fact]
    public void ForEachTupleDeconstruction_EmitsPythonUnpack()
    {
        var result = CompileSingle("ForeachTuple.cs", """
            using System.Collections.Generic;
            using static TFWR.Api.Game;

            public static class Program
            {
                public static void Main()
                {
                    List<(int x, int y)> list = [];
                    list.Add((1, 2));
                    foreach ((int x, int y) in list)
                    {
                        Print(x);
                        Print(y);
                    }
                }
            }
            """);

        AssertSuccess(result);
        var py = GetModule(result, "ForeachTuple.py");
        Assert.Contains("for x, y in list:", py);
        Assert.Contains("print(x)", py);
        Assert.Contains("print(y)", py);
        AssertForbidden(py);
    }

    [Fact]
    public void Record_LowersLikeClassWithPrimaryConstructor()
    {
        var result = CompileSingle("Record.cs", """
            using static TFWR.Api.Game;

            public record Position(int X, int Y);

            public static class Program
            {
                public static void Main()
                {
                    var p = new Position(1, 2);
                    Print(p.X);
                    Print(p.Y);
                }
            }
            """);

        AssertSuccess(result);
        var py = GetModule(result, "Record.py");
        Assert.Contains("def Position_new(X, Y):", py);
        Assert.Contains("\"X\": X", py);
        Assert.Contains("\"Y\": Y", py);
        Assert.Contains("Position_new(1, 2)", py);
        Assert.Contains("p[\"X\"]", py);
        Assert.Contains("p[\"Y\"]", py);
        AssertForbidden(py);
    }

    [Fact]
    public void ElseIf_EmitsElifNotNestedElseIf()
    {
        var result = CompileSingle("ElseIf.cs", """
            using static TFWR.Api.Game;
            using TFWR.Api;

            public static class Program
            {
                public static void Main()
                {
                    int direction = 0;
                    int left = 3;
                    int right = 1;
                    int back = 2;
                    Direction[] directions = [Direction.North, Direction.East, Direction.South, Direction.West];

                    if (CanMove(directions[left]))
                    {
                        direction = left;
                    }
                    else if (CanMove(directions[direction]))
                    {
                    }
                    else if (CanMove(directions[right]))
                    {
                        direction = right;
                    }
                    else
                    {
                        direction = back;
                    }

                    Move(directions[direction]);
                }
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

    private static CompileResult CompileSingle(string fileName, string source) =>
        TfwrCompiler.CompileToMemory(new Dictionary<string, string> { [fileName] = source });

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
