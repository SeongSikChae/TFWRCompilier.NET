namespace TFWR.Api;

/// <summary>
/// Compile-time stubs mirroring TFWR builtins. Bodies are never executed in-game.
/// Prefer <c>using static TFWR.Api.Game;</c>.
/// </summary>
public static class Game
{
    private static NotSupportedException Stub() =>
        new NotSupportedException("TFWR.Api is compile-time only. Compile with tfwrc.");

    /// <summary>Harvests the entity on the current tile.</summary>
    /// <returns><see langword="true"/> if something was harvested; otherwise <see langword="false"/>.</returns>
    public static bool Harvest() => throw Stub();

    /// <summary>Checks whether the current tile can be harvested.</summary>
    /// <returns><see langword="true"/> if harvest is possible; otherwise <see langword="false"/>.</returns>
    public static bool CanHarvest() => throw Stub();

    /// <summary>Plants the specified entity on the current tile.</summary>
    /// <param name="entity">Entity type to plant.</param>
    /// <returns><see langword="true"/> if planting succeeded; otherwise <see langword="false"/>.</returns>
    public static bool Plant(Entities entity) => throw Stub();

    /// <summary>Swaps the current tile with the adjacent tile in the given direction.</summary>
    /// <param name="direction">Direction of the adjacent tile.</param>
    /// <returns><see langword="true"/> if the swap succeeded; otherwise <see langword="false"/>.</returns>
    public static bool Swap(Direction direction) => throw Stub();

    /// <summary>Tills the ground on the current tile.</summary>
    public static void Till() => throw Stub();

    /// <summary>Uses an inventory item a given number of times.</summary>
    /// <param name="item">Item to use.</param>
    /// <param name="n">Number of uses. Defaults to 1.</param>
    /// <returns><see langword="true"/> if the item was used; otherwise <see langword="false"/>.</returns>
    public static bool UseItem(Items item, int n = 1) => throw Stub();

    /// <summary>Clears the current tile.</summary>
    public static void Clear() => throw Stub();

    /// <summary>Changes the farmer's hat.</summary>
    /// <param name="hat">Hat to wear.</param>
    public static void ChangeHat(Hats hat) => throw Stub();

    /// <summary>Moves the farmer one tile in the given direction.</summary>
    /// <param name="direction">Direction to move.</param>
    /// <returns><see langword="true"/> if the move succeeded; otherwise <see langword="false"/>.</returns>
    public static bool Move(Direction direction) => throw Stub();

    /// <summary>Checks whether the farmer can move in the given direction.</summary>
    /// <param name="direction">Direction to test.</param>
    /// <returns><see langword="true"/> if movement is possible; otherwise <see langword="false"/>.</returns>
    public static bool CanMove(Direction direction) => throw Stub();

    /// <summary>Gets the farmer's current X position.</summary>
    /// <returns>World X coordinate.</returns>
    public static int GetPosX() => throw Stub();

    /// <summary>Gets the farmer's current Y position.</summary>
    /// <returns>World Y coordinate.</returns>
    public static int GetPosY() => throw Stub();

    /// <summary>Gets the current world size.</summary>
    /// <returns>World size.</returns>
    public static int GetWorldSize() => throw Stub();

    /// <summary>Gets the entity type on the current tile.</summary>
    /// <returns>Entity type, or <see langword="null"/> if none.</returns>
    public static Entities? GetEntityType() => throw Stub();

    /// <summary>Gets the ground type of the current tile.</summary>
    /// <returns>Ground type.</returns>
    public static Grounds GetGroundType() => throw Stub();

    /// <summary>Gets the water level on the current tile.</summary>
    /// <returns>Water amount.</returns>
    public static double GetWater() => throw Stub();

    /// <summary>Gets how many of an item are in inventory.</summary>
    /// <param name="item">Item to count.</param>
    /// <returns>Item count.</returns>
    public static double NumItems(Items item) => throw Stub();

    /// <summary>Gets companion information for the current tile.</summary>
    /// <returns>Companion data, or <see langword="null"/> if none.</returns>
    public static object? GetCompanion() => throw Stub();

    /// <summary>Measures the current tile or an adjacent tile.</summary>
    /// <param name="direction">Optional adjacent direction to measure; current tile when omitted.</param>
    /// <returns>Measurement result.</returns>
    public static object? Measure(Direction? direction = null) => throw Stub();

    /// <summary>Spawns a drone that runs the given task.</summary>
    /// <param name="task">Work for the drone to perform.</param>
    /// <returns>Handle to the spawned drone.</returns>
    public static Drone SpawnDrone(Action task) => throw Stub();

    /// <summary>Waits until the given drone finishes.</summary>
    /// <param name="drone">Drone to wait for.</param>
    /// <returns>Result produced by the drone, if any.</returns>
    public static object? WaitFor(Drone drone) => throw Stub();

    /// <summary>Checks whether the given drone has finished.</summary>
    /// <param name="drone">Drone to query.</param>
    /// <returns><see langword="true"/> if finished; otherwise <see langword="false"/>.</returns>
    public static bool HasFinished(Drone drone) => throw Stub();

