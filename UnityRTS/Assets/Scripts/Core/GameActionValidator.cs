using System;
using System.Collections.Generic;

namespace Rts.Core
{
    public sealed class ActionCheck
    {
        public int Index;
        public AiAction Action;
        public bool Ok;
        public string Reason;
        public Cost Cost;
    }

    public sealed class PlanValidation
    {
        public List<string> PlanErrors = new List<string>();
        public List<ActionCheck> Checks = new List<ActionCheck>();
        public bool AllValid
        {
            get
            {
                if (PlanErrors.Count > 0) return false;
                foreach (var c in Checks) if (!c.Ok) return false;
                return true;
            }
        }
        public int ValidCount { get { int n = 0; foreach (var c in Checks) if (c.Ok) n++; return n; } }
        public Cost TotalCost { get { Cost t = Cost.Zero; foreach (var c in Checks) if (c.Ok) t = t + c.Cost; return t; } }
    }

    /// <summary>Facts about the live match the validator needs besides the requesting player's PlayerState.</summary>
    public sealed class ValidationContext
    {
        public bool MatchEnded;
        public int OwnMilitaryUnits;

        public static ValidationContext From(MatchSimulation sim, int playerId)
        {
            int military = 0;
            foreach (var e in sim.Entities)
                if (e.Alive && e.OwnerId == playerId && e.IsMilitaryUnit) military++;
            return new ValidationContext { MatchEnded = sim.Result.Ended, OwnMilitaryUnits = military };
        }
    }

    /// <summary>
    /// Authoritative gameplay validation of an (already approved) AI plan. It runs again at approval time
    /// because resources and combat may have changed since the plan was proposed. Actions are checked in order
    /// against a simulated copy of the player's state, so costs and population accumulate across the plan.
    /// Buildings queued by the same plan do NOT satisfy prerequisites of later actions (they are not built yet).
    /// The validator only reads the requesting player's own state: no other player's data is involved.
    /// </summary>
    public static class GameActionValidator
    {
        public static PlanValidation Validate(AiActionPlan plan, PlayerState player, ValidationContext ctx)
        {
            var result = new PlanValidation();
            if (plan == null || player == null || ctx == null) { result.PlanErrors.Add("missing plan or state"); return result; }
            if (ctx.MatchEnded) result.PlanErrors.Add("match has ended");
            if (plan.Actions == null) { result.PlanErrors.Add("plan has no action list"); return result; }
            if (plan.Actions.Count > ActionPlanParser.MaxActions) result.PlanErrors.Add($"too many actions (max {ActionPlanParser.MaxActions})");
            if (double.IsNaN(plan.Confidence) || plan.Confidence < 0 || plan.Confidence > 1) result.PlanErrors.Add("confidence out of range");
            if (result.PlanErrors.Count > 0) return result;

            var sim = player.Clone();
            for (int i = 0; i < plan.Actions.Count; i++)
            {
                var check = CheckOne(plan.Actions[i], sim, ctx);
                check.Index = i;
                check.Action = plan.Actions[i];
                result.Checks.Add(check);
            }
            return result;
        }

        static ActionCheck Fail(string reason) => new ActionCheck { Ok = false, Reason = reason };

