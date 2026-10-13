// il_profiler. Source-level instruction-count injector: a port of Keen's own VRage.Scripting.Rewriters.InstructionCountingRewriter
// (KeenSoftwareHouse/SpaceEngineers, Sources/VRage.Scripting/Rewriters/InstructionCountingRewriter.cs) onto Roslyn 1.3.2, so the
// sim counts exactly what the game's "Script too complex" limit counts: CODE BLOCKS ENTERED (see sim/shim/IlCounter.cs).
//
// Usage: mono IlRewrite.exe <wrapped.cs> <out.cs> <sites.tsv> <line-offset> <ref.dll> [<ref.dll> ...]
//   wrapped.cs   the script exactly as ScriptCompiler.Wrap / sim/build.sh wrap it (usings + "public partial class Program ... {" + text + "}")
//   out.cs       the same source with global::SimShim.IlCounter.C(site) / Enter() / Exit() calls injected
//   sites.tsv    "<site>\t<kind>\t<method>\t<script line>" for every injected counter (line = wrapped line - line-offset)
//   refs         reference assemblies for the semantic model (mscorlib, System, System.Core, SeApi.dll, SimShim.dll)
// Injection points (same as the game): method / constructor / destructor / operator / conversion / property accessor entry (+ Enter/Exit
// call-chain depth in try/finally); lambda and anonymous-method bodies; if / else (not "else if") bodies; do / while / for / foreach bodies
// (per iteration); every non-empty switch section; catch and finally blocks; goto. NOT counted: conditions, ?:, && ||, plain nested
// blocks, empty switch labels, field initializers, auto-properties.
// Differences from the game's rewriter (documented in docs/audit/il_profiler.md): no #line barricades, `using` statements keep their
// shape (only the body is counted), the finally dead-check is kept but not the using->try/finally rewrite.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

static class IlRewriteMain
{
    static int Main(string[] a)
    {
        if (a.Length < 5) { Console.Error.WriteLine("usage: IlRewrite.exe wrapped.cs out.cs sites.tsv line-offset ref.dll..."); return 2; }
        string src = File.ReadAllText(a[0]);
        int off = int.Parse(a[3]);
        var refs = new List<MetadataReference>();
        for (int i = 4; i < a.Length; i++) refs.Add(MetadataReference.CreateFromFile(a[i]));
        var tree = CSharpSyntaxTree.ParseText(src, new CSharpParseOptions(LanguageVersion.CSharp6));
        var comp = CSharpCompilation.Create("probe", new[] { tree }, refs, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        int errs = 0;
        foreach (var d in comp.GetDiagnostics()) if (d.Severity == DiagnosticSeverity.Error) { if (errs++ < 5) Console.Error.WriteLine("warning: semantic model error: " + d); }
        var rw = new Rw(comp.GetSemanticModel(tree), off);
        var root = rw.Visit(tree.GetRoot());
        File.WriteAllText(a[1], root.ToFullString(), new UTF8Encoding(false));
        var sb = new StringBuilder();
        for (int i = 0; i < rw.Sites.Count; i++) sb.Append(i).Append('\t').Append(rw.Sites[i]).Append('\n');
        File.WriteAllText(a[2], sb.ToString(), new UTF8Encoding(false));
        Console.WriteLine("sites=" + rw.Sites.Count + " unresolvedLambdas=" + rw.Unresolved + " semanticErrors=" + errs);
        return 0;
    }
}

sealed class Rw : CSharpSyntaxRewriter
{
    readonly SemanticModel _sm; readonly int _off;
    public readonly List<string> Sites = new List<string>();
    public int Unresolved;
    readonly Stack<string> _ctx = new Stack<string>();
    public Rw(SemanticModel sm, int off) { _sm = sm; _off = off; }

    int NewSite(string kind, SyntaxNode at)
    {
        int line = at.GetLocation().GetLineSpan().StartLinePosition.Line + 1 - _off;
        Sites.Add(kind + "\t" + (_ctx.Count > 0 ? _ctx.Peek() : "?") + "\t" + line);
        return Sites.Count - 1;
    }

    StatementSyntax CountStmt(string kind, SyntaxNode at) { return SyntaxFactory.ParseStatement("global::SimShim.IlCounter.C(" + NewSite(kind, at) + ");"); }
    static StatementSyntax EnterStmt() { return SyntaxFactory.ParseStatement("global::SimShim.IlCounter.Enter();"); }
    static StatementSyntax ExitStmt() { return SyntaxFactory.ParseStatement("global::SimShim.IlCounter.Exit();"); }

