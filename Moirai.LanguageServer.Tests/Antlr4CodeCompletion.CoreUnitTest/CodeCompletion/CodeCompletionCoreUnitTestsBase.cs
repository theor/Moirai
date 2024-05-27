using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using Antlr4.Runtime.Tree;
using Antlr4CodeCompletion.Core.CodeCompletion;
using Antlr4CodeCompletion.CoreUnitTest.Grammar;
using Antlr4CodeCompletion.CoreUnitTest.Utils;
using Moirai.Parser;
using NFluent;

namespace Antlr4CodeCompletion.CoreUnitTest.CodeCompletion;

public class CodeCompletionCoreUnitTestsBase
{
    protected static void PrintCandidates(string label, CandidatesCollection candidates, Parser parser)
    {
        Console.WriteLine(label);
        foreach (var (key, succ) in candidates.Tokens)
        {
            Console.WriteLine($"  {parser.Vocabulary.GetSymbolicName(key)}: {string.Join(", ", succ.Select(parser.Vocabulary.GetSymbolicName))}");
        }

        foreach (var (key, succ) in candidates.Rules)
        {
            Console.WriteLine($"  {parser.RuleNames[key]} <- callstack: {string.Join(", ", succ.Select(i => parser.RuleNames[i]))}");

        }

        foreach (var (key, succ) in candidates.RulePositions)
        {
            Console.WriteLine($"  {parser.RuleNames[key]} [{string.Join(", ", succ.Select(i => i))}]");

        }
    }

    class TokenIndexWalker(int line, int column) : MoiraiParserBaseListener
    {
        private string sourceTextLine = "";
        private string curIndexLine = "";
        private string tokenIndicesLine = "";
        private int curLineIndex = -1;
        public override void VisitTerminal(ITerminalNode node)
        {
            var token = node.Symbol;
            if (token.Line != curLineIndex && token.Type != moirai_lexer.Eof)
            {
                if (curLineIndex != -1)
                    Flush();
                curLineIndex = token.Line;
            }
            {
                while (sourceTextLine.Length < token.Column)
                    sourceTextLine += " ";
                while (curIndexLine.Length < token.Column)
                    curIndexLine += curLineIndex == (line+1) && curIndexLine.Length == column ? "|": " ";
                curIndexLine += curLineIndex == (line+1) && curIndexLine.Length == column ? "+" : "*";
                tokenIndicesLine += token.TokenIndex + " ";
                sourceTextLine += token.Text;
            }
            // Console.WriteLine($"{token.TokenIndex}: {token.Text.ReplaceLineEndings("\\n")} at {token.Line}:{token.Column}");
        }

        public void Flush()
        {
            if (curLineIndex == line + 1)
            {
                Console.WriteLine("|     |" + curIndexLine + " // " + tokenIndicesLine);
                Console.Write($"|{curLineIndex,4} |{sourceTextLine}");
            }

            sourceTextLine = "";
            curIndexLine = "";
            tokenIndicesLine = "";
        }
    }

    protected void TokenIndexFromLineColumn(MoiraiParser.RContext tree, int line, int column)
    {
        var walker = new ParseTreeWalker();
        var l = new TokenIndexWalker(line, column);
        walker.Walk(l, tree);
        l.Flush();
    }

    protected (CodeCompletionCore core, MoiraiParser parser, MoiraiParser.RContext tree) Setup(string expression)
    {
        MoiraiCodeCompletion.SetupMoiraiCompletion(expression, out var lexer, out var parser, out var core);

        // Specify our entry point
        lexer.RemoveErrorListeners();
        parser.RemoveErrorListeners();

        var errorListener = new CountingErrorListener();
        parser.AddErrorListener(errorListener);
        var tree = parser.r();
        Check.That(errorListener.ErrorCount).IsEqualTo(0);
        return (core, parser, tree);
    }
}
