using System;
using System.Collections.Generic;

namespace Rts.Core.Net
{
    public struct HostReply
    {
        public bool Ok;
        public string Message;        // shown to the requesting player
        public string SnapshotJson;   // set for RequestSnapshot
    }

    /// <summary>
    /// The host's command gate. Every client request ends up in the SAME authoritative methods a local player's UI uses
    /// (MatchSimulation.Try*/Order*, and PlanReview/GameActionValidator for AI plans). The sender's player id comes from
    /// the transport (never from the message), commands are decoded defensively, and each player is rate limited.
    /// </summary>
    public sealed class HostAuthority
    {
        public const float MaxBuildDistanceFromCitadel = 60f;

        readonly MatchSimulation _sim;
        readonly Func<double> _epoch;
        readonly Dictionary<int, int> _count = new Dictionary<int, int>();
        float _windowStart;
        public int MaxCommandsPerSecond = 60;
        public readonly List<string> Log = new List<string>();   // rejected commands (type + reason), no payloads

        public HostAuthority(MatchSimulation sim, Func<double> epochSeconds = null)
        {
            if (sim.IsReplica) throw new ArgumentException("the host must own the real simulation, not a replica");
            _sim = sim;
            _epoch = epochSeconds ?? (() => (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds);
        }

        public HostReply Handle(int senderPlayerId, byte[] data)
        {
            if (!_sim.Players.ContainsKey(senderPlayerId)) return Reject("unknown sender");
            if (!AllowRate(senderPlayerId)) return Reject("rate limit exceeded");
            if (!CommandCodec.TryDecode(data, out var c, out string err)) return Reject("malformed command: " + err);

            switch (c.Type)
            {
                case CommandType.Train: return From(_sim.TryTrain(senderPlayerId, c.Unit, c.Count, c.BuildingId), c.Type);
                case CommandType.Research: return From(_sim.TryResearch(senderPlayerId, c.Tech), c.Type);
                case CommandType.Move: return From(_sim.OrderMove(senderPlayerId, c.Ids, new Vec2(c.X, c.Y)), c.Type);
                case CommandType.Attack: return From(_sim.OrderAttack(senderPlayerId, c.Ids, c.TargetId), c.Type);
                case CommandType.Gather: return From(_sim.OrderGather(senderPlayerId, c.Ids, c.TargetId), c.Type);
                case CommandType.Stop: _sim.OrderStop(senderPlayerId, c.Ids); return new HostReply { Ok = true };
                case CommandType.Build:
                {
                    var citadel = _sim.FindCitadel(senderPlayerId);
                    var pos = new Vec2(c.X, c.Y);
                    if (citadel == null || Vec2.Distance(citadel.Pos, pos) > MaxBuildDistanceFromCitadel)
                        return Reject(c.Type + ": build site too far from your Citadel");
                    return From(_sim.TryBuild(senderPlayerId, c.Building, pos, c.TargetId), c.Type);
                }
                case CommandType.ApprovePlan:
                {
                    // Human approval happened on the client. The host re-validates against ITS state: that is the real authority.
                    var review = PlanReview.FromJson(c.PlanJson, _sim, senderPlayerId);
                    if (review.Status == ReviewStatus.Invalid) return Reject("plan rejected: " + string.Join("; ", review.ParseErrors));
                    var report = review.Approve(_sim, senderPlayerId);
                    return new HostReply { Ok = report.Executed > 0, Message = $"AI plan: {report.Executed} executed, {report.Rejected} rejected\n" + string.Join("\n", report.Lines) };
                }
                case CommandType.RequestSnapshot:
                    return new HostReply { Ok = true, SnapshotJson = _sim.BuildSnapshot(senderPlayerId, _epoch()).ToJson() };
            }
            return Reject("unsupported command");
        }

        HostReply From(CommandResult r, CommandType t)
        {
            if (r.Ok) return new HostReply { Ok = true };
            Log.Add(t + ": " + r.Reason);
            return new HostReply { Ok = false, Message = r.Reason };
        }

        HostReply Reject(string reason)
        {
            if (Log.Count < 500) Log.Add(reason);
            return new HostReply { Ok = false, Message = reason };
        }

        bool AllowRate(int player)
        {
            float now = _sim.Time;
            if (now - _windowStart >= 1f || now < _windowStart) { _windowStart = now; _count.Clear(); }
            _count.TryGetValue(player, out int n);
            _count[player] = n + 1;
            return n < MaxCommandsPerSecond;
        }
    }
}
