using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using Antlr4.Runtime.Tree;
using Antlr4CodeCompletion.Core.CodeCompletion;
using Antlr4CodeCompletion.CoreUnitTest.Grammar;
using Antlr4CodeCompletion.CoreUnitTest.Utils;
using Moirai.Parser;
using NFluent;
using NUnit.Framework;

namespace Antlr4CodeCompletion.CoreUnitTest.CodeCompletion;

public class CodeCompletionCoreUnitTestsBase
{
    protected static void PrintCandidates(string label, CandidatesCollection candidates, Parser parser)
    {
        Console.WriteLine(label);
        foreach (var (key, succ) in candidates.Tokens)
        {
            Console.WriteLine($"  {parser.Vocabulary.GetDisplayName(key)}: {string.Join(", ", succ.Select(parser.Vocabulary.GetDisplayName))}");
        }

        foreach (var (key, succ) in candidates.Rules)
        {
            Console.WriteLine($"  {parser.RuleNames[key]} <- callstack: {string.Join(", ", succ.Select(i => parser.RuleNames[i]))}");

        }
    }

    class TokenIndexWalker : MoiraiParserBaseListener
    {
        private string curLine = "";
        private string curIndexLine = "";
        private string curIndexLabels = "";
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
                while (curLine.Length < token.Column)
                    curLine += " ";
                while (curIndexLine.Length < token.Column)
                    curIndexLine += " ";
                curIndexLine += "*";
                curIndexLabels += token.TokenIndex + " ";
                curLine += token.Text;
            }
            // Console.WriteLine($"{token.TokenIndex}: {token.Text.ReplaceLineEndings("\\n")} at {token.Line}:{token.Column}");
        }

        public void Flush()
        {
            Console.WriteLine("|     |" + curIndexLine + " // " + curIndexLabels);
            Console.Write($"|{curLineIndex,4} |{curLine}");
            curLine = "";
            curIndexLine = "";
            curIndexLabels = "";
        }
    }
    protected int TokenIndexFromLineColumn(int line, int column, IParseTree t)
    {
        var walker = new ParseTreeWalker();
        var l = new TokenIndexWalker();
        walker.Walk(l, t);
        l.Flush();
        return 0;
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
        TokenIndexFromLineColumn(-1, -1, tree);
        return (core, parser, tree);
    }
}

public class MoiraiCodeCompletionTests : CodeCompletionCoreUnitTestsBase
{
    [Test]
    public void Complete()
    {
        var (core,parser, tree) = Setup(@"
entity Person {
    prop birthplace: string

}

@start
event start {
    create Person $p: ('{random(Name)}')
    set $p.birthplace =  '{random(City)}'
}
");
        var candidates = core.CollectCandidates(0, null);
        PrintCandidates("1) At the input start.", candidates, parser);
        
        candidates = core.CollectCandidates(13, null);
        PrintCandidates("2) prop birthplace: ", candidates, parser);
        
        candidates = core.CollectCandidates(50, null);
        PrintCandidates("3) set $p", candidates, parser);
        
        candidates = core.CollectCandidates(51, null);
        PrintCandidates("4) set $p.", candidates, parser);
        
        candidates = core.CollectCandidates(52, null);
        PrintCandidates("5) set $p.birthplace", candidates, parser);
        
        candidates = core.CollectCandidates(54, null);
        PrintCandidates("6) set $p.birthplace =", candidates, parser);
        
        candidates = core.CollectCandidates(56, null);
        PrintCandidates("7) set $p.birthplace = 'asd", candidates, parser);
        
        
    }
}
