// SLOT B. Intergrid communication (docs/sim-design.md 4.3).
// Send: SimIgc.SendBroadcastMessage enqueues (tag, data, sender PB, distance mode) into IgcHub's outbox (one boxed payload per
// message, struct storage reused). Delivery: World phase 2 (after all scripts of the tick ran) -> messages are readable on the
// NEXT tick, independent of PB order.
// Reach for AntennaRelay: receiver PB's Body == sender Body, or a chain of antennas: some working broadcasting antenna on a
// reached body whose Radius >= distance(antenna, any working antenna on the next body) (receiving antenna only needs to be
// working; BFS over bodies so relays through third bodies work). The antenna graph is rebuilt at most once per tick.
// CurrentConstruct / ConnectedConstructs: same Body only. Sender does not receive its own broadcast.
// Listener queue: MaxWaitingMessages (SimSettings.IgcMaxWaitingMessages); overflow drops the OLDEST. Payload must be immutable
// (MyTuple of primitives/Vector3D/string...): a mutable payload (List<>, arrays, classes) -> InvalidOperationException in the
// sender (the game rejects it too).
// Teams: antennas only carry messages between owners that are not enemies (Friendly()), like the game's antenna system.
// Callbacks: SetMessageCallback(arg) makes the owner PB run once (UpdateType.IGC, argument arg) after a delivery that put
// messages in that queue.
using System;
using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using VRageMath;
using SeSim.Core;

namespace SeSim.Api
{
    /// <summary>Which payload types the game accepts in IGC messages ("immutable" types).</summary>
    public static class ImmutableTypes
    {
        public static bool Check(Type t)
        {
            if (t == null) return false;
            if (t.IsPrimitive || t == typeof(string) || t == typeof(decimal) || t.IsEnum) return true;
            if (t.IsValueType && t.Namespace == "VRageMath") return true;
            if (t == typeof(VRage.MyFixedPoint)) return true;
            if (t.IsGenericType)
            {
                Type def = t.GetGenericTypeDefinition();
                string n = def.FullName ?? "";
                if (n.StartsWith("VRage.MyTuple`", StringComparison.Ordinal) || n.StartsWith("System.Nullable`", StringComparison.Ordinal)
                    || n.StartsWith("System.Collections.Immutable.", StringComparison.Ordinal))
                {
                    foreach (Type a in t.GetGenericArguments()) if (!Check(a)) return false;
                    return true;
                }
            }
            return false;
        }
    }

    static class ImmutableCheck<T> { public static readonly bool Ok = ImmutableTypes.Check(typeof(T)); }

    public sealed class IgcHub
    {
        public readonly World World;
        public readonly List<SimIgc> Endpoints = new List<SimIgc>();
        public readonly List<SimRadioAntenna> Antennas = new List<SimRadioAntenna>();
        public readonly List<SimProgrammableBlock> PendingScripts = new List<SimProgrammableBlock>();   // see BlockApiFactory.FlushPending

        struct Msg { public SimIgc From; public string Tag; public object Data; public TransmissionDistance Dist; public long To; public bool Unicast; }
        Msg[] _out = new Msg[64]; int _outN;
        readonly List<SimMessageQueue> _callbacks = new List<SimMessageQueue>();
        public long Sent, Delivered, Dropped;   // statistics

        // ---- antenna graph, rebuilt at most once per tick --------------------------------------------------------
        long _netTick = -1;
        SimRadioAntenna[] _na = new SimRadioAntenna[16]; int _naN;
        int[] _naBody = new int[16]; Vector3D[] _naPos = new Vector3D[16]; bool[] _naBc = new bool[16]; double[] _naRad = new double[16];
        Body[] _nb = new Body[16]; int _nbN;
        bool[][] _reach = new bool[16][]; bool[] _reachDone = new bool[16];
        int[] _queue = new int[16];

        public IgcHub(World w) { World = w; }

        public void Register(SimIgc e) { if (!Endpoints.Contains(e)) Endpoints.Add(e); }
        public void Unregister(SimIgc e) { Endpoints.Remove(e); }
        public void RegisterAntenna(SimRadioAntenna a) { if (!Antennas.Contains(a)) Antennas.Add(a); }

