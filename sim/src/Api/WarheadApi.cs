// SLOT B. IMyWarhead over Combat.WarheadUnit (slot C does countdown and explosion). Detonate() only works when IsArmed
// (game behaviour); StartCountdown starts the timer (DetonationTime seconds, blueprint CountdownMs/1000), StopCountdown
// stops it and restores the timer.
using System;
using Sandbox.ModAPI.Ingame;
using SeSim.Core;
using SeSim.Combat;

namespace SeSim.Api
{
    public sealed class SimWarhead : SimTerminalBlock, IMyWarhead
    {
        public WarheadUnit Unit { get { return Inst.Warhead; } }
        public SimWarhead(BlockInst inst, World world) : base(inst, world) { }
        public bool IsCountingDown { get { var u = Unit; return u != null && u.CountingDown; } }
        public float DetonationTime
        {
            get { var u = Unit; return u != null ? (float)(u.CountingDown ? u.CountdownLeft : u.DetonationTime) : 0f; }
            set
            {
                var u = Unit; if (u == null) return;
                float v = float.IsNaN(value) ? 0f : (value < 1f ? 1f : (value > 3600f ? 3600f : value));
                u.DetonationTime = v;
                if (u.CountingDown) u.CountdownLeft = v;
            }
        }
        public bool IsArmed { get { var u = Unit; return u != null && u.Armed; } set { var u = Unit; if (u != null) u.Armed = value; } }
        public bool StartCountdown()
        {
            var u = Unit; if (u == null || u.Exploded || !Inst.IsFunctional) return false;
            u.CountingDown = true; u.CountdownLeft = u.DetonationTime; return true;
        }
        public bool StopCountdown()
        {
            var u = Unit; if (u == null) return false;
            u.CountingDown = false; u.CountdownLeft = u.DetonationTime; return true;
        }
        public void Detonate()
        {
            var u = Unit;
            if (u != null && u.Armed && !u.Exploded && Inst.IsFunctional) u.Detonate(World);
        }

        protected override void Register(TerminalRegistry r)
        {
            base.Register(r);
            r.Add(new SimAction("StartCountdown", b => ((SimWarhead)b).StartCountdown()));
            r.Add(new SimAction("StopCountdown", b => ((SimWarhead)b).StopCountdown()));
            r.Add(new SimAction("Detonate", b => ((SimWarhead)b).Detonate()));
        }
    }
}