    BlockSyntax Injected(StatementSyntax node, string kind, SyntaxNode at)
    {
        var inj = CountStmt(kind, at);
        var block = node as BlockSyntax;
        if (block != null) return block.WithStatements(block.Statements.Insert(0, inj));
        return SyntaxFactory.Block(inj, node);
    }

    BlockSyntax MethodBody(BlockSyntax body, string kind, SyntaxNode at)
    {
        return SyntaxFactory.Block(CountStmt(kind, at), EnterStmt(),
            SyntaxFactory.TryStatement(body, default(SyntaxList<CatchClauseSyntax>), SyntaxFactory.FinallyClause(SyntaxFactory.Block(ExitStmt()))));
    }

    BlockSyntax ExprBody(ExpressionSyntax e, bool returns, string kind, SyntaxNode at)
    {
        StatementSyntax st = returns ? (StatementSyntax)SyntaxFactory.ReturnStatement(SyntaxFactory.Token(SyntaxKind.ReturnKeyword).WithTrailingTrivia(SyntaxFactory.Space), e, SyntaxFactory.Token(SyntaxKind.SemicolonToken)) : SyntaxFactory.ExpressionStatement(e);
        return SyntaxFactory.Block(CountStmt(kind, at), EnterStmt(),
            SyntaxFactory.TryStatement(SyntaxFactory.Block(st), default(SyntaxList<CatchClauseSyntax>), SyntaxFactory.FinallyClause(SyntaxFactory.Block(ExitStmt()))));
    }

