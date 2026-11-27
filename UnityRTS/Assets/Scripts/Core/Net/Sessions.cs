using System;
using System.Collections.Generic;
using System.Text;

namespace Rts.Core.Net
{
    /// <summary>Splits large host→client messages into RPC-sized chunks and reassembles them (header: msgId u16, index u8, count u8).</summary>
    public static class Chunker
    {
        public const int HeaderSize = 4;
        public const int DefaultMaxChunk = 400;   // payload bytes per chunk; keeps RPCs well below Fusion's default size limit

        public static List<byte[]> Split(byte[] payload, int msgId, int maxChunk = DefaultMaxChunk)
        {
            int count = Math.Max(1, (payload.Length + maxChunk - 1) / maxChunk);
            if (count > 255) throw new ArgumentException("message too large");
            var list = new List<byte[]>(count);
            for (int i = 0; i < count; i++)
            {
                int off = i * maxChunk, len = Math.Min(maxChunk, payload.Length - off);
                var w = new ByteWriter().U16(msgId).U8(i).U8(count).Raw(payload, off, Math.Max(0, len));
                list.Add(w.ToArray());
            }
            return list;
        }
    }

    public sealed class ChunkAssembler
    {
        sealed class Partial { public byte[][] Parts; public int Got; public int Size; }
        readonly Dictionary<int, Partial> _open = new Dictionary<int, Partial>();
        readonly Queue<int> _doneOrder = new Queue<int>();
        readonly HashSet<int> _done = new HashSet<int>();   // recently completed ids: late duplicates must not re-deliver
        public const int MaxOpenMessages = 8;
        public const int MaxMessageBytes = 96 * 1024;

        /// <summary>Feeds one chunk. Returns the full message when its last chunk arrives, otherwise null. Malformed chunks are ignored.</summary>
        public byte[] Add(byte[] chunk)
        {
            if (chunk == null || chunk.Length < Chunker.HeaderSize) return null;
            int id = chunk[0] | (chunk[1] << 8), index = chunk[2], count = chunk[3];
            if (count == 0 || index >= count || _done.Contains(id)) return null;
            if (!_open.TryGetValue(id, out var p))
            {
                if (_open.Count >= MaxOpenMessages) EvictOldest();
                p = new Partial { Parts = new byte[count][] };
                _open[id] = p;
            }
            if (p.Parts.Length != count || p.Parts[index] != null) return null;
            int len = chunk.Length - Chunker.HeaderSize;
            if (p.Size + len > MaxMessageBytes) { _open.Remove(id); return null; }
            var data = new byte[len];
            Array.Copy(chunk, Chunker.HeaderSize, data, 0, len);
            p.Parts[index] = data; p.Got++; p.Size += len;
            if (p.Got < count) return null;
            _open.Remove(id);
            _done.Add(id); _doneOrder.Enqueue(id);
            if (_doneOrder.Count > 64) _done.Remove(_doneOrder.Dequeue());
            var full = new byte[p.Size];
            int pos = 0;
            foreach (var part in p.Parts) { Array.Copy(part, 0, full, pos, part.Length); pos += part.Length; }
            return full;
        }

        void EvictOldest()
        {
            int oldest = -1;
            foreach (int k in _open.Keys) { oldest = k; break; }
            if (oldest >= 0) _open.Remove(oldest);
        }
    }

    public enum HostMessage : byte { State = 1, Notice = 2, Snapshot = 3 }

    public interface IHostTransport { void SendToPlayer(int playerId, byte[] chunk); }
    public interface IClientTransport { void SendToHost(byte[] data); }

    /// <summary>
    /// Host side (state authority). Owns nothing but the wiring: commands from clients go to HostAuthority; every
    /// ReplicationInterval the host sends each remote player THEIR OWN fog-filtered state (StateCodec.Capture).
    /// The host's own player plays directly on the authoritative simulation.
    /// </summary>
    public sealed class HostSession
    {
        public readonly MatchSimulation Sim;
        public readonly HostAuthority Authority;
        public readonly int HostPlayerId;
        public float ReplicationInterval = 0.2f;
        public int MaxChunk = Chunker.DefaultMaxChunk;

