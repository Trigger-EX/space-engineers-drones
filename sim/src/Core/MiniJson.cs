// SLOT A. Minimal JSON reader (defs_stats.json) producing Dictionary<string,object> / List<object> / double / string / bool / null.
// Load-time only; not used in hot paths. (Slot D writes JSON with its own writer in Run/JsonOut.cs.)
// Accepts the Python json extensions NaN / Infinity / -Infinity (tools/index_stats.py may emit them).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SeSim.Core
{
    public static class MiniJson
    {
        public static object Parse(string text)
        {
            var p = new Parser(text);
            p.Ws();
            object v = p.Value();
            p.Ws();
            if (p.I != text.Length) throw p.Error("trailing characters");
            return v;
        }

        sealed class Parser
        {
            readonly string _s; public int I;
            public Parser(string s) { _s = s; }
            public Exception Error(string msg)
            {
                int line = 1; for (int k = 0; k < I && k < _s.Length; k++) if (_s[k] == '\n') line++;
                return new FormatException("JSON: " + msg + " at offset " + I + " (line " + line + ")");
            }
            public void Ws() { while (I < _s.Length) { char c = _s[I]; if (c == ' ' || c == '\t' || c == '\r' || c == '\n') I++; else break; } }

            public object Value()
            {
                if (I >= _s.Length) throw Error("unexpected end");
                char c = _s[I];
                switch (c)
                {
                    case '{': return Object();
                    case '[': return Array();
                    case '"': return String();
                    case 't': Expect("true"); return true;
                    case 'f': Expect("false"); return false;
                    case 'n': Expect("null"); return null;
                    case 'N': Expect("NaN"); return double.NaN;
                    case 'I': Expect("Infinity"); return double.PositiveInfinity;
                    default: return Number();
                }
            }

            void Expect(string lit)
            {
                if (string.CompareOrdinal(_s, I, lit, 0, lit.Length) != 0) throw Error("expected " + lit);
                I += lit.Length;
            }

            object Number()
            {
                int st = I;
                if (I < _s.Length && _s[I] == '-')
                {
                    if (string.CompareOrdinal(_s, I, "-Infinity", 0, 9) == 0) { I += 9; return double.NegativeInfinity; }
                    I++;
                }
                while (I < _s.Length)
                {
                    char c = _s[I];
                    if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-') I++; else break;
                }
                if (I == st) throw Error("unexpected character '" + _s[I] + "'");
                double d;
                if (!double.TryParse(_s.Substring(st, I - st), NumberStyles.Float, CultureInfo.InvariantCulture, out d)) throw Error("bad number");
                return d;
            }

            string String()
            {
                I++; // opening quote
                StringBuilder sb = null;
                int st = I;
                while (true)
                {
                    if (I >= _s.Length) throw Error("unterminated string");
                    char c = _s[I];
                    if (c == '"')
                    {
                        string r = sb == null ? _s.Substring(st, I - st) : sb.Append(_s, st, I - st).ToString();
                        I++;
                        return r;
                    }
                    if (c == '\\')
                    {
                        if (sb == null) sb = new StringBuilder();
                        sb.Append(_s, st, I - st);
                        I++;
                        if (I >= _s.Length) throw Error("bad escape");
                        char e = _s[I++];
                        switch (e)
                        {
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case '/': sb.Append('/'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'u':
                                if (I + 4 > _s.Length) throw Error("bad \\u escape");
                                sb.Append((char)int.Parse(_s.Substring(I, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                                I += 4; break;
                            default: throw Error("bad escape \\" + e);
                        }
                        st = I;
                    }
                    else I++;
                }
            }

            Dictionary<string, object> Object()
            {
                I++; // {
                var d = new Dictionary<string, object>();
                Ws();
                if (I < _s.Length && _s[I] == '}') { I++; return d; }
                while (true)
                {
                    Ws();
                    if (I >= _s.Length || _s[I] != '"') throw Error("expected key");
                    string k = String();
                    Ws();
                    if (I >= _s.Length || _s[I] != ':') throw Error("expected ':'");
                    I++; Ws();
                    d[k] = Value();
                    Ws();
                    if (I >= _s.Length) throw Error("unterminated object");
                    if (_s[I] == ',') { I++; continue; }
                    if (_s[I] == '}') { I++; return d; }
                    throw Error("expected ',' or '}'");
                }
            }

            List<object> Array()
            {
                I++; // [
                var l = new List<object>();
                Ws();
                if (I < _s.Length && _s[I] == ']') { I++; return l; }
                while (true)
                {
                    Ws();
                    l.Add(Value());
                    Ws();
                    if (I >= _s.Length) throw Error("unterminated array");
                    if (_s[I] == ',') { I++; continue; }
                    if (_s[I] == ']') { I++; return l; }
                    throw Error("expected ',' or ']'");
                }
            }
        }

        // ---- typed helpers: return default when the path is missing or of another type ----------------------

        public static Dictionary<string, object> Obj(object o, string key)
        {
            var d = o as Dictionary<string, object>;
            if (d == null || key == null) return null;
            object v; return d.TryGetValue(key, out v) ? v as Dictionary<string, object> : null;
        }

        public static double Num(object o, string key, double dflt)
        {
            var d = o as Dictionary<string, object>;
            if (d == null) return dflt;
            object v; if (!d.TryGetValue(key, out v) || v == null) return dflt;
            if (v is double) return (double)v;
            if (v is bool) return ((bool)v) ? 1 : 0;
            return dflt;
        }

        public static string Str(object o, string key, string dflt)
        {
            var d = o as Dictionary<string, object>;
            if (d == null) return dflt;
            object v; if (!d.TryGetValue(key, out v) || v == null) return dflt;
            return v as string ?? dflt;
        }

        public static bool Bool(object o, string key, bool dflt)
        {
            var d = o as Dictionary<string, object>;
            if (d == null) return dflt;
            object v; if (!d.TryGetValue(key, out v) || v == null) return dflt;
            if (v is bool) return (bool)v;
            if (v is double) return (double)v != 0;
            return dflt;
        }

        public static List<object> Arr(object o, string key)
        {
            var d = o as Dictionary<string, object>;
            if (d == null) return null;
            object v; return d.TryGetValue(key, out v) ? v as List<object> : null;
        }

        public static bool Has(object o, string key)
        {
            var d = o as Dictionary<string, object>;
            object v; return d != null && d.TryGetValue(key, out v) && v != null;
        }

        /// <summary>Walks nested objects: Dig(root, "a", "b") = root.a.b (null when missing).</summary>
        public static object Dig(object o, params string[] keys)
        {
            for (int i = 0; i < keys.Length && o != null; i++)
            {
                var d = o as Dictionary<string, object>;
                if (d == null) return null;
                object v; if (!d.TryGetValue(keys[i], out v)) return null;
                o = v;
            }
            return o;
        }
    }
}
