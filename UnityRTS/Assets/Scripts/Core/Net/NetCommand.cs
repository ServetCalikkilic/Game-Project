using System;
using System.Collections.Generic;

namespace Rts.Core.Net
{
    public enum CommandType : byte { Train = 1, Build = 2, Research = 3, Move = 4, Attack = 5, Gather = 6, Stop = 7, ApprovePlan = 8, RequestSnapshot = 9 }

    /// <summary>
    /// A request from a client to the host. There is deliberately NO player id field: the host derives the sender's
    /// identity from the network connection, so a client cannot issue commands for another player.
    /// </summary>
    public sealed class NetCommand
    {
        public CommandType Type;
        public UnitType Unit;
        public BuildingType Building;
        public TechId Tech;
        public int Count, BuildingId, TargetId;
        public float X, Y;
        public List<int> Ids = new List<int>();
        public string PlanJson;

        public static NetCommand Train(UnitType u, int count, int buildingId = 0) => new NetCommand { Type = CommandType.Train, Unit = u, Count = count, BuildingId = buildingId };
        public static NetCommand Build(BuildingType b, Vec2 pos, int builderId) => new NetCommand { Type = CommandType.Build, Building = b, X = pos.X, Y = pos.Y, TargetId = builderId };
        public static NetCommand Research(TechId t) => new NetCommand { Type = CommandType.Research, Tech = t };
        public static NetCommand Move(IEnumerable<int> ids, Vec2 target) => With(CommandType.Move, ids, 0, target);
        public static NetCommand Attack(IEnumerable<int> ids, int target) => With(CommandType.Attack, ids, target, default);
        public static NetCommand Gather(IEnumerable<int> ids, int node) => With(CommandType.Gather, ids, node, default);
        public static NetCommand Stop(IEnumerable<int> ids) => With(CommandType.Stop, ids, 0, default);
        public static NetCommand Approve(string planJson) => new NetCommand { Type = CommandType.ApprovePlan, PlanJson = planJson };
        public static NetCommand Snapshot() => new NetCommand { Type = CommandType.RequestSnapshot };

        static NetCommand With(CommandType t, IEnumerable<int> ids, int target, Vec2 pos)
        {
            var c = new NetCommand { Type = t, TargetId = target, X = pos.X, Y = pos.Y };
            foreach (int id in ids) { if (c.Ids.Count >= CommandCodec.MaxIds) break; c.Ids.Add(id); }
            return c;
        }
    }

    public static class CommandCodec
    {
        public const int MaxIds = 64;
        public const int MaxPlanBytes = 16 * 1024;
        public const float MaxCoord = 1000f;

        public static byte[] Encode(NetCommand c)
        {
            var w = new ByteWriter().U8((int)c.Type);
            switch (c.Type)
            {
                case CommandType.Train: w.U8((int)c.Unit).U8(c.Count).I32(c.BuildingId); break;
                case CommandType.Build: w.U8((int)c.Building).F32(c.X).F32(c.Y).I32(c.TargetId); break;
                case CommandType.Research: w.U8((int)c.Tech); break;
                case CommandType.Move: Ids(w, c).F32(c.X).F32(c.Y); break;
                case CommandType.Attack: case CommandType.Gather: Ids(w, c).I32(c.TargetId); break;
                case CommandType.Stop: Ids(w, c); break;
                case CommandType.ApprovePlan: w.Str(c.PlanJson, MaxPlanBytes); break;
                case CommandType.RequestSnapshot: break;
                default: throw new ArgumentException("unknown command type");
            }
            return w.ToArray();
        }

        static ByteWriter Ids(ByteWriter w, NetCommand c)
        {
            if (c.Ids.Count > MaxIds) throw new ArgumentException("too many ids");
            w.U8(c.Ids.Count);
            foreach (int id in c.Ids) w.I32(id);
            return w;
        }

        /// <summary>Decodes untrusted bytes. Never throws: any malformed input is rejected with an error string.</summary>
        public static bool TryDecode(byte[] data, out NetCommand cmd, out string error)
        {
            cmd = null; error = null;
            try
            {
                var r = new ByteReader(data);
                int t = r.U8();
                if (!Enum.IsDefined(typeof(CommandType), (byte)t)) throw new FormatException("unknown command type");
                var c = new NetCommand { Type = (CommandType)t };
                switch (c.Type)
                {
                    case CommandType.Train:
                        c.Unit = E<UnitType>(r.U8()); c.Count = r.U8(); c.BuildingId = r.I32();
                        if (c.Count < 1 || c.Count > 20) throw new FormatException("count must be 1..20");
                        break;
                    case CommandType.Build:
                        c.Building = E<BuildingType>(r.U8()); c.X = Coord(r.F32()); c.Y = Coord(r.F32()); c.TargetId = r.I32();
                        break;
                    case CommandType.Research: c.Tech = E<TechId>(r.U8()); break;
                    case CommandType.Move: ReadIds(r, c); c.X = Coord(r.F32()); c.Y = Coord(r.F32()); break;
                    case CommandType.Attack: case CommandType.Gather: ReadIds(r, c); c.TargetId = r.I32(); break;
                    case CommandType.Stop: ReadIds(r, c); break;
                    case CommandType.ApprovePlan: c.PlanJson = r.Str(MaxPlanBytes); break;
                }
                r.ExpectEnd();
                cmd = c;
                return true;
            }
            catch (FormatException ex) { error = ex.Message; return false; }
        }

        static void ReadIds(ByteReader r, NetCommand c)
        {
            int n = r.U8();
            if (n < 1 || n > MaxIds) throw new FormatException("id count must be 1.." + MaxIds);
            for (int i = 0; i < n; i++) c.Ids.Add(r.I32());
        }

        static float Coord(float f) { if (Math.Abs(f) > MaxCoord) throw new FormatException("coordinate out of range"); return f; }

        static T E<T>(int v) where T : struct
        {
            if (!Enum.IsDefined(typeof(T), v)) throw new FormatException("invalid " + typeof(T).Name);
            return (T)Enum.ToObject(typeof(T), v);
        }
    }
}