        /// <summary>Checks one action against `sim` (a scratch copy) and, if valid, applies its effects to it.</summary>
        static ActionCheck CheckOne(AiAction a, PlayerState sim, ValidationContext ctx)
        {
            if (a == null) return Fail("null action");
            if (!Enum.IsDefined(typeof(ActionType), a.Type)) return Fail("unknown action type");
            switch (a.Type)
            {
                case ActionType.TrainUnit:
                {
                    if (!a.Unit.HasValue || !Enum.IsDefined(typeof(UnitType), a.Unit.Value)) return Fail("unknown unit type");
                    if (!a.Count.HasValue || a.Count.Value < 1 || a.Count.Value > 20) return Fail("invalid count (1..20)");
                    var d = GameData.Units[a.Unit.Value];
                    int n = a.Count.Value;
                    if (d.RequiredAge > sim.Age) return Fail($"{d.Type} requires Age {d.RequiredAge}");
                    if (!sim.HasBuilding(d.TrainedAt)) return Fail($"required building missing: {d.TrainedAt}");
                    var cost = d.Cost * n;
                    if (!sim.CanAfford(cost)) return Fail($"insufficient resources (need {cost}, have {sim.Resources})");
                    if (sim.PopulationUsed + d.Pop * n > sim.PopulationCap)
                        return Fail($"population cap exceeded ({sim.PopulationUsed}+{d.Pop * n} > {sim.PopulationCap})");
                    sim.Spend(cost);
                    sim.PopulationUsed += d.Pop * n;
                    return new ActionCheck { Ok = true, Cost = cost };
                }
                case ActionType.Build:
                {
                    if (!a.Building.HasValue || !Enum.IsDefined(typeof(BuildingType), a.Building.Value)) return Fail("unknown building type");
                    var d = GameData.Buildings[a.Building.Value];
                    if (!d.Buildable) return Fail($"{d.Type} cannot be built");
                    if (d.RequiredAge > sim.Age) return Fail($"{d.Type} requires Age {d.RequiredAge}");
                    if (!sim.CanAfford(d.Cost)) return Fail($"insufficient resources (need {d.Cost}, have {sim.Resources})");
                    sim.Spend(d.Cost);
                    return new ActionCheck { Ok = true, Cost = d.Cost };
                }
                case ActionType.Research:
                {
                    if (!a.Tech.HasValue || !Enum.IsDefined(typeof(TechId), a.Tech.Value)) return Fail("unknown technology");
                    var d = GameData.Techs[a.Tech.Value];
                    if (sim.Has(d.Id)) return Fail("already researched");
                    if (sim.Researching.Contains(d.Id)) return Fail("already being researched");
                    if (d.RequiresAge2 && sim.Age < 2) return Fail("requires Age II");
                    foreach (var b in d.RequiresAll) if (!sim.HasBuilding(b)) return Fail($"required building missing: {b}");
                    if (d.RequiresAny.Length > 0)
                    {
                        bool any = false;
                        foreach (var b in d.RequiresAny) any |= sim.HasBuilding(b);
                        if (!any) return Fail("required building missing: " + string.Join(" or ", d.RequiresAny));
                    }
                    int busy = 0;
                    foreach (var t in sim.Researching) if (GameData.Techs[t].ResearchedAt == d.ResearchedAt) busy++;
                    if (sim.CountCompleted(d.ResearchedAt) - busy <= 0) return Fail($"no free {d.ResearchedAt} to research in");
                    if (!sim.CanAfford(d.Cost)) return Fail($"insufficient resources (need {d.Cost}, have {sim.Resources})");
                    sim.Spend(d.Cost);
                    sim.Researching.Add(d.Id);
                    return new ActionCheck { Ok = true, Cost = d.Cost };
                }
                case ActionType.MoveGroup:
                {
                    if (!a.TargetZone.HasValue || !Enum.IsDefined(typeof(Zone), a.TargetZone.Value)) return Fail("unknown target zone");
                    if (ctx.OwnMilitaryUnits < 1) return Fail("no military units to move");
                    return new ActionCheck { Ok = true, Cost = Cost.Zero };
                }
            }
            return Fail("unknown action type");
        }
    }

    public sealed class ExecutionReport
    {
        public List<string> Lines = new List<string>();
        public int Executed;
        public int Rejected;
    }

    /// <summary>
    /// Converts an APPROVED plan into authoritative commands. It re-validates against the current live state
    /// (never trusting an earlier validation) and then calls exactly the same MatchSimulation methods a human
    /// player's UI uses. Rejected/invalid actions change nothing.
    /// </summary>
    public static class ApprovedPlanExecutor
    {
        public static ExecutionReport Execute(MatchSimulation sim, int playerId, AiActionPlan plan)
        {
            var report = new ExecutionReport();
            if (!sim.Players.TryGetValue(playerId, out var player)) { report.Lines.Add("unknown player"); report.Rejected++; return report; }
            var validation = GameActionValidator.Validate(plan, player, ValidationContext.From(sim, playerId));
            if (validation.PlanErrors.Count > 0)
            {
                foreach (var e in validation.PlanErrors) report.Lines.Add("plan rejected: " + e);
                report.Rejected++;
                return report;
            }
            foreach (var check in validation.Checks)
            {
                if (!check.Ok) { report.Rejected++; report.Lines.Add($"#{check.Index} {check.Action}: REJECTED ({check.Reason})"); continue; }
                var res = Run(sim, playerId, check.Action);
                if (res.Ok) { report.Executed++; report.Lines.Add($"#{check.Index} {check.Action}: executed"); }
                else { report.Rejected++; report.Lines.Add($"#{check.Index} {check.Action}: FAILED ({res.Reason})"); }
            }
            return report;
        }

