// SLOT B. Text surfaces are no-ops that keep the last text (for --dump-screens debugging). WriteText must not allocate
// beyond storing the string reference (scripts call it every 30 ticks); append concatenates (capped).
using System;
using System.Text;
using VRage.Game.GUI.TextPanel;
using Sandbox.ModAPI.Ingame;

namespace SeSim.Api
{
    public sealed class SimTextSurface : IMyTextSurface
    {
        public const int MaxChars = 100000;
        public string Text = "";
        public ContentType ContentType { get; set; }
        public float FontSize { get; set; }
        public string Font { get; set; }
        public SimTextSurface() { FontSize = 1f; Font = "Debug"; }

        public bool WriteText(string s, bool append = false)
        {
            if (s == null) s = "";
            if (append && Text.Length > 0)
            {
                string t = Text + s;
                Text = t.Length > MaxChars ? t.Substring(t.Length - MaxChars) : t;
            }
            else Text = s;
            return true;
        }
        public bool WriteText(StringBuilder s, bool append = false) { return WriteText(s == null ? "" : s.ToString(), append); }
        public string GetText() { return Text; }
    }
}
