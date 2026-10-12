// SLOT C. Decoy blocks (sim-audit-1 section 7.4; docs/sim-design.md section 15, SimSettings.DecoysFunctional).
// Evidence: wiki Decoy block "enemy turrets and homing missiles prefer decoys"; Data/CubeBlocks_Weapons.sbc and CubeBlocks_SignalsPack.sbc give
// the Decoy definition PriorityModifier 10 and NotWorkingPriorityMultiplier 0 (a switched-off or non-functional decoy has priority 0).
// Model, used by TurretUnit (large turrets and Custom Turret Controller designation) and VanillaAiBrain (AI block stand-in, rockets included):
//   * a decoy is "working" when it is alive, functional (Hp >= critical integrity) and enabled;
//   * target selection prefers bodies with a working decoy (SimSettings.DecoyPreferTarget) among those in range with line of sight;
//   * the aim point on the chosen body is the working decoy nearest to the shooter instead of the body centre, with probability
//     SimSettings.DecoyAimProbability (default 1), re-rolled every SimSettings.DecoyRerollS seconds per shooter/target pair.
// Not modelled: guided missiles (sim rockets and slugs fly straight), a decoy's own PCU/range limits, scripts' camera-ray aim points.
using System;
using VRageMath;
using SeSim.Core;

namespace SeSim.Combat
{
    public static class Decoys
    {
        /// <summary>Decoy blocks of the body (cached per Body.Version); includes non-working ones.</summary>
        public static System.Collections.Generic.List<BlockInst> Of(Body b)
        {
            if (b.DecoyVersion != b.Version)
            {
                b.DecoyVersion = b.Version;
                b.DecoyBlocks.Clear();
                var bl = b.Blocks;
                for (int i = 0; i < bl.Count; i++)
                    if (string.Equals(bl[i].Def.TypeId, "Decoy", StringComparison.Ordinal)) b.DecoyBlocks.Add(bl[i]);
            }
            return b.DecoyBlocks;
        }

        static bool Working(BlockInst d)
        {
            return !d.Destroyed && d.IsFunctional && CombatUtil.BlockEnabled(d);
        }

        /// <summary>True when the body has at least one working decoy and decoys are enabled in the settings.</summary>
        public static bool HasWorking(World w, Body b)
        {
            if (!w.Settings.DecoysFunctional) return false;
            var l = Of(b);
            for (int i = 0; i < l.Count; i++) if (Working(l[i])) return true;
            return false;
        }

        /// <summary>Nearest working decoy of 'target' to 'from' (world position), or null.</summary>
        public static BlockInst Nearest(World w, Body target, Vector3D from)
        {
            if (!w.Settings.DecoysFunctional) return null;
            var l = Of(target);
            BlockInst best = null; double bd = double.MaxValue;
            for (int i = 0; i < l.Count; i++)
            {
                var d = l[i];
                if (!Working(d)) continue;
                Vector3D p = target.LocalToWorld(d.LocalCenter);
                double dx = p.X - from.X, dy = p.Y - from.Y, dz = p.Z - from.Z;
                double q = dx * dx + dy * dy + dz * dz;
                if (q < bd) { bd = q; best = d; }
            }
            return best;
        }

        /// <summary>The point a shooter at 'from' (identified by 'shooterId' for the deterministic roll) aims at on 'target': the nearest working
        /// decoy when there is one and the roll passes, else the body position (centre of mass), as before.</summary>
        public static Vector3D AimPoint(World w, Body target, Vector3D from, long shooterId)
        {
            var s = w.Settings;
            if (!s.DecoysFunctional) return target.Position;
            var d = Nearest(w, target, from);
            if (d == null) return target.Position;
            if (s.DecoyAimProbability < 1.0)
            {
                long bucket = s.DecoyRerollS > 0 ? (long)Math.Floor(w.Time / s.DecoyRerollS) : 0;
                ulong h = DetRng.Mix(DetRng.Mix(w.Seed ^ 0xDEC0Ful, (ulong)shooterId), (ulong)target.EntityId ^ ((ulong)bucket * 0x9E3779B97F4A7C15UL));
                double u = (h >> 11) * (1.0 / 9007199254740992.0);
                if (u >= s.DecoyAimProbability) return target.Position;
            }
            return target.LocalToWorld(d.LocalCenter);
        }
    }
}
