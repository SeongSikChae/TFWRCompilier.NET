/**
 * Compile-time ambient API mirroring TFWR.Api / in-game builtins.
 * Bodies are never executed; compile with `tfwrc --lang ts`.
 */

export declare const enum Direction {
  North = "North",
  East = "East",
  South = "South",
  West = "West",
}

export declare const enum Entities {
  Apple = "Apple",
  Bush = "Bush",
  Cactus = "Cactus",
  Carrot = "Carrot",
  Dead_Pumpkin = "Dead_Pumpkin",
  Dinosaur = "Dinosaur",
  Grass = "Grass",
  Hedge = "Hedge",
  Pumpkin = "Pumpkin",
  Sunflower = "Sunflower",
  Treasure = "Treasure",
  Tree = "Tree",
}

export declare const enum Grounds {
  Grassland = "Grassland",
  Soil = "Soil",
}

export declare const enum Hats {
  Brown_Hat = "Brown_Hat",
  Cactus_Hat = "Cactus_Hat",
  Carrot_Hat = "Carrot_Hat",
  Dinosaur_Hat = "Dinosaur_Hat",
  Gold_Hat = "Gold_Hat",
  Gold_Trophy_Hat = "Gold_Trophy_Hat",
  Golden_Cactus_Hat = "Golden_Cactus_Hat",
  Golden_Carrot_Hat = "Golden_Carrot_Hat",
  Golden_Gold_Hat = "Golden_Gold_Hat",
  Golden_Pumpkin_Hat = "Golden_Pumpkin_Hat",
  Golden_Sunflower_Hat = "Golden_Sunflower_Hat",
  Golden_Tree_Hat = "Golden_Tree_Hat",
  Gray_Hat = "Gray_Hat",
  Green_Hat = "Green_Hat",
  Pumpkin_Hat = "Pumpkin_Hat",
  Purple_Hat = "Purple_Hat",
  Silver_Trophy_Hat = "Silver_Trophy_Hat",
  Straw_Hat = "Straw_Hat",
  Sunflower_Hat = "Sunflower_Hat",
  The_Farmers_Remains = "The_Farmers_Remains",
  Top_Hat = "Top_Hat",
  Traffic_Cone = "Traffic_Cone",
  Traffic_Cone_Stack = "Traffic_Cone_Stack",
  Tree_Hat = "Tree_Hat",
  Wizard_Hat = "Wizard_Hat",
  Wood_Trophy_Hat = "Wood_Trophy_Hat",
}

export declare const enum Items {
  Bone = "Bone",
  Cactus = "Cactus",
  Carrot = "Carrot",
  Fertilizer = "Fertilizer",
  Gold = "Gold",
  Hay = "Hay",
  Piggy = "Piggy",
  Power = "Power",
  Pumpkin = "Pumpkin",
  Water = "Water",
  Weird_Substance = "Weird_Substance",
  Wood = "Wood",
}

export declare const enum Leaderboards {
  Cactus = "Cactus",
  Cactus_Single = "Cactus_Single",
  Carrots = "Carrots",
  Carrots_Single = "Carrots_Single",
  Dinosaur = "Dinosaur",
  Fastest_Reset = "Fastest_Reset",
  Hay = "Hay",
  Hay_Single = "Hay_Single",
  Maze = "Maze",
  Maze_Single = "Maze_Single",
  Pumpkins = "Pumpkins",
  Pumpkins_Single = "Pumpkins_Single",
  Sunflowers = "Sunflowers",
  Sunflowers_Single = "Sunflowers_Single",
  Wood = "Wood",
  Wood_Single = "Wood_Single",
}

export declare const enum Unlocks {
  Auto_Unlock = "Auto_Unlock",
  Cactus = "Cactus",
  Carrots = "Carrots",
  Costs = "Costs",
  Debug = "Debug",
  Debug_2 = "Debug_2",
  Dictionaries = "Dictionaries",
  Dinosaurs = "Dinosaurs",
  Expand = "Expand",
  Fertilizer = "Fertilizer",
  Functions = "Functions",
  Grass = "Grass",
  Hats = "Hats",
  Import = "Import",
  Leaderboard = "Leaderboard",
  Lists = "Lists",
  Loops = "Loops",
  Mazes = "Mazes",
  Megafarm = "Megafarm",
  Operators = "Operators",
  Plant = "Plant",
  Polyculture = "Polyculture",
  Pumpkins = "Pumpkins",
  Senses = "Senses",
  Simulation = "Simulation",
  Speed = "Speed",
  Sunflowers = "Sunflowers",
  The_Farmers_Remains = "The_Farmers_Remains",
  Timing = "Timing",
  Top_Hat = "Top_Hat",
  Trees = "Trees",
  Utilities = "Utilities",
  Variables = "Variables",
  Watering = "Watering",
}

/** Opaque drone handle. */
export declare class Drone {
  private constructor();
}

export declare function harvest(): boolean;
export declare function canHarvest(): boolean;
export declare function plant(entity: Entities): boolean;
export declare function swap(direction: Direction): boolean;
export declare function till(): void;
export declare function useItem(item: Items, n?: number): boolean;
export declare function clear(): void;
export declare function changeHat(hat: Hats): void;
export declare function move(direction: Direction): boolean;
export declare function canMove(direction: Direction): boolean;
export declare function getPosX(): number;
export declare function getPosY(): number;
export declare function getWorldSize(): number;
export declare function getEntityType(): Entities | null;
export declare function getGroundType(): Grounds;
export declare function getWater(): number;
export declare function numItems(item: Items): number;
export declare function getCompanion(): unknown;
export declare function measure(direction?: Direction | null): unknown;
export declare function spawnDrone(task: () => void): Drone;
export declare function waitFor(drone: Drone): unknown;
export declare function hasFinished(drone: Drone): boolean;
export declare function maxDrones(): number;
export declare function numDrones(): number;
export declare function getTime(): number;
export declare function getTickCount(): number;
export declare function setExecutionSpeed(speed: number): void;
export declare function setWorldSize(size: number): void;
export declare function getCost(thing: unknown, level?: number | null): unknown;
export declare function unlock(unlock: Unlocks): boolean;
export declare function numUnlocked(thing: unknown): number;
export declare function random(): number;
export declare function min(...args: unknown[]): unknown;
export declare function max(...args: unknown[]): unknown;
export declare function abs(x: number): number;
export declare function print(...something: unknown[]): void;
export declare function quickPrint(...something: unknown[]): void;
export declare function doAFlip(): void;
export declare function petThePiggy(): void;
export declare function leaderboardRun(
  leaderboard: Leaderboards,
  fileName: string,
  speedup: number,
): void;
export declare function range(stop: number): Iterable<number>;
export declare function range(start: number, stop: number): Iterable<number>;
export declare function range(start: number, stop: number, step: number): Iterable<number>;
export declare function len(obj: unknown): number;
export declare function str(obj: unknown): string;
