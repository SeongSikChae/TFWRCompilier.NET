namespace TFWR.Api;

/// <summary>
/// Cardinal directions used by movement and sensing APIs.
/// </summary>
public enum Direction
{
    /// <summary>North.</summary>
    North,

    /// <summary>East.</summary>
    East,

    /// <summary>South.</summary>
    South,

    /// <summary>West.</summary>
    West
}

/// <summary>
/// Entity types that can appear on a farm tile.
/// </summary>
public enum Entities
{
    /// <summary>Apple entity.</summary>
    Apple,

    /// <summary>Bush entity.</summary>
    Bush,

    /// <summary>Cactus entity.</summary>
    Cactus,

    /// <summary>Carrot entity.</summary>
    Carrot,

    /// <summary>Dead pumpkin entity.</summary>
    Dead_Pumpkin,

    /// <summary>Dinosaur entity.</summary>
    Dinosaur,

    /// <summary>Grass entity.</summary>
    Grass,

    /// <summary>Hedge entity.</summary>
    Hedge,

    /// <summary>Pumpkin entity.</summary>
    Pumpkin,

    /// <summary>Sunflower entity.</summary>
    Sunflower,

    /// <summary>Treasure entity.</summary>
    Treasure,

    /// <summary>Tree entity.</summary>
    Tree
}

/// <summary>
/// Ground types for farm tiles.
/// </summary>
public enum Grounds
{
    /// <summary>Grassland ground.</summary>
    Grassland,

    /// <summary>Tilled soil ground.</summary>
    Soil
}

/// <summary>
/// Hats that can be worn by the farmer.
/// </summary>
public enum Hats
{
    /// <summary>Brown hat.</summary>
    Brown_Hat,

    /// <summary>Cactus hat.</summary>
    Cactus_Hat,

    /// <summary>Carrot hat.</summary>
    Carrot_Hat,

    /// <summary>Dinosaur hat.</summary>
    Dinosaur_Hat,

    /// <summary>Gold hat.</summary>
    Gold_Hat,

    /// <summary>Gold trophy hat.</summary>
    Gold_Trophy_Hat,

    /// <summary>Golden cactus hat.</summary>
    Golden_Cactus_Hat,

    /// <summary>Golden carrot hat.</summary>
    Golden_Carrot_Hat,

    /// <summary>Golden gold hat.</summary>
    Golden_Gold_Hat,

    /// <summary>Golden pumpkin hat.</summary>
    Golden_Pumpkin_Hat,

    /// <summary>Golden sunflower hat.</summary>
    Golden_Sunflower_Hat,

    /// <summary>Golden tree hat.</summary>
    Golden_Tree_Hat,

    /// <summary>Gray hat.</summary>
    Gray_Hat,

    /// <summary>Green hat.</summary>
    Green_Hat,

    /// <summary>Pumpkin hat.</summary>
    Pumpkin_Hat,

    /// <summary>Purple hat.</summary>
    Purple_Hat,

    /// <summary>Silver trophy hat.</summary>
    Silver_Trophy_Hat,

    /// <summary>Straw hat.</summary>
    Straw_Hat,

    /// <summary>Sunflower hat.</summary>
    Sunflower_Hat,

    /// <summary>The farmer's remains hat.</summary>
    The_Farmers_Remains,

    /// <summary>Top hat.</summary>
    Top_Hat,

    /// <summary>Traffic cone hat.</summary>
    Traffic_Cone,

    /// <summary>Stacked traffic cone hat.</summary>
    Traffic_Cone_Stack,

    /// <summary>Tree hat.</summary>
    Tree_Hat,

    /// <summary>Wizard hat.</summary>
    Wizard_Hat,

    /// <summary>Wood trophy hat.</summary>
    Wood_Trophy_Hat
}

/// <summary>
/// Inventory and consumable item types.
/// </summary>
public enum Items
{
    /// <summary>Bone item.</summary>
    Bone,

    /// <summary>Cactus item.</summary>
    Cactus,

    /// <summary>Carrot item.</summary>
    Carrot,

    /// <summary>Fertilizer item.</summary>
    Fertilizer,

    /// <summary>Gold item.</summary>
    Gold,

    /// <summary>Hay item.</summary>
    Hay,

    /// <summary>Piggy item.</summary>
    Piggy,

    /// <summary>Power item.</summary>
    Power,