        readonly IHostTransport _transport;
        readonly HashSet<int> _remote = new HashSet<int>();
        float _sinceReplicate;
        int _msgId;
        public int StatesSent { get; private set; }

        public HostSession(MatchSimulation sim, IHostTransport transport, int hostPlayerId = 1, Func<double> epochSeconds = null)
        {
            Sim = sim; HostPlayerId = hostPlayerId; _transport = transport;
            Authority = new HostAuthority(sim, epochSeconds);
        }

        public bool HasRemote(int playerId) => _remote.Contains(playerId);
        public IEnumerable<int> RemotePlayers => _remote;

        public bool AddRemotePlayer(int playerId)
        {
            if (playerId == HostPlayerId || !Sim.Players.ContainsKey(playerId)) return false;
            if (!_remote.Add(playerId)) return false;
            Send(playerId, HostMessage.State, StateCodec.Encode(StateCodec.Capture(Sim, playerId)));   // immediate first state
            return true;
        }

        public void RemoveRemotePlayer(int playerId) { _remote.Remove(playerId); }

        /// <summary>Entry point for command bytes. `senderPlayerId` must come from the transport (connection), never from the payload.</summary>
        public void OnCommand(int senderPlayerId, byte[] data)
        {
            if (!_remote.Contains(senderPlayerId)) return;   // unknown / not-joined sender: ignore silently
            var reply = Authority.Handle(senderPlayerId, data);
            if (!string.IsNullOrEmpty(reply.Message)) Send(senderPlayerId, HostMessage.Notice, Encoding.UTF8.GetBytes(Truncate(reply.Message)));
            if (reply.SnapshotJson != null) Send(senderPlayerId, HostMessage.Snapshot, Encoding.UTF8.GetBytes(reply.SnapshotJson));
        }

        /// <summary>Call after Sim.Tick(dt). Sends fog-filtered state to every remote player at ReplicationInterval.</summary>
        public void Replicate(float dt)
        {
            _sinceReplicate += dt;
            if (_sinceReplicate < ReplicationInterval) return;
            _sinceReplicate = 0f;
            foreach (int pid in _remote)
            {
                Send(pid, HostMessage.State, StateCodec.Encode(StateCodec.Capture(Sim, pid)));
                StatesSent++;
            }
        }

        void Send(int playerId, HostMessage type, byte[] body)
        {
            var payload = new byte[body.Length + 1];
            payload[0] = (byte)type;
            Array.Copy(body, 0, payload, 1, body.Length);
            _msgId = (_msgId + 1) & 0xFFFF;
            foreach (var chunk in Chunker.Split(payload, _msgId, MaxChunk)) _transport.SendToPlayer(playerId, chunk);
        }

        static string Truncate(string s) => s.Length <= 1500 ? s : s.Substring(0, 1500);
    }

    /// <summary>
    /// Client side. Owns a REPLICA simulation (never ticked) that mirrors what the host sends, and turns local player
    /// intents into command bytes for the host. Nothing a client does changes state until the host accepts it.
    /// </summary>
    public sealed class ClientSession
    {
        public readonly MatchSimulation Replica;
        public readonly int PlayerId;
        readonly IClientTransport _transport;
        readonly ChunkAssembler _assembler = new ChunkAssembler();
        readonly Queue<string> _notices = new Queue<string>();
        Action<string> _snapshotCallback;

        public int StatesApplied { get; private set; }
        public int MalformedMessages { get; private set; }
        public bool HasState => StatesApplied > 0;
        public float LastStateTime { get; private set; }