    // ---- methods ----------------------------------------------------------------------------------------------
    public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        if (node.Body == null && node.ExpressionBody == null) return base.VisitMethodDeclaration(node);
        string name = node.Identifier.ValueText;
        _ctx.Push(name);
        var at = (SyntaxNode)node.Body ?? node.ExpressionBody;
        var n = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node);
        BlockSyntax body;
        if (n.Body != null) body = MethodBody(n.Body, "method", at);
        else
        {
            var pt = n.ReturnType as PredefinedTypeSyntax;
            bool isVoid = pt != null && pt.Keyword.IsKind(SyntaxKind.VoidKeyword);
            body = ExprBody(n.ExpressionBody.Expression, !isVoid, "method", at);
        }
        _ctx.Pop();
        return n.WithExpressionBody(null).WithSemicolonToken(default(SyntaxToken)).WithBody(body);
    }

    public override SyntaxNode VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
    {
        if (node.Body == null) return base.VisitConstructorDeclaration(node);
        _ctx.Push(node.Identifier.ValueText + ".ctor");
        var at = node.Body;
        var n = (ConstructorDeclarationSyntax)base.VisitConstructorDeclaration(node);
        var r = n.WithBody(MethodBody(n.Body, "ctor", at));
        _ctx.Pop();
        return r;
    }

    public override SyntaxNode VisitDestructorDeclaration(DestructorDeclarationSyntax node)
    {
        if (node.Body == null) return base.VisitDestructorDeclaration(node);
        _ctx.Push("~" + node.Identifier.ValueText);
        var at = node.Body;
        var n = (DestructorDeclarationSyntax)base.VisitDestructorDeclaration(node);
        var r = n.WithBody(MethodBody(n.Body, "dtor", at));
        _ctx.Pop();
        return r;
    }

    public override SyntaxNode VisitOperatorDeclaration(OperatorDeclarationSyntax node)
    {
        if (node.Body == null && node.ExpressionBody == null) return base.VisitOperatorDeclaration(node);
        _ctx.Push("operator" + node.OperatorToken.ValueText);
        var at = (SyntaxNode)node.Body ?? node.ExpressionBody;
        var n = (OperatorDeclarationSyntax)base.VisitOperatorDeclaration(node);
        BlockSyntax body = n.Body != null ? MethodBody(n.Body, "operator", at) : ExprBody(n.ExpressionBody.Expression, true, "operator", at);
        _ctx.Pop();
        return n.WithExpressionBody(null).WithSemicolonToken(default(SyntaxToken)).WithBody(body);
    }

    public override SyntaxNode VisitConversionOperatorDeclaration(ConversionOperatorDeclarationSyntax node)
    {
        if (node.Body == null && node.ExpressionBody == null) return base.VisitConversionOperatorDeclaration(node);
        _ctx.Push("conversion");
        var at = (SyntaxNode)node.Body ?? node.ExpressionBody;
        var n = (ConversionOperatorDeclarationSyntax)base.VisitConversionOperatorDeclaration(node);
        BlockSyntax body = n.Body != null ? MethodBody(n.Body, "operator", at) : ExprBody(n.ExpressionBody.Expression, true, "operator", at);
        _ctx.Pop();
        return n.WithExpressionBody(null).WithSemicolonToken(default(SyntaxToken)).WithBody(body);
    }

    public override SyntaxNode VisitPropertyDeclaration(PropertyDeclarationSyntax node)
    {
        _ctx.Push(node.Identifier.ValueText);
        try
        {
            if (node.ExpressionBody == null) return base.VisitPropertyDeclaration(node);
            var at = node.ExpressionBody;
            var n = (PropertyDeclarationSyntax)base.VisitPropertyDeclaration(node);
            var acc = SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration, ExprBody(n.ExpressionBody.Expression, true, "getter", at));
            return n.WithExpressionBody(null).WithSemicolonToken(default(SyntaxToken)).WithAccessorList(SyntaxFactory.AccessorList(SyntaxFactory.SingletonList(acc)));
        }
        finally { _ctx.Pop(); }
    }

    public override SyntaxNode VisitIndexerDeclaration(IndexerDeclarationSyntax node)
    {
        _ctx.Push("this[]");
        try
        {
            if (node.ExpressionBody == null || node.AccessorList != null) return base.VisitIndexerDeclaration(node);
            var at = node.ExpressionBody;
            var n = (IndexerDeclarationSyntax)base.VisitIndexerDeclaration(node);
            var acc = SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration, ExprBody(n.ExpressionBody.Expression, true, "getter", at));
            return n.WithExpressionBody(null).WithSemicolonToken(default(SyntaxToken)).WithAccessorList(SyntaxFactory.AccessorList(SyntaxFactory.SingletonList(acc)));
        }
        finally { _ctx.Pop(); }
    }

    public override SyntaxNode VisitAccessorDeclaration(AccessorDeclarationSyntax node)
    {
        if (node.Body == null) return base.VisitAccessorDeclaration(node);
        var at = node.Body;
        var n = (AccessorDeclarationSyntax)base.VisitAccessorDeclaration(node);
        return n.WithBody(MethodBody(n.Body, "accessor", at));
    }

    // ---- lambdas ----------------------------------------------------------------------------------------------
    static SyntaxNode SetBody(AnonymousFunctionExpressionSyntax n, CSharpSyntaxNode body)
    {
        var a = n as AnonymousMethodExpressionSyntax; if (a != null) return a.WithBody(body);
        var p = n as ParenthesizedLambdaExpressionSyntax; if (p != null) return p.WithBody(body);
        return ((SimpleLambdaExpressionSyntax)n).WithBody(body);
    }
    SyntaxNode Lambda(AnonymousFunctionExpressionSyntax node, Func<SyntaxNode> baseVisit)
    {
        var type = _sm.GetTypeInfo(node).ConvertedType as INamedTypeSymbol;
        var at = node.Body;
        _ctx.Push((_ctx.Count > 0 ? _ctx.Peek() : "?") + "/lambda");
        var n = (AnonymousFunctionExpressionSyntax)baseVisit();
        try
        {
            var bb = n.Body as BlockSyntax;
            if (bb != null) return SetBody(n, Injected(bb, "lambda", at));
            if (type == null || type.DelegateInvokeMethod == null) { Unresolved++; return n; }
            return SetBody(n, ExprBody((ExpressionSyntax)n.Body, !type.DelegateInvokeMethod.ReturnsVoid, "lambda", at));
        }
        finally { _ctx.Pop(); }
    }
    public override SyntaxNode VisitAnonymousMethodExpression(AnonymousMethodExpressionSyntax node) { return Lambda(node, () => base.VisitAnonymousMethodExpression(node)); }
    public override SyntaxNode VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node) { return Lambda(node, () => base.VisitParenthesizedLambdaExpression(node)); }
    public override SyntaxNode VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node) { return Lambda(node, () => base.VisitSimpleLambdaExpression(node)); }

    // ---- control flow -----------------------------------------------------------------------------------------
    public override SyntaxNode VisitIfStatement(IfStatementSyntax node)
    {
        var at = node.Statement;
        var n = (IfStatementSyntax)base.VisitIfStatement(node);
        return n.WithStatement(Injected(n.Statement, "if", at));
    }

    public override SyntaxNode VisitElseClause(ElseClauseSyntax node)
    {
        var at = node.Statement;
        var n = (ElseClauseSyntax)base.VisitElseClause(node);
        if (n.Statement.Kind() == SyntaxKind.IfStatement) return n;      // "else if" is left alone
        return n.WithStatement(Injected(n.Statement, "else", at));
    }

    public override SyntaxNode VisitDoStatement(DoStatementSyntax node)
    {
        var at = node.Statement; var n = (DoStatementSyntax)base.VisitDoStatement(node);
        return n.WithStatement(Injected(n.Statement, "do", at));
    }
    public override SyntaxNode VisitWhileStatement(WhileStatementSyntax node)
    {
        var at = node.Statement; var n = (WhileStatementSyntax)base.VisitWhileStatement(node);
        return n.WithStatement(Injected(n.Statement, "while", at));
    }
    public override SyntaxNode VisitForEachStatement(ForEachStatementSyntax node)
    {
        var at = node.Statement; var n = (ForEachStatementSyntax)base.VisitForEachStatement(node);
        return n.WithStatement(Injected(n.Statement, "foreach", at));
    }
    public override SyntaxNode VisitForStatement(ForStatementSyntax node)
    {
        var at = node.Statement; var n = (ForStatementSyntax)base.VisitForStatement(node);
        return n.WithStatement(Injected(n.Statement, "for", at));
    }

    public override SyntaxNode VisitUsingStatement(UsingStatementSyntax node)
    {
        var at = node.Statement; var n = (UsingStatementSyntax)base.VisitUsingStatement(node);
        return n.WithStatement(Injected(n.Statement, "using", at));
    }

    public override SyntaxNode VisitGotoStatement(GotoStatementSyntax node)
    {
        if (node.CaseOrDefaultKeyword.Kind() != SyntaxKind.None) return base.VisitGotoStatement(node);
        var n = (GotoStatementSyntax)base.VisitGotoStatement(node);
        return Injected(n, "goto", node);
    }

    public override SyntaxNode VisitSwitchSection(SwitchSectionSyntax node)
    {
        SyntaxNode at = node.Statements.Count > 0 ? (SyntaxNode)node.Statements[0] : node;
        var n = (SwitchSectionSyntax)base.VisitSwitchSection(node);
        if (n.Statements.Count > 0) n = n.WithStatements(n.Statements.Insert(0, CountStmt("case", at)));
        return n;
    }

    static SyntaxToken SpacedId(string id) { return SyntaxFactory.Identifier(SyntaxFactory.TriviaList(SyntaxFactory.Space), SyntaxKind.IdentifierToken, id, id, SyntaxFactory.TriviaList()); }
    static readonly string Filter = "!(IDENT is global::SimShim.ScriptTooComplexException)";
    public override SyntaxNode VisitCatchClause(CatchClauseSyntax node)
    {
        var at = node.Block;
        var n = (CatchClauseSyntax)base.VisitCatchClause(node);
        string id = "ilex_" + (Sites.Count);
        if (n.Declaration == null)
            n = n.WithDeclaration(SyntaxFactory.CatchDeclaration(SyntaxFactory.ParseTypeName("global::System.Exception"), SpacedId(id)));
        else if (n.Declaration.Identifier.IsKind(SyntaxKind.None))
            n = n.WithDeclaration(n.Declaration.WithIdentifier(SpacedId(id)));
        else id = n.Declaration.Identifier.ValueText;
        var cond = SyntaxFactory.ParseExpression(Filter.Replace("IDENT", id));
        if (n.Filter == null) n = n.WithFilter(SyntaxFactory.CatchFilterClause(cond));
        else n = n.WithFilter(SyntaxFactory.CatchFilterClause(SyntaxFactory.BinaryExpression(SyntaxKind.LogicalAndExpression,
            SyntaxFactory.ParenthesizedExpression(n.Filter.FilterExpression), cond)));
        return n.WithBlock(Injected(n.Block, "catch", at));
    }

    public override SyntaxNode VisitFinallyClause(FinallyClauseSyntax node)
    {
        var at = node.Block;
        var n = (FinallyClauseSyntax)base.VisitFinallyClause(node);
        // game: finally { if (!IsDead()) { <counter>; body } }
        var inner = SyntaxFactory.IfStatement(SyntaxFactory.ParseExpression("!global::SimShim.IlCounter.IsDead()"), Injected(n.Block, "finally", at));
        return n.WithBlock(SyntaxFactory.Block(inner));
    }
}