        public void Enqueue(SimIgc from, string tag, object data, TransmissionDistance d)
        {
            if (from.Pb.Inst.Destroyed) return;
            Push(new Msg { From = from, Tag = tag, Data = data, Dist = d });
        }

        /// <summary>Test hook (dock-launch): delivers a broadcast to every listener of the tag at once, as if sent by a script-less source.</summary>
        public void InjectBroadcast(string tag, object data)
        {
            for (int k = 0; k < Endpoints.Count; k++)
            {
                SimIgc e = Endpoints[k];
                if (e.Pb.Inst.Destroyed) continue;
                SimBroadcastListener l = e.ListenerFor(tag);
                if (l != null) { l.Push(new MyIGCMessage(data, tag, 0)); Delivered++; }
            }
        }

        public bool EnqueueUnicast(SimIgc from, long to, string tag, object data)
        {
            if (from.Pb.Inst.Destroyed) return false;
            SimIgc target = Find(to);
            if (target == null || target == from || !Reachable(from, target, TransmissionDistance.AntennaRelay)) return false;
            Push(new Msg { From = from, Tag = tag, Data = data, Dist = TransmissionDistance.AntennaRelay, To = to, Unicast = true });
            return true;
        }

        void Push(Msg m)
        {
            if (_outN == _out.Length) Array.Resize(ref _out, _out.Length * 2);
            _out[_outN++] = m; Sent++;
        }

        public SimIgc Find(long address)
        {
            for (int i = 0; i < Endpoints.Count; i++)
            {
                SimIgc e = Endpoints[i];
                if (e.Me == address && !e.Pb.Inst.Destroyed) return e;
            }
            return null;
        }

        /// <summary>World phase 2.</summary>
        public void Deliver()
        {
            if (PendingScripts.Count > 0) BlockApiFactory.FlushPending(World);
            if (_outN == 0) return;
            int n = _outN;   // callbacks run later and may send more: those stay for the next Deliver
            for (int i = 0; i < n; i++)
            {
                Msg m = _out[i];
                if (m.Unicast)
                {
                    SimIgc t = Find(m.To);
                    if (t != null && t != m.From && Reachable(m.From, t, m.Dist)) { t.Unicast.Push(new MyIGCMessage(m.Data, m.Tag, m.From.Me)); Delivered++; }
                    else Dropped++;
                    continue;
                }
                for (int k = 0; k < Endpoints.Count; k++)
                {
                    SimIgc e = Endpoints[k];
                    if (e == m.From || e.Pb.Inst.Destroyed) continue;
                    SimBroadcastListener l = e.ListenerFor(m.Tag);
                    if (l == null) continue;
                    if (!Reachable(m.From, e, m.Dist)) { Dropped++; continue; }
                    l.Push(new MyIGCMessage(m.Data, m.Tag, m.From.Me)); Delivered++;
                }
            }
            if (n == _outN) { Array.Clear(_out, 0, n); _outN = 0; }
            else { Array.Copy(_out, n, _out, 0, _outN - n); Array.Clear(_out, _outN - n, n); _outN -= n; }

            if (_callbacks.Count > 0)
            {
                var scripts = World.Scripts;
                for (int i = 0; i < _callbacks.Count; i++)
                {
                    SimMessageQueue q = _callbacks[i];
                    q.CallbackQueued = false;
                    if (scripts != null && q.CallbackSet && !q.Owner.Pb.Inst.Destroyed) scripts.RunArgument(q.Owner.Pb, q.CallbackArgument ?? "", UpdateType.IGC);
                }
                _callbacks.Clear();
            }
        }

        public void QueueCallback(SimMessageQueue q) { _callbacks.Add(q); }

        public bool Reachable(SimIgc from, SimIgc to, TransmissionDistance d)
        {
            Body fb = from.Pb.Inst.Body, tb = to.Pb.Inst.Body;
            if (fb == tb) return true;
            if (d != TransmissionDistance.AntennaRelay) return false;
            if (!Friendly(fb.OwnerId, tb.OwnerId)) return false;
            if (!World.Settings.IgcRequireAntenna) return true;
            BuildNet();
            int fi = BodyIndex(fb), ti = BodyIndex(tb);
            if (fi < 0 || ti < 0) return false;
            return Row(fi)[ti];
        }