        public ClientSession(int playerId, IClientTransport transport, MatchSimulation replica = null)
        {
            PlayerId = playerId; _transport = transport;
            Replica = replica ?? new MatchSimulation();
            Replica.MarkReplica();
        }

        public void Send(NetCommand cmd) { _transport.SendToHost(CommandCodec.Encode(cmd)); }

        public void RequestSnapshot(Action<string> onJson)
        {
            _snapshotCallback = onJson;
            Send(NetCommand.Snapshot());
        }

        public bool TryDequeueNotice(out string notice)
        {
            if (_notices.Count > 0) { notice = _notices.Dequeue(); return true; }
            notice = null; return false;
        }

        public void ReceiveFromHost(byte[] chunk)
        {
            var full = _assembler.Add(chunk);
            if (full == null) return;
            if (full.Length < 1) { MalformedMessages++; return; }
            var body = new byte[full.Length - 1];
            Array.Copy(full, 1, body, 0, body.Length);
            switch ((HostMessage)full[0])
            {
                case HostMessage.State:
                    if (!StateCodec.TryDecode(body, out var state, out _) || state.Me.Id != PlayerId) { MalformedMessages++; return; }
                    Replica.ApplyReplicatedState(state);
                    StatesApplied++;
                    LastStateTime = state.Time;
                    break;
                case HostMessage.Notice:
                    if (_notices.Count < 50) _notices.Enqueue(Encoding.UTF8.GetString(body));
                    break;
                case HostMessage.Snapshot:
                    var cb = _snapshotCallback; _snapshotCallback = null;
                    cb?.Invoke(Encoding.UTF8.GetString(body));
                    break;
                default: MalformedMessages++; break;
            }
        }
    }

    /// <summary>
    /// In-memory network between one HostSession and any number of ClientSessions. Messages are queued and delivered by
    /// Pump(), so tests and the offline demo exercise the exact same encode / chunk / decode path as a real transport.
    /// </summary>
    public sealed class LoopbackNetwork : IHostTransport
    {
        sealed class ClientPort : IClientTransport
        {
            public LoopbackNetwork Net; public int PlayerId;
            public void SendToHost(byte[] data) { Net._toHost.Enqueue(new KeyValuePair<int, byte[]>(PlayerId, (byte[])data.Clone())); }
        }

        readonly Queue<KeyValuePair<int, byte[]>> _toHost = new Queue<KeyValuePair<int, byte[]>>();
        readonly Queue<KeyValuePair<int, byte[]>> _toClients = new Queue<KeyValuePair<int, byte[]>>();
        readonly Dictionary<int, ClientSession> _clients = new Dictionary<int, ClientSession>();
        public HostSession Host;
        public int HostToClientMessages { get; private set; }

        public void SendToPlayer(int playerId, byte[] chunk) { _toClients.Enqueue(new KeyValuePair<int, byte[]>(playerId, (byte[])chunk.Clone())); }

        public ClientSession Connect(int playerId)
        {
            var port = new ClientPort { Net = this, PlayerId = playerId };
            var client = new ClientSession(playerId, port);
            _clients[playerId] = client;
            Host.AddRemotePlayer(playerId);
            return client;
        }

        public void Disconnect(int playerId) { _clients.Remove(playerId); Host.RemoveRemotePlayer(playerId); }

        /// <summary>Delivers everything currently queued in both directions (commands first, then host messages).</summary>
        public void Pump()
        {
            for (int guard = 0; guard < 8 && (_toHost.Count > 0 || _toClients.Count > 0); guard++)
            {
                while (_toHost.Count > 0) { var m = _toHost.Dequeue(); Host.OnCommand(m.Key, m.Value); }
                while (_toClients.Count > 0)
                {
                    var m = _toClients.Dequeue();
                    HostToClientMessages++;
                    if (_clients.TryGetValue(m.Key, out var c)) c.ReceiveFromHost(m.Value);
                }
            }
        }
    }
}
