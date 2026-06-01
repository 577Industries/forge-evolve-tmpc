// CyclomaticComplexity — Roslyn-based max-method CC measurement.
//
// Uses the SAME definition the Discovery engine pre-registers (CSharpAnalyzer): McCabe with
// short-circuit operators. CC = 1 + count of decision points, where decision points are: if, for,
// foreach, while, do, catch, ?:, switch case labels (const + pattern), switch-expression arms,
// conditional access (?.), and the boolean operators &&, ||, ??. This lets the Transformation
// engine report a verifiable before/after complexity reduction (legacy 49 -> modern < 10).

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace ForgeEvolve.Transformation;

/// <summary>One method's measured cyclomatic complexity.</summary>
public sealed record MethodComplexity(string TypeName, string MethodName, int CyclomaticComplexity);

/// <summary>Stateless cyclomatic-complexity analyzer over C# source text.</summary>
public static class CyclomaticComplexity
{
    /// <summary>Measure the CC of every method/accessor/local-function in the given source files.</summary>
    public static IReadOnlyList<MethodComplexity> Measure(IEnumerable<(string Path, string Content)> files)
    {
        var results = new List<MethodComplexity>();
        foreach ((string path, string content) in files)
        {
            SyntaxNode root = CSharpSyntaxTree.ParseText(content).GetRoot();
            foreach (BaseMethodDeclarationSyntax method in root.DescendantNodes().OfType<BaseMethodDeclarationSyntax>())
            {
                results.Add(MeasureMethod(method));
            }
            foreach (LocalFunctionStatementSyntax local in root.DescendantNodes().OfType<LocalFunctionStatementSyntax>())
            {
                SyntaxNode? body = (SyntaxNode?)local.Body ?? local.ExpressionBody;
                int cc = 1 + (body is null ? 0 : CountDecisionPoints(body));
                results.Add(new MethodComplexity(EnclosingTypeName(local), local.Identifier.Text, cc));
            }
        }
        return results;
    }

    /// <summary>The maximum method CC across the given source files (0 if none found).</summary>
    public static int MaxMethodComplexity(IEnumerable<(string Path, string Content)> files)
    {
        int max = 0;
        foreach (MethodComplexity m in Measure(files))
        {
            if (m.CyclomaticComplexity > max) max = m.CyclomaticComplexity;
        }
        return max;
    }

    private static MethodComplexity MeasureMethod(BaseMethodDeclarationSyntax method)
    {
        SyntaxNode? body = (SyntaxNode?)method.Body ?? method.ExpressionBody;
        int cc = 1 + (body is null ? 0 : CountDecisionPoints(body));
        return new MethodComplexity(EnclosingTypeName(method), MethodName(method), cc);
    }

    private static string MethodName(BaseMethodDeclarationSyntax method) => method switch
    {
        MethodDeclarationSyntax m => m.Identifier.Text,
        ConstructorDeclarationSyntax c => c.Identifier.Text + ".ctor",
        _ => method.Kind().ToString(),
    };

    private static string EnclosingTypeName(SyntaxNode node)
    {
        for (SyntaxNode? n = node.Parent; n is not null; n = n.Parent)
        {
            if (n is TypeDeclarationSyntax t) return t.Identifier.Text;
        }
        return "<global>";
    }

    private static int CountDecisionPoints(SyntaxNode node)
    {
        int count = 0;
        foreach (SyntaxNode n in node.DescendantNodes())
        {
            switch (n)
            {
                case IfStatementSyntax:
                case ForStatementSyntax:
                case ForEachStatementSyntax:
                case ForEachVariableStatementSyntax:
                case WhileStatementSyntax:
                case DoStatementSyntax:
                case CatchClauseSyntax:
                case ConditionalExpressionSyntax:       // ?:
                case CasePatternSwitchLabelSyntax:       // case <pattern>:
                case CaseSwitchLabelSyntax:              // case <const>:
                case SwitchExpressionArmSyntax:          // switch-expression arms
                case ConditionalAccessExpressionSyntax:  // ?. (short-circuit path)
                    count++;
                    break;
                case BinaryExpressionSyntax bin:
                    if (bin.IsKind(SyntaxKind.LogicalAndExpression)
                        || bin.IsKind(SyntaxKind.LogicalOrExpression)
                        || bin.IsKind(SyntaxKind.CoalesceExpression))
                    {
                        count++;
                    }
                    break;
            }
        }
        return count;
    }
}
