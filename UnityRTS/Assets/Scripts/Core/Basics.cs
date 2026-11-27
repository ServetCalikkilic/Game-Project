using System;

namespace Rts.Core
{
    public enum ResourceType { Wood, Food, Gold }
    public enum UnitType { Worker, Spearman, Archer, Raider, Guardian, Ram }
    public enum BuildingType { Citadel, House, Barracks, ArcheryYard, Stable }
    public enum TechId { Age2IronAge, SharpenedTools, LongbowTraining, ForgedBlades, ReinforcedPlating }
    public enum Zone { BaseCenter, NorthGate, SouthGate, EastGate, WestGate, FarmArea, Forest, GoldMine }
    public enum Phase { EarlyEconomy, BuildUp, UnderAttack, Skirmish, LateGame }
    public enum EventKind { UnderAttack, UnitLost, BuildingLost, UnitTrained, BuildingCompleted, TechResearched }
    public enum DamageType { Melee, Pierce, Siege }
    public enum EntityKind { Unit, Building, ResourceNode }
    public enum OrderType { None, Move, Attack, Gather }
    public enum ActionType { TrainUnit, Build, MoveGroup, Research }

    /// <summary>2D position on the map: X = east, Y = north (Unity Z).</summary>
    public struct Vec2
    {
        public float X, Y;
        public Vec2(float x, float y) { X = x; Y = y; }
        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.X + b.X, a.Y + b.Y);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.X - b.X, a.Y - b.Y);
        public static Vec2 operator *(Vec2 a, float s) => new Vec2(a.X * s, a.Y * s);
        public float Length => (float)Math.Sqrt(X * X + Y * Y);
        public static float Distance(Vec2 a, Vec2 b) => (a - b).Length;
        public override string ToString() => $"({X:0.#}, {Y:0.#})";
    }

    /// <summary>Wood/Food/Gold amounts (immutable value).</summary>
    public readonly struct Cost
    {
        public readonly int Wood, Food, Gold;
        public Cost(int wood = 0, int food = 0, int gold = 0) { Wood = wood; Food = food; Gold = gold; }
        public static readonly Cost Zero = new Cost();
        public static Cost operator +(Cost a, Cost b) => new Cost(a.Wood + b.Wood, a.Food + b.Food, a.Gold + b.Gold);
        public static Cost operator -(Cost a, Cost b) => new Cost(a.Wood - b.Wood, a.Food - b.Food, a.Gold - b.Gold);
        public static Cost operator *(Cost a, int n) => new Cost(a.Wood * n, a.Food * n, a.Gold * n);
        public int Get(ResourceType t) => t == ResourceType.Wood ? Wood : t == ResourceType.Food ? Food : Gold;
        public bool IsZero => Wood == 0 && Food == 0 && Gold == 0;
        public bool CoveredBy(Cost have) => Wood <= have.Wood && Food <= have.Food && Gold <= have.Gold;
        public override string ToString() => $"{Wood}w/{Food}f/{Gold}g";
    }

    /// <summary>Result of an authoritative command or validation step.</summary>
    public readonly struct CommandResult
    {
        public readonly bool Ok;
        public readonly string Reason;
        public CommandResult(bool ok, string reason) { Ok = ok; Reason = reason; }
        public static CommandResult Success => new CommandResult(true, null);
        public static CommandResult Fail(string reason) => new CommandResult(false, reason);
        public override string ToString() => Ok ? "ok" : Reason;
    }

    /// <summary>String names shared with the Python side (MCP server / ActionPlan schema).</summary>
    public static class WireNames
    {
        static readonly string[] Techs =
        {
            "tech_age2_iron_age_v1", "tech_sharpened_tools_v1", "tech_longbow_training_v1",
            "tech_forged_blades_v1", "tech_reinforced_plating_v1",
        };
        static readonly string[] Events = { "UNDER_ATTACK", "UNIT_LOST", "BUILDING_LOST", "UNIT_TRAINED", "BUILDING_COMPLETED", "TECH_RESEARCHED" };
        static readonly string[] Actions = { "TRAIN_UNIT", "BUILD", "MOVE_GROUP", "RESEARCH" };

        public static string Wire(TechId t) => Techs[(int)t];
        public static string Wire(EventKind k) => Events[(int)k];
        public static string Wire(ActionType a) => Actions[(int)a];

        public static bool TryTech(string s, out TechId t) => TryIndex(Techs, s, out int i) & Set(i, out t);
        public static bool TryAction(string s, out ActionType a) => TryIndex(Actions, s, out int i) & Set(i, out a);
        public static bool TryUnit(string s, out UnitType u) => TryEnum(s, out u);
        public static bool TryBuilding(string s, out BuildingType b) => TryEnum(s, out b);
        public static bool TryZone(string s, out Zone z) => TryEnum(s, out z);

        static bool TryIndex(string[] table, string s, out int index)
        {
            index = Array.IndexOf(table, s);
            return index >= 0;
        }
        static bool Set(int i, out TechId t) { t = i >= 0 ? (TechId)i : default; return i >= 0; }
        static bool Set(int i, out ActionType a) { a = i >= 0 ? (ActionType)i : default; return i >= 0; }

        // Exact (case-sensitive) names only; numeric strings are rejected so "3" never parses as an enum.
        static bool TryEnum<T>(string s, out T value) where T : struct, Enum
        {
            value = default;
            if (string.IsNullOrEmpty(s) || char.IsDigit(s[0]) || s[0] == '-') return false;
            return Enum.TryParse(s, false, out value) && Enum.IsDefined(typeof(T), value) && value.ToString() == s;
        }
    }
}