    /// <summary>Pumpkin item.</summary>
    Pumpkin,

    /// <summary>Water item.</summary>
    Water,

    /// <summary>Weird substance item.</summary>
    Weird_Substance,

    /// <summary>Wood item.</summary>
    Wood
}

/// <summary>
/// Leaderboard categories for timed runs.
/// </summary>
public enum Leaderboards
{
    /// <summary>Cactus leaderboard.</summary>
    Cactus,

    /// <summary>Cactus single-player leaderboard.</summary>
    Cactus_Single,

    /// <summary>Carrots leaderboard.</summary>
    Carrots,

    /// <summary>Carrots single-player leaderboard.</summary>
    Carrots_Single,

    /// <summary>Dinosaur leaderboard.</summary>
    Dinosaur,

    /// <summary>Fastest reset leaderboard.</summary>
    Fastest_Reset,

    /// <summary>Hay leaderboard.</summary>
    Hay,

    /// <summary>Hay single-player leaderboard.</summary>
    Hay_Single,

    /// <summary>Maze leaderboard.</summary>
    Maze,

    /// <summary>Maze single-player leaderboard.</summary>
    Maze_Single,

    /// <summary>Pumpkins leaderboard.</summary>
    Pumpkins,

    /// <summary>Pumpkins single-player leaderboard.</summary>
    Pumpkins_Single,

    /// <summary>Sunflowers leaderboard.</summary>
    Sunflowers,

    /// <summary>Sunflowers single-player leaderboard.</summary>
    Sunflowers_Single,

    /// <summary>Wood leaderboard.</summary>
    Wood,

    /// <summary>Wood single-player leaderboard.</summary>
    Wood_Single
}

/// <summary>
/// Unlockable features and research nodes.
/// </summary>
public enum Unlocks
{
    /// <summary>Auto unlock feature.</summary>
    Auto_Unlock,

    /// <summary>Cactus unlock.</summary>
    Cactus,

    /// <summary>Carrots unlock.</summary>
    Carrots,

    /// <summary>Costs unlock.</summary>
    Costs,

    /// <summary>Debug unlock.</summary>
    Debug,

    /// <summary>Debug 2 unlock.</summary>
    Debug_2,

    /// <summary>Dictionaries unlock.</summary>
    Dictionaries,

    /// <summary>Dinosaurs unlock.</summary>
    Dinosaurs,

    /// <summary>Expand unlock.</summary>
    Expand,

    /// <summary>Fertilizer unlock.</summary>
    Fertilizer,

    /// <summary>Functions unlock.</summary>
    Functions,

    /// <summary>Grass unlock.</summary>
    Grass,

    /// <summary>Hats unlock.</summary>
    Hats,

    /// <summary>Import unlock.</summary>
    Import,

    /// <summary>Leaderboard unlock.</summary>
    Leaderboard,

    /// <summary>Lists unlock.</summary>
    Lists,

    /// <summary>Loops unlock.</summary>
    Loops,

    /// <summary>Mazes unlock.</summary>
    Mazes,

    /// <summary>Megafarm unlock.</summary>
    Megafarm,

    /// <summary>Operators unlock.</summary>
    Operators,

    /// <summary>Plant unlock.</summary>
    Plant,

    /// <summary>Polyculture unlock.</summary>
    Polyculture,

    /// <summary>Pumpkins unlock.</summary>
    Pumpkins,

    /// <summary>Senses unlock.</summary>
    Senses,

    /// <summary>Simulation unlock.</summary>
    Simulation,

    /// <summary>Speed unlock.</summary>
    Speed,

    /// <summary>Sunflowers unlock.</summary>
    Sunflowers,

    /// <summary>The farmer's remains unlock.</summary>
    The_Farmers_Remains,

    /// <summary>Timing unlock.</summary>
    Timing,

    /// <summary>Top hat unlock.</summary>
    Top_Hat,

    /// <summary>Trees unlock.</summary>
    Trees,

    /// <summary>Utilities unlock.</summary>
    Utilities,

    /// <summary>Variables unlock.</summary>
    Variables,

    /// <summary>Watering unlock.</summary>
    Watering
}

/// <summary>
/// Handle representing a spawned drone. Opaque at compile time.
/// </summary>
public sealed class Drone
{
    /// <summary>
    /// Creates a drone handle. Only the compiler emits real drone values.
    /// </summary>
    internal Drone() { }
}
