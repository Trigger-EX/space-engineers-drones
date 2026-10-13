// il_profiler. Runtime half of the instruction counter (tools/ilcount/). Compiled into out/sim/SimShim.dll, which every script assembly
// references anyway. Only scripts rewritten by tools/ilcount/IlRewrite.exe (SimSettings.CountInstructions) ever call into it, so the
// normal sim path is unaffected.
//
// MODEL = Keen's own: VRage.Scripting.Rewriters.InstructionCountingRewriter (public source, KeenSoftwareHouse/SpaceEngineers) injects a
// call IlInjector.CountInstructions() (m_numInstructions++ and a limit test) at the start of every method / constructor / operator /
// accessor / lambda body, every if / else / while / do / for / foreach body (once per iteration), every non-empty switch section, every
// catch / finally and every goto. It is therefore a count of CODE BLOCKS ENTERED, not of IL instructions (wiki: "entered code blocks").
// EnterMethod / ExitMethod keep the call-chain depth (limit 1000). Everything inside the game API / BCL (including LINQ and List.Sort
// internals) is invisible to the counter; only the lambdas it calls back are counted.
using System;

namespace SimShim
{
    /// <summary>Thrown when the limit is hit; scripts' own catch blocks do not catch it (the rewriter adds an exception filter, like the game's UnblockableIngameExceptions).</summary>
    public sealed class ScriptTooComplexException : Exception
    {
        public ScriptTooComplexException(string m) : base(m) { }
    }

    public static class IlCounter
    {
        [ThreadStatic] public static int Total;       // = Runtime.CurrentInstructionCount
        [ThreadStatic] public static int Limit;       // 0 = not enforced
        [ThreadStatic] public static int Depth;
        [ThreadStatic] public static int MaxDepth;
        [ThreadStatic] public static bool Dead;
        [ThreadStatic] public static int[] Sites;     // per injection site hit counts of the current run

        /// <summary>Starts one Main / constructor run (the game's BeginRunBlock): counter, depth and per-site counts to zero.</summary>
        public static void Begin(int nSites, int limit)
        {
            Total = 0; Limit = limit; Depth = 0; MaxDepth = 0; Dead = false;
            var s = Sites;
            if (s == null || s.Length < nSites) Sites = new int[Math.Max(nSites, 64)];
            else Array.Clear(s, 0, s.Length);
        }

        /// <summary>Nested run (PB.TryRun from inside another PB's Main): the inner script gets its own counter, depth and site array and the outer
        /// run's state is restored afterwards. Inner instructions are not charged to the outer run (conservative; how the game charges a nested
        /// run is unverified).</summary>
        public static int[] Push(out int total, out int limit, out int depth, out int maxDepth, out bool dead)
        {
            total = Total; limit = Limit; depth = Depth; maxDepth = MaxDepth; dead = Dead;
            var s = Sites; Sites = null; return s;
        }

        public static void Pop(int[] sites, int total, int limit, int depth, int maxDepth, bool dead)
        {
            Sites = sites; Total = total; Limit = limit; Depth = depth; MaxDepth = maxDepth; Dead = dead;
        }

        public static void C(int site)
        {
            Sites[site]++;
            if (++Total > Limit && Limit > 0) { Dead = true; throw new ScriptTooComplexException("Script too complex (" + Total + " > " + Limit + ")"); }
        }

        /// <summary>IL mode (tools/ilcount/IlCecil.cs): n IL instructions of one basic block of method <paramref name="site"/> are about to run.</summary>
        public static void A(int site, int n)
        {
            Sites[site] += n;
            Total += n;
            if (Limit > 0 && Total > Limit) { Dead = true; throw new ScriptTooComplexException("Script too complex (" + Total + " > " + Limit + ")"); }
        }

        public static void Enter()
        {
            if (++Depth > MaxDepth) MaxDepth = Depth;
            if (Depth > 1000) { Dead = true; throw new ScriptTooComplexException("call chain depth > 1000"); }
        }

        public static void Exit() { Depth--; }
        public static bool IsDead() { return Dead; }
    }
}
