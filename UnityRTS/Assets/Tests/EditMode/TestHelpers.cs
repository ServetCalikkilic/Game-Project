using Rts.Core;

namespace Rts.Tests
{
    public static class T
    {
        public static MatchSimulation Match() => MapSetup.CreateDemoMatch();

        public static void Run(MatchSimulation sim, float seconds, float dt = 0.1f)
        {
            for (float t = 0; t < seconds; t += dt) sim.Tick(dt);
        }

        public static Entity First(MatchSimulation sim, int owner, UnitType u)
        {
            foreach (var e in sim.Entities) if (e.Alive && e.OwnerId == owner && e.Kind == EntityKind.Unit && e.Unit == u) return e;
            return null;
        }

        public static Entity Node(MatchSimulation sim, ResourceType r, int nearOwner)
        {
            var c = sim.FindCitadel(nearOwner);
            Entity best = null;
            foreach (var e in sim.Entities)
                if (e.Kind == EntityKind.ResourceNode && e.NodeResource == r && (best == null || Vec2.Distance(e.Pos, c.Pos) < Vec2.Distance(best.Pos, c.Pos))) best = e;
            return best;
        }

        public static int Count(MatchSimulation sim, int owner, UnitType u)
        {
            int n = 0;
            foreach (var e in sim.Entities) if (e.Alive && e.OwnerId == owner && e.Kind == EntityKind.Unit && e.Unit == u) n++;
            return n;
        }

        public static Cost Give(MatchSimulation sim, int player, int wood = 0, int food = 0, int gold = 0)
        {
            var p = sim.Players[player]; p.Wood += wood; p.Food += food; p.Gold += gold; return p.Resources;
        }

        public static AiActionPlan Plan(params AiAction[] actions)
        {
            var plan = new AiActionPlan { Summary = "test", Confidence = 0.8 };
            plan.Actions.AddRange(actions);
            return plan;
        }

        public static AiAction Train(UnitType u, int n) => new AiAction { Type = ActionType.TrainUnit, Unit = u, Count = n, Reason = "t" };
        public static AiAction Build(BuildingType b) => new AiAction { Type = ActionType.Build, Building = b, Reason = "t" };
        public static AiAction Research(TechId t) => new AiAction { Type = ActionType.Research, Tech = t, Reason = "t" };
        public static AiAction Move(Zone z) => new AiAction { Type = ActionType.MoveGroup, TargetZone = z, Reason = "t" };
    }
}
