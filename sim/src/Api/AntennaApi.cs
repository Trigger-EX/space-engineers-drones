// SLOT B. IMyRadioAntenna: Radius (m, from blueprint BroadcastRadius, clamped 0..ApiTuning.MaxAntennaRadius),
// EnableBroadcasting (blueprint EnableBroadcasting). Power: Radius/250 kW (wiki) written to Inst.Sink.RequestMW (MW) when
// broadcasting and enabled. IgcHub uses IsBroadcasting && IsWorking antennas for reachability.
using System;
using Sandbox.ModAPI.Ingame;
using SeSim.Core;

namespace SeSim.Api
{
    public sealed class SimRadioAntenna : SimFunctionalBlock, IMyRadioAntenna
    {
        public float RadiusValue; public bool Broadcast = true; public string Hud = "";
        public SimRadioAntenna(BlockInst inst, World world) : base(inst, world)
        {
            var p = inst.Src != null ? inst.Src.Props : null;
            string s; float f;
            RadiusValue = 5000;
            if (p != null)
            {
                if (p.TryGetValue("BroadcastRadius", out s) && float.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out f)) RadiusValue = f;
                if (p.TryGetValue("EnableBroadcasting", out s)) Broadcast = string.Equals(s.Trim(), "true", StringComparison.OrdinalIgnoreCase) || s.Trim() == "1";
                if (p.TryGetValue("HudText", out s)) Hud = s ?? "";
            }
            RadiusValue = ClampRadius(RadiusValue);
            UpdatePower();
        }

        static float ClampRadius(float v) { return float.IsNaN(v) || v < 0f ? 0f : (v > ApiTuning.MaxAntennaRadius ? (float)ApiTuning.MaxAntennaRadius : v); }
        void UpdatePower()
        {
            var s = Inst.Sink;
            if (s != null) s.RequestMW = (EnabledValue && Broadcast) ? RadiusValue / 250.0 / 1000.0 : 0.0;
        }
        protected override void OnEnabledChanged() { UpdatePower(); }

        public float Radius { get { return RadiusValue; } set { RadiusValue = ClampRadius(value); UpdatePower(); } }
        public bool EnableBroadcasting { get { return Broadcast; } set { Broadcast = value; UpdatePower(); } }
        public string HudText { get { return Hud; } set { Hud = value ?? ""; } }
        public bool IsBroadcasting { get { return Broadcast && IsWorking; } }

        protected override void Register(TerminalRegistry r)
        {
            base.Register(r);
            AddFloat(r, "Radius", b => ((SimRadioAntenna)b).Radius, (b, v) => ((SimRadioAntenna)b).Radius = v, 5000f, 0f, (float)ApiTuning.MaxAntennaRadius);
            AddBool(r, "EnableBroadCast", b => ((SimRadioAntenna)b).EnableBroadcasting, (b, v) => ((SimRadioAntenna)b).EnableBroadcasting = v, true);
        }
    }
}
