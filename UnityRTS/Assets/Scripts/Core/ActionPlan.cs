using System;
using System.Collections.Generic;

namespace Rts.Core
{
    /// <summary>One proposed action. Mirrors schemas/action_plan.schema.json.</summary>
    public sealed class AiAction
    {
        public ActionType Type;
        public UnitType? Unit;
        public BuildingType? Building;
        public TechId? Tech;
        public int? Count;
        public Zone? TargetZone;
        public string Reason = "";

        public override string ToString()
        {
            switch (Type)
            {
                case ActionType.TrainUnit: return $"Train {Count} {Unit}";
                case ActionType.Build: return $"Build {Building}";
                case ActionType.MoveGroup: return $"Move army to {TargetZone}";
                default: return $"Research {Tech}";
            }
        }
    }

    public sealed class AiActionPlan
    {
        public string Summary = "";
        public double Confidence;
        public string Limitations;
        public List<string> Evidence = new List<string>();
        public List<AiAction> Actions = new List<AiAction>();
    }

    public sealed class PlanParseResult
    {
        public AiActionPlan Plan;                       // null when Errors is non-empty
        public List<string> Errors = new List<string>();
        public bool Ok => Errors.Count == 0 && Plan != null;
    }

    /// <summary>
    /// Strict parser for the ActionPlan JSON produced by the AI gateway. This is the C# counterpart of the
    /// schema check in agent/plan_validation.py: unknown fields, wrong types, unknown enum values, missing or
    /// forbidden per-action fields and out-of-range numbers are all errors. A parsed plan is still only a
    /// proposal: GameActionValidator checks it against the live game state.
    /// </summary>
    public static class ActionPlanParser
    {
        public const int MaxActions = 8;
        static readonly HashSet<string> TopKeys = new HashSet<string> { "summary", "confidence", "limitations", "evidence", "actions" };
        static readonly HashSet<string> ActionKeys = new HashSet<string> { "action_type", "unit_type", "building_type", "tech_id", "count", "target_zone", "reason" };

        public static PlanParseResult Parse(string json)
        {
            var r = new PlanParseResult();
            object root;
            try { root = MiniJson.Parse(json); }
            catch (MiniJson.JsonException ex) { r.Errors.Add("invalid JSON: " + ex.Message); return r; }
            if (!(root is Dictionary<string, object> top)) { r.Errors.Add("plan must be a JSON object"); return r; }

            foreach (var k in top.Keys) if (!TopKeys.Contains(k)) r.Errors.Add($"unknown field '{k}'");
            var plan = new AiActionPlan();

            if (Str(top, "summary", 1, 400, true, r.Errors, out string summary)) plan.Summary = summary;
            if (!top.TryGetValue("confidence", out var conf) || !(conf is double c) || c < 0 || c > 1) r.Errors.Add("confidence must be a number between 0 and 1");
            else plan.Confidence = c;
            if (top.ContainsKey("limitations") && Str(top, "limitations", 0, 250, false, r.Errors, out string lim)) plan.Limitations = lim;

            if (top.TryGetValue("evidence", out var ev))
            {
                if (!(ev is List<object> el) || el.Count > 10) r.Errors.Add("evidence must be an array of at most 10 ids");
                else foreach (var item in el)
                    {
                        if (item is string s && IsSourceId(s) && !plan.Evidence.Contains(s)) plan.Evidence.Add(s);
                        else r.Errors.Add("evidence contains an invalid or duplicate source id");
                    }
            }

            if (!top.TryGetValue("actions", out var act) || !(act is List<object> list)) r.Errors.Add("actions must be an array");
            else if (list.Count > MaxActions) r.Errors.Add($"too many actions (max {MaxActions})");
            else for (int i = 0; i < list.Count; i++)
                {
                    var a = ParseAction(list[i], i, r.Errors);
                    if (a != null) plan.Actions.Add(a);
                }

            if (r.Errors.Count == 0) r.Plan = plan;
            return r;
        }

