using System;
using System.Collections.Generic;
using Rts.Core;
using Rts.Core.Net;

namespace Rts.Game
{
    public enum NetRole { Local, Host, Client }

    /// <summary>
    /// Everything the UI can ask the game to do. The UI never touches the simulation directly:
    /// Local/Host roles execute on the authoritative MatchSimulation, the Client role sends the intent to the host.
    /// </summary>
    public interface ICommandSink
    {
        CommandResult Train(UnitType unit, int count, int buildingId);
        CommandResult Build(BuildingType building, Vec2 site, int builderId);
        CommandResult Research(TechId tech);
        CommandResult Move(IEnumerable<int> ids, Vec2 target);
        CommandResult Attack(IEnumerable<int> ids, int targetId);
        CommandResult Gather(IEnumerable<int> ids, int nodeId);
        void Stop(IEnumerable<int> ids);
        /// <summary>The player pressed APPROVE. `planJson` is the exact text that produced `review`.</summary>
        ExecutionReport ApprovePlan(PlanReview review, string planJson);
        void RequestSnapshot(Action<string> onJson);
    }

    /// <summary>Local and Host roles: the same authoritative methods that HostAuthority calls for remote players.</summary>
    public sealed class LocalCommandSink : ICommandSink
    {
        readonly MatchSimulation _sim;
        readonly Func<int> _player;
        public LocalCommandSink(MatchSimulation sim, Func<int> localPlayer) { _sim = sim; _player = localPlayer; }

        public CommandResult Train(UnitType unit, int count, int buildingId) => _sim.TryTrain(_player(), unit, count, buildingId);
        public CommandResult Build(BuildingType building, Vec2 site, int builderId) => _sim.TryBuild(_player(), building, site, builderId);
        public CommandResult Research(TechId tech) => _sim.TryResearch(_player(), tech);
        public CommandResult Move(IEnumerable<int> ids, Vec2 target) => _sim.OrderMove(_player(), ids, target);
        public CommandResult Attack(IEnumerable<int> ids, int targetId) => _sim.OrderAttack(_player(), ids, targetId);
        public CommandResult Gather(IEnumerable<int> ids, int nodeId) => _sim.OrderGather(_player(), ids, nodeId);
        public void Stop(IEnumerable<int> ids) => _sim.OrderStop(_player(), ids);
        public ExecutionReport ApprovePlan(PlanReview review, string planJson) => review.Approve(_sim, _player());
        public void RequestSnapshot(Action<string> onJson) =>
            onJson(_sim.BuildSnapshot(_player(), DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0).ToJson());
    }

    /// <summary>Client role: intents become command messages for the host; results come back as notices.</summary>
    public sealed class ClientCommandSink : ICommandSink
    {
        readonly ClientSession _session;
        public ClientCommandSink(ClientSession session) { _session = session; }

        CommandResult Send(NetCommand c) { _session.Send(c); return CommandResult.Success; }

        public CommandResult Train(UnitType unit, int count, int buildingId) => Send(NetCommand.Train(unit, count, buildingId));
        public CommandResult Build(BuildingType building, Vec2 site, int builderId) => Send(NetCommand.Build(building, site, builderId));
        public CommandResult Research(TechId tech) => Send(NetCommand.Research(tech));
        public CommandResult Move(IEnumerable<int> ids, Vec2 target) => Send(NetCommand.Move(ids, target));
        public CommandResult Attack(IEnumerable<int> ids, int targetId) => Send(NetCommand.Attack(ids, targetId));
        public CommandResult Gather(IEnumerable<int> ids, int nodeId) => Send(NetCommand.Gather(ids, nodeId));
        public void Stop(IEnumerable<int> ids) => _session.Send(NetCommand.Stop(ids));

        public ExecutionReport ApprovePlan(PlanReview review, string planJson)
        {
            var report = new ExecutionReport();
            if (!review.MarkSubmittedToHost()) { report.Rejected = 1; report.Lines.Add("plan is not pending"); return report; }
            _session.Send(NetCommand.Approve(planJson));
            report.Lines.Add("Plan sent to the host for validation and execution; the result arrives as a notice.");
            return report;
        }

        public void RequestSnapshot(Action<string> onJson) => _session.RequestSnapshot(onJson);
    }
}