    /// <summary>Gets the maximum number of drones allowed.</summary>
    /// <returns>Maximum drone count.</returns>
    public static int MaxDrones() => throw Stub();

    /// <summary>Gets the current number of active drones.</summary>
    /// <returns>Active drone count.</returns>
    public static int NumDrones() => throw Stub();

    /// <summary>Gets the current simulation time.</summary>
    /// <returns>Elapsed time.</returns>
    public static double GetTime() => throw Stub();

    /// <summary>Gets the current tick count.</summary>
    /// <returns>Tick count.</returns>
    public static int GetTickCount() => throw Stub();

    /// <summary>Sets the script execution speed.</summary>
    /// <param name="speed">Execution speed multiplier.</param>
    public static void SetExecutionSpeed(double speed) => throw Stub();

    /// <summary>Sets the world size.</summary>
    /// <param name="size">Desired world size.</param>
    public static void SetWorldSize(double size) => throw Stub();

    /// <summary>Gets the cost of unlocking or upgrading something.</summary>
    /// <param name="thing">Unlock or item to query.</param>
    /// <param name="level">Optional level for the cost query.</param>
    /// <returns>Cost information.</returns>
    public static object? GetCost(object thing, int? level = null) => throw Stub();

    /// <summary>Unlocks the specified feature.</summary>
    /// <param name="unlock">Feature to unlock.</param>
    /// <returns><see langword="true"/> if unlocking succeeded; otherwise <see langword="false"/>.</returns>
    public static bool Unlock(Unlocks unlock) => throw Stub();

    /// <summary>Gets how many levels of something are unlocked.</summary>
    /// <param name="thing">Unlock or item to query.</param>
    /// <returns>Unlocked level count.</returns>
    public static int NumUnlocked(object thing) => throw Stub();

    /// <summary>Returns a random number in the unit interval.</summary>
    /// <returns>Random value.</returns>
    public static double Random() => throw Stub();

    /// <summary>Returns the minimum of the given arguments.</summary>
    /// <param name="args">Values to compare.</param>
    /// <returns>Minimum value.</returns>
    public static object Min(params object[] args) => throw Stub();

    /// <summary>Returns the maximum of the given arguments.</summary>
    /// <param name="args">Values to compare.</param>
    /// <returns>Maximum value.</returns>
    public static object Max(params object[] args) => throw Stub();

    /// <summary>Returns the absolute value of a number.</summary>
    /// <param name="x">Input value.</param>
    /// <returns>Absolute value.</returns>
    public static double Abs(double x) => throw Stub();

    /// <summary>Prints values to the in-game output.</summary>
    /// <param name="something">Values to print.</param>
    public static void Print(params object[] something) => throw Stub();

    /// <summary>Prints values quickly without the normal print delay.</summary>
    /// <param name="something">Values to print.</param>
    public static void QuickPrint(params object[] something) => throw Stub();

    /// <summary>Performs a flip animation.</summary>
    public static void DoAFlip() => throw Stub();

    /// <summary>Pets the piggy.</summary>
    public static void PetThePiggy() => throw Stub();

    /// <summary>Starts a leaderboard run with the given script.</summary>
    /// <param name="leaderboard">Leaderboard category.</param>
    /// <param name="fileName">Script file name to run.</param>
    /// <param name="speedup">Speedup factor for the run.</param>
    public static void LeaderboardRun(Leaderboards leaderboard, string fileName, double speedup) => throw Stub();

    /// <summary>Generates a range of integers from 0 up to, but not including, <paramref name="stop"/>.</summary>
    /// <param name="stop">Exclusive end.</param>
    /// <returns>Integer sequence.</returns>
    public static IEnumerable<int> Range(int stop) => throw Stub();

    /// <summary>Generates a range of integers from <paramref name="start"/> up to, but not including, <paramref name="stop"/>.</summary>
    /// <param name="start">Inclusive start.</param>
    /// <param name="stop">Exclusive end.</param>
    /// <returns>Integer sequence.</returns>
    public static IEnumerable<int> Range(int start, int stop) => throw Stub();

    /// <summary>Generates a range of integers from <paramref name="start"/> up to, but not including, <paramref name="stop"/>, stepping by <paramref name="step"/>.</summary>
    /// <param name="start">Inclusive start.</param>
    /// <param name="stop">Exclusive end.</param>
    /// <param name="step">Step size.</param>
    /// <returns>Integer sequence.</returns>
    public static IEnumerable<int> Range(int start, int stop, int step) => throw Stub();

    /// <summary>Gets the length of a sequence or collection-like value.</summary>
    /// <param name="obj">Value to measure.</param>
    /// <returns>Length.</returns>
    public static int Len(object obj) => throw Stub();

    /// <summary>Converts a value to its string representation.</summary>
    /// <param name="obj">Value to convert.</param>
    /// <returns>String form of <paramref name="obj"/>.</returns>
    public static string Str(object obj) => throw Stub();
}