        static AiAction ParseAction(object node, int i, List<string> errors)
        {
            string p = $"actions[{i}]";
            if (!(node is Dictionary<string, object> d)) { errors.Add($"{p}: must be an object"); return null; }
            int before = errors.Count;
            foreach (var k in d.Keys) if (!ActionKeys.Contains(k)) errors.Add($"{p}: unknown field '{k}'");

            var a = new AiAction();
            if (!d.TryGetValue("action_type", out var at) || !(at is string ats) || !WireNames.TryAction(ats, out a.Type))
            { errors.Add($"{p}: unknown action_type"); return null; }
            if (Str(d, "reason", 1, 250, true, errors, out string reason)) a.Reason = reason;

            if (d.ContainsKey("unit_type")) { if (d["unit_type"] is string s && WireNames.TryUnit(s, out var u)) a.Unit = u; else errors.Add($"{p}: unknown unit_type"); }
            if (d.ContainsKey("building_type"))
            {
                if (d["building_type"] is string s && WireNames.TryBuilding(s, out var b) && GameData.Buildings[b].Buildable) a.Building = b;
                else errors.Add($"{p}: unknown or non-buildable building_type");
            }
            if (d.ContainsKey("tech_id")) { if (d["tech_id"] is string s && WireNames.TryTech(s, out var t)) a.Tech = t; else errors.Add($"{p}: unknown tech_id"); }
            if (d.ContainsKey("target_zone")) { if (d["target_zone"] is string s && WireNames.TryZone(s, out var z)) a.TargetZone = z; else errors.Add($"{p}: unknown target_zone"); }
            if (d.ContainsKey("count"))
            {
                if (d["count"] is double n && n == Math.Floor(n) && n >= 1 && n <= 20) a.Count = (int)n;
                else errors.Add($"{p}: count must be an integer between 1 and 20");
            }

            // Per-type required / forbidden fields (same rules as the schema's allOf/if/then).
            bool u_ = d.ContainsKey("unit_type"), b_ = d.ContainsKey("building_type"), t_ = d.ContainsKey("tech_id"), c_ = d.ContainsKey("count"), z_ = d.ContainsKey("target_zone");
            switch (a.Type)
            {
                case ActionType.TrainUnit:
                    if (!u_ || !c_) errors.Add($"{p}: TRAIN_UNIT requires unit_type and count");
                    if (b_ || t_ || z_) errors.Add($"{p}: TRAIN_UNIT allows only unit_type and count");
                    break;
                case ActionType.Build:
                    if (!b_) errors.Add($"{p}: BUILD requires building_type");
                    if (u_ || t_ || c_) errors.Add($"{p}: BUILD allows only building_type");
                    break;
                case ActionType.MoveGroup:
                    if (!z_) errors.Add($"{p}: MOVE_GROUP requires target_zone");
                    if (u_ || b_ || t_ || c_) errors.Add($"{p}: MOVE_GROUP allows only target_zone");
                    break;
                case ActionType.Research:
                    if (!t_) errors.Add($"{p}: RESEARCH requires tech_id");
                    if (u_ || b_ || c_ || z_) errors.Add($"{p}: RESEARCH allows only tech_id");
                    break;
            }
            return errors.Count == before ? a : null;
        }

        static bool Str(Dictionary<string, object> d, string key, int min, int max, bool required, List<string> errors, out string value)
        {
            value = null;
            if (!d.TryGetValue(key, out var v)) { if (required) errors.Add($"missing '{key}'"); return false; }
            if (!(v is string s) || s.Length < min || s.Length > max) { errors.Add($"{key} must be a string of length {min}..{max}"); return false; }
            value = s;
            return true;
        }

        static bool IsSourceId(string s)
        {
            int us = s.IndexOf('_');
            if (us <= 0 || s.Length > 80) return false;
            string prefix = s.Substring(0, us);
            if (prefix != "unit" && prefix != "bld" && prefix != "tech" && prefix != "rule") return false;
            int v = s.LastIndexOf("_v", StringComparison.Ordinal);
            if (v <= us || v + 2 >= s.Length) return false;
            for (int i = v + 2; i < s.Length; i++) if (!char.IsDigit(s[i])) return false;
            for (int i = 0; i < v; i++) if (!((s[i] >= 'a' && s[i] <= 'z') || (s[i] >= '0' && s[i] <= '9') || s[i] == '_')) return false;
            return true;
        }
    }
}
