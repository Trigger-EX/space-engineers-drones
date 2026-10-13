// il_profiler. IL-level instruction counter (the alternative metric, SimSettings.InstructionCountMode = "il").
// Usage: mono IlCecil.exe <in.dll> <out.dll> <sites.tsv> <SimShim.dll>
// At the start of every IL basic block of every method of the script assembly it injects  IlCounter.A(site, n)  with n = the number of IL
// instructions in that block (site = method id), so the sum is the number of IL instructions executed inside the script assembly (calls into
// the BCL / game API / SimShim count as the call instruction only). This is what the game's pre-2016 IlInjector did; the current public
// game source counts code blocks instead (tools/ilcount/IlRewrite.cs), so this number is the CONSERVATIVE upper-bound view: it is
// typically 8-15x the "blocks" figure. Block leaders: method start, branch targets, instruction after any branch / switch / ret / throw /
// leave / endfinally, and every exception handler / filter / try boundary.
// Branch targets and handler boundaries that pointed at a leader are re-pointed to the injected prefix, so the prefix runs on every entry.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

static class IlCecilMain
{
    static int Main(string[] a)
    {
        if (a.Length < 4) { Console.Error.WriteLine("usage: IlCecil.exe in.dll out.dll sites.tsv SimShim.dll"); return 2; }
        var shim = ModuleDefinition.ReadModule(a[3]);
        var addDef = shim.GetType("SimShim.IlCounter").Methods.First(m => m.Name == "A");
        var asm = AssemblyDefinition.ReadAssembly(a[0]);
        var mod = asm.MainModule;
        asm.Name.Name = asm.Name.Name + ".ilc"; mod.Name = asm.Name.Name + ".dll";   // distinct identity: the plain assembly is loaded too
        var add = mod.Import(addDef);
        var sites = new List<string>();
        foreach (var t in AllTypes(mod.Types))
            foreach (var m in t.Methods)
            {
                if (!m.HasBody) continue;
                string name = (t.DeclaringType != null ? t.DeclaringType.Name + "/" : "") + t.Name + "." + m.Name;
                sites.Add("il\t" + name + "\t0");
                Instrument(m, sites.Count - 1, add, mod);
            }
        asm.Write(a[1]);
        File.WriteAllText(a[2], string.Join("", sites.Select((s, i) => i + "\t" + s + "\n").ToArray()), new UTF8Encoding(false));
        Console.WriteLine("methods=" + sites.Count);
        return 0;
    }

    static IEnumerable<TypeDefinition> AllTypes(IEnumerable<TypeDefinition> ts)
    {
        foreach (var t in ts) { yield return t; foreach (var n in AllTypes(t.NestedTypes)) yield return n; }
    }

    static void Instrument(MethodDefinition m, int site, MethodReference add, ModuleDefinition mod)
    {
        var body = m.Body;
        body.SimplifyMacros();
        var ins = body.Instructions;
        if (ins.Count == 0) return;
        var leaders = new HashSet<Instruction> { ins[0] };
        for (int i = 0; i < ins.Count; i++)
        {
            var x = ins[i];
            var t = x.Operand as Instruction;
            var ts = x.Operand as Instruction[];
            if (t != null) leaders.Add(t);
            if (ts != null) foreach (var y in ts) leaders.Add(y);
            bool ends = x.OpCode.FlowControl == FlowControl.Branch || x.OpCode.FlowControl == FlowControl.Cond_Branch || x.OpCode.FlowControl == FlowControl.Return
                     || x.OpCode.FlowControl == FlowControl.Throw || x.OpCode.Code == Code.Leave || x.OpCode.Code == Code.Endfinally || x.OpCode.Code == Code.Endfilter;
            if (ends && i + 1 < ins.Count) leaders.Add(ins[i + 1]);
        }
        foreach (var h in body.ExceptionHandlers)
        {
            if (h.TryStart != null) leaders.Add(h.TryStart);
            if (h.TryEnd != null) leaders.Add(h.TryEnd);
            if (h.HandlerStart != null) leaders.Add(h.HandlerStart);
            if (h.HandlerEnd != null) leaders.Add(h.HandlerEnd);
            if (h.FilterStart != null) leaders.Add(h.FilterStart);
        }
        // block sizes
        var size = new Dictionary<Instruction, int>();
        Instruction cur = null;
        for (int i = 0; i < ins.Count; i++)
        {
            if (leaders.Contains(ins[i])) { cur = ins[i]; size[cur] = 0; }
            size[cur]++;
        }
        var first = new Dictionary<Instruction, Instruction>();
        var il = body.GetILProcessor();
        foreach (var kv in size.ToList())
        {
            var L = kv.Key;
            var a = il.Create(OpCodes.Ldc_I4, site);
            var b = il.Create(OpCodes.Ldc_I4, kv.Value);
            var c = il.Create(OpCodes.Call, add);
            il.InsertBefore(L, a); il.InsertBefore(L, b); il.InsertBefore(L, c);
            first[L] = a;
        }
        Func<Instruction, Instruction> fix = x => x != null && first.ContainsKey(x) ? first[x] : x;
        foreach (var x in ins)
        {
            var t = x.Operand as Instruction;
            if (t != null) { x.Operand = fix(t); continue; }
            var ts = x.Operand as Instruction[];
            if (ts != null) for (int k = 0; k < ts.Length; k++) ts[k] = fix(ts[k]);
        }
        foreach (var h in body.ExceptionHandlers)
        {
            h.TryStart = fix(h.TryStart); h.TryEnd = fix(h.TryEnd); h.HandlerStart = fix(h.HandlerStart); h.HandlerEnd = fix(h.HandlerEnd); h.FilterStart = fix(h.FilterStart);
        }
        body.OptimizeMacros();
    }
}