        /// <summary>Game rule: antennas only relay for owners that are not enemies of the sender (own team, allies, faction).
        /// ApiTuning.IgcAllowEnemyRelay lifts it.</summary>
        bool Friendly(long senderOwner, long otherOwner)
        {
            if (ApiTuning.IgcAllowEnemyRelay || senderOwner == otherOwner) return true;
            var rel = World.Teams.Relation(senderOwner, otherOwner);
            return rel == VRage.Game.MyRelationsBetweenPlayerAndBlock.Owner || rel == VRage.Game.MyRelationsBetweenPlayerAndBlock.FactionShare
                || rel == VRage.Game.MyRelationsBetweenPlayerAndBlock.Friends;
        }

        int BodyIndex(Body b) { for (int i = 0; i < _nbN; i++) if (_nb[i] == b) return i; return -1; }

        void BuildNet()
        {
            if (_netTick == World.Tick) return;
            _netTick = World.Tick; _naN = 0; _nbN = 0;
            for (int i = 0; i < Antennas.Count; i++)
            {
                SimRadioAntenna a = Antennas[i];
                if (a.Inst.Destroyed || !a.Inst.Body.Alive || !a.IsWorking) continue;
                if (_naN == _na.Length)
                {
                    int sz = _na.Length * 2;
                    Array.Resize(ref _na, sz); Array.Resize(ref _naBody, sz); Array.Resize(ref _naPos, sz); Array.Resize(ref _naBc, sz); Array.Resize(ref _naRad, sz);
                }
                Body b = a.Inst.Body; int bi = BodyIndex(b);
                if (bi < 0)
                {
                    if (_nbN == _nb.Length) { Array.Resize(ref _nb, _nb.Length * 2); Array.Resize(ref _reach, _nb.Length); Array.Resize(ref _reachDone, _nb.Length); }
                    bi = _nbN; _nb[_nbN++] = b;
                }
                _na[_naN] = a; _naBody[_naN] = bi; _naPos[_naN] = a.Inst.WorldCenter; _naBc[_naN] = a.Broadcast; _naRad[_naN] = a.RadiusValue;
                _naN++;
            }
            for (int i = 0; i < _nbN; i++) _reachDone[i] = false;
        }

        bool[] Row(int bi)
        {
            bool[] row = _reach[bi];
            if (row == null || row.Length < _nbN) { row = new bool[Math.Max(_nbN, 8)]; _reach[bi] = row; }
            if (_reachDone[bi]) return row;
            _reachDone[bi] = true;
            Array.Clear(row, 0, row.Length);
            if (_queue.Length < _nbN) _queue = new int[_nbN * 2];
            int qh = 0, qt = 0;
            row[bi] = true; _queue[qt++] = bi;
            long senderOwner = _nb[bi].OwnerId;
            while (qh < qt)
            {
                int cur = _queue[qh++];
                for (int a = 0; a < _naN; a++)
                {
                    if (_naBody[a] != cur || !_naBc[a]) continue;
                    double r2 = _naRad[a] * _naRad[a];
                    for (int t = 0; t < _naN; t++)
                    {
                        int tb = _naBody[t];
                        if (row[tb]) continue;
                        if (!Friendly(senderOwner, _nb[tb].OwnerId)) continue;
                        double dx = _naPos[a].X - _naPos[t].X, dy = _naPos[a].Y - _naPos[t].Y, dz = _naPos[a].Z - _naPos[t].Z;
                        if (dx * dx + dy * dy + dz * dz <= r2) { row[tb] = true; _queue[qt++] = tb; }
                    }
                }
            }
            return row;
        }
    }

    public sealed class SimIgc : IMyIntergridCommunicationSystem
    {
        public readonly SimProgrammableBlock Pb; public readonly IgcHub Hub;
        public readonly List<SimBroadcastListener> Listeners = new List<SimBroadcastListener>();
        public readonly SimUnicastListener Unicast;

        public SimIgc(SimProgrammableBlock pb, IgcHub hub)
        {
            Pb = pb; Hub = hub;
            Unicast = new SimUnicastListener();
            Unicast.Owner = this; Unicast.Max = hub.World.Settings.IgcMaxWaitingMessages;
        }

        public long Me { get { return Pb.EntityId; } }     // the game uses the PB's EntityId as address
        public IMyUnicastListener UnicastListener { get { return Unicast; } }

