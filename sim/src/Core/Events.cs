// SLOT A owns the struct and the enum; any slot EMITS events (World.Events.On(ref e)); slot D's Metrics consumes them.
// Adding an EventKind value: append at the end and tell slot D.
using System;
using VRageMath;

namespace SeSim.Core
{
    public enum EventKind
    {
        Shot,              // ShipA fired a weapon (Value = projectile kind)
        Hit,               // projectile/explosion from ShipA damaged a block of ShipB (Value = damage)
        BlockDestroyed,    // block of ShipB destroyed by ShipA (Value = block mass)
        ShipDisabled,      // ShipB lost its AI (PB or RC or all batteries) or all thrust/guns -> "mission kill"
        ShipDestroyed,     // ShipB has no functional blocks left / below 10% mass
        FriendlyFire,      // Hit where Relation(A,B) is Owner/FactionShare/Friends
        Collision,         // ShipA hit ShipB (Value = relative speed)
        Detonation,        // warhead of ShipA exploded (Value = radius)
        ScriptException,   // PB of ShipA threw out of Main or the constructor (Text = message); PB stops like in game
        MergeReleased,     // merge connection broken on ShipA
        GridSplit,         // ShipA split (Value = parts)
        TargetLocked,      // optional, from scripts' IGC/script state when observable
        CollisionDamage,   // ShipB lost Value hit points of block integrity in a collision with ShipA (ShipA may be -1); not a Hit
        BlockDamage,       // reporting only: Block of ShipB absorbed Value integrity from ShipA; Aux = SeSim.Combat.DamageKind, Id = projectile id (0 unknown); sent AFTER Block.Hp was reduced
    }

    public struct SimEvent
    {
        public EventKind Kind;
        public long Tick;
        public int ShipA, ShipB;          // ShipInstance indices (-1 = none/world)
        public double Value;
        public Vector3D Position;
        public string Text;               // rare (exceptions); null otherwise
        public int Aux;                   // BlockDamage: DamageKind as int
        public BlockInst Block;           // BlockDamage: the damaged block (Hp already reduced; Hp <= 0 = this hit destroyed it)
        public long Id;                   // Hit/FriendlyFire: id of the projectile that did it (0 = unknown, each event then counts alone)
    }

    public interface IEventSink
    {
        void On(ref SimEvent e);
        void EndTick(World w);            // metrics sampling + termination check hooks
    }

    public sealed class NullEventSink : IEventSink
    {
        public static readonly NullEventSink Instance = new NullEventSink();
        public void On(ref SimEvent e) { }
        public void EndTick(World w) { }
    }
}