        static CommandResult Run(MatchSimulation sim, int playerId, AiAction a)
        {
            switch (a.Type)
            {
                case ActionType.TrainUnit: return sim.TryTrain(playerId, a.Unit.Value, a.Count.Value);
                case ActionType.Research: return sim.TryResearch(playerId, a.Tech.Value);
                case ActionType.Build:
                {
                    var site = sim.FindBuildSite(playerId);
                    if (!site.HasValue) return CommandResult.Fail("no free build site");
                    return sim.TryBuild(playerId, a.Building.Value, site.Value, NearestWorker(sim, playerId, site.Value));
                }
                case ActionType.MoveGroup:
                {
                    var citadel = sim.FindCitadel(playerId);
                    var anchor = citadel != null ? citadel.Pos : default;
                    var ids = new List<int>();
                    foreach (var e in sim.Entities) if (e.Alive && e.OwnerId == playerId && e.IsMilitaryUnit) ids.Add(e.Id);
                    var nodes = new List<Entity>();
                    foreach (var e in sim.Entities) if (e.Kind == EntityKind.ResourceNode) nodes.Add(e);
                    return sim.OrderMove(playerId, ids, ZoneMap.Center(a.TargetZone.Value, anchor, nodes));
                }
            }
            return CommandResult.Fail("unsupported action");
        }

        static int NearestWorker(MatchSimulation sim, int playerId, Vec2 site)
        {
            Entity best = null;
            foreach (var e in sim.Entities)
            {
                if (!e.Alive || e.OwnerId != playerId || e.Kind != EntityKind.Unit || e.Unit != UnitType.Worker) continue;
                bool idle = e.Order == OrderType.None;
                if (best == null || (idle && best.Order != OrderType.None) || (idle == (best.Order == OrderType.None) && Vec2.Distance(e.Pos, site) < Vec2.Distance(best.Pos, site))) best = e;
            }
            return best != null ? best.Id : 0;
        }
    }

    /// <summary>Creates the demo match: two players, two bases mirrored across the map, resource nodes, starting workers.</summary>
    public static class MapSetup
    {
        public static readonly Vec2 Player1Base = new Vec2(-60, -45);
        public static readonly Vec2 Player2Base = new Vec2(60, 45);

        public static MatchSimulation CreateDemoMatch()
        {
            var sim = new MatchSimulation();
            Setup(sim, 1, Player1Base, +1);
            Setup(sim, 2, Player2Base, -1);
            return sim;
        }

        static void Setup(MatchSimulation sim, int id, Vec2 b, int s)
        {
            sim.AddPlayer(id);
            sim.SpawnBuilding(id, BuildingType.Citadel, b);
            for (int i = 0; i < GameData.StartingWorkers; i++) sim.SpawnUnit(id, UnitType.Worker, b + new Vec2(s * (4 + i * 2), -s * 5));
            sim.SpawnNode(ResourceType.Wood, b + new Vec2(-s * 16, s * 10), 1500);
            sim.SpawnNode(ResourceType.Wood, b + new Vec2(-s * 20, s * 4), 1500);
            sim.SpawnNode(ResourceType.Food, b + new Vec2(s * 12, -s * 14), 1000);
            sim.SpawnNode(ResourceType.Food, b + new Vec2(s * 18, -s * 10), 1000);
            sim.SpawnNode(ResourceType.Gold, b + new Vec2(s * 20, s * 8), 1500);
        }
    }
}