        public SimBroadcastListener ListenerFor(string tag)
        {
            for (int i = 0; i < Listeners.Count; i++)
            {
                SimBroadcastListener l = Listeners[i];
                if (l.Active && string.Equals(l.TagValue, tag, StringComparison.Ordinal)) return l;
            }
            return null;
        }

        public IMyBroadcastListener RegisterBroadcastListener(string t)
        {
            if (t == null) throw new ArgumentNullException("t");
            SimBroadcastListener l = ListenerFor(t);
            if (l != null) return l;
            l = new SimBroadcastListener();
            l.Owner = this; l.TagValue = t; l.Max = Hub.World.Settings.IgcMaxWaitingMessages;
            Listeners.Add(l);
            return l;
        }

        public void DisableBroadcastListener(IMyBroadcastListener l)
        {
            var s = l as SimBroadcastListener;
            if (s == null) return;
            s.Active = false; s.CallbackSet = false; Listeners.Remove(s);
        }

        public void GetBroadcastListeners(List<IMyBroadcastListener> listeners, Func<IMyBroadcastListener, bool> collect = null)
        {
            listeners.Clear();
            for (int i = 0; i < Listeners.Count; i++) if (collect == null || collect(Listeners[i])) listeners.Add(Listeners[i]);
        }

        public bool IsEndpointReachable(long address, TransmissionDistance d = TransmissionDistance.AntennaRelay)
        {
            SimIgc e = Hub.Find(address);
            return e != null && e != this && Hub.Reachable(this, e, d);
        }

        public void SendBroadcastMessage<T>(string tag, T data, TransmissionDistance d = TransmissionDistance.AntennaRelay)
        {
            if (tag == null) throw new ArgumentNullException("tag");
            CheckPayload(data);
            Hub.Enqueue(this, tag, data, d);
        }

        public bool SendUnicastMessage<T>(long addressee, string tag, T data)
        {
            if (tag == null) throw new ArgumentNullException("tag");
            CheckPayload(data);
            return Hub.EnqueueUnicast(this, addressee, tag, data);
        }

        static void CheckPayload<T>(T data)
        {
            bool ok = ImmutableCheck<T>.Ok;
            if (!ok && typeof(T) == typeof(object) && data != null) ok = ImmutableTypes.Check(data.GetType());
            if (!ok) throw new InvalidOperationException("IGC payload type " + typeof(T).FullName + " is not an immutable type");
        }
    }

    public class SimMessageQueue : IMyMessageProvider
    {
        protected readonly Queue<MyIGCMessage> Q = new Queue<MyIGCMessage>();
        public int Max = 25;
        public SimIgc Owner;
        public bool CallbackSet, CallbackQueued; public string CallbackArgument;
        public bool HasPendingMessage { get { return Q.Count > 0; } }
        public int MaxWaitingMessages { get { return Max; } }
        public int Count { get { return Q.Count; } }

        /// <summary>Dequeues the oldest message; an empty queue gives default(MyIGCMessage).</summary>
        public MyIGCMessage AcceptMessage() { return Q.Count > 0 ? Q.Dequeue() : default(MyIGCMessage); }

        public void SetMessageCallback(string argument = "")
        {
            CallbackSet = true; CallbackArgument = argument ?? "";
            if (Q.Count > 0 && !CallbackQueued && Owner != null) { CallbackQueued = true; Owner.Hub.QueueCallback(this); }
        }
        public void DisableMessageCallback() { CallbackSet = false; }

        public void Push(MyIGCMessage m)
        {
            if (Max > 0 && Q.Count >= Max) { Q.Dequeue(); if (Owner != null) Owner.Hub.Dropped++; }
            Q.Enqueue(m);
            if (CallbackSet && !CallbackQueued && Owner != null) { CallbackQueued = true; Owner.Hub.QueueCallback(this); }
        }
    }

    public sealed class SimBroadcastListener : SimMessageQueue, IMyBroadcastListener
    {
        public string TagValue; public bool Active = true;
        public bool IsActive { get { return Active; } }
        public string Tag { get { return TagValue; } }
    }

    public sealed class SimUnicastListener : SimMessageQueue, IMyUnicastListener { }
}
