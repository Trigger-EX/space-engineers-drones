// SLOT A. Deterministic RNG (SplitMix64 seeding + xoshiro256**). Implemented by the architect; slot A owns changes.
// Never use System.Random or time in the engine. Every consumer forks its own stream: rng.Fork(salt).
using System;

namespace SeSim.Core
{
    public sealed class DetRng
    {
        ulong s0, s1, s2, s3;

        public DetRng(ulong seed)
        {
            ulong x = seed;
            s0 = SplitMix(ref x); s1 = SplitMix(ref x); s2 = SplitMix(ref x); s3 = SplitMix(ref x);
        }

        public static ulong SplitMix(ref ulong x)
        {
            ulong z = (x += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>Stable 64-bit mix of a seed and a salt (used for per-ship, per-PB and per-entity streams/ids).</summary>
        public static ulong Mix(ulong seed, ulong salt) { ulong x = seed ^ (salt * 0xD1B54A32D192ED03UL); return SplitMix(ref x); }

        static ulong Rotl(ulong x, int k) { return (x << k) | (x >> (64 - k)); }

        public ulong NextU64()
        {
            ulong result = Rotl(s1 * 5, 7) * 9, t = s1 << 17;
            s2 ^= s0; s3 ^= s1; s1 ^= s2; s0 ^= s3; s2 ^= t; s3 = Rotl(s3, 45);
            return result;
        }

        public double NextDouble() { return (NextU64() >> 11) * (1.0 / 9007199254740992.0); }
        public double Range(double a, double b) { return a + (b - a) * NextDouble(); }
        public int NextInt(int n) { return (int)((NextU64() >> 33) % (ulong)n); }
        public int NextInt32() { return (int)(NextU64() >> 33); }
        public double NextGaussian() { double u = 1.0 - NextDouble(), v = NextDouble(); return Math.Sqrt(-2 * Math.Log(u)) * Math.Cos(2 * Math.PI * v); }
        public DetRng Fork(ulong salt) { return new DetRng(Mix(NextU64(), salt)); }
    }
}
