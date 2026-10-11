// SLOT D. Compiled into out/sim/SimShim.dll and referenced by every script assembly and by SimHost.
// The script wrapper adds ONE line, "using Random = SimShim.DetRandom;", so the scripts' own text stays byte-identical while
// `new Random()` (time-seeded in .NET) becomes deterministic. DetRandom IS a System.Random, so behaviour is otherwise the same.
// ScriptHost sets DetRandomSeed.Current (thread-static) before constructing/running a PB; each parameterless construction draws
// the next seed from that stream.
using System;

namespace SimShim
{
    public static class DetRandomSeed
    {
        [ThreadStatic] static ulong _state;
        public static void Set(ulong seed) { _state = seed; }
        /// <summary>Current stream state: ScriptHost saves it after each Main call and restores it before the next one, so each PB owns an
        /// independent deterministic stream regardless of how runs interleave on a worker thread.</summary>
        public static ulong Get() { return _state; }
        public static int Next()
        {
            ulong z = (_state += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return (int)((z ^ (z >> 31)) & 0x7FFFFFFF);
        }
    }

    public class DetRandom : System.Random
    {
        public DetRandom() : base(DetRandomSeed.Next()) { }
        public DetRandom(int seed) : base(seed) { }
    }
}
