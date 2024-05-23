using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using Antlr4.Runtime.Tree;
using Moirai.Parser;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Antlr4CodeCompletion.Core.CodeCompletion;

public static class MoiraiCodeCompletion
{
    private static readonly HashSet<int> IgnoredTokens = new HashSet<int>()
    {
        // moirai_lexer.DOT,
        // moirai_lexer.ID,
        moirai_lexer.AT,
        moirai_lexer.PAREN_OPEN,
        moirai_lexer.PAREN_CLOSE,
        moirai_lexer.EQ,
        moirai_lexer.LINE_BREAK,
    };

    public static void SetupMoiraiCompletion(string expression, out moirai_lexer lexer, out MoiraiParser parser,
        out CodeCompletionCore core)
    {
        var inputStream = new AntlrInputStream(expression);
        lexer = new moirai_lexer(inputStream);
        var tokenStream = new CommonTokenStream(lexer);
        parser = new MoiraiParser(tokenStream);
        parser.Interpreter.PredictionMode = PredictionMode.LL_EXACT_AMBIG_DETECTION;

        SetupMoiraiCompletion(parser, out core);
    }

    public static void SetupMoiraiCompletion(MoiraiParser parser, out CodeCompletionCore core)
    {
        // Tell the engine to return certain rules to us, which we could use to look up values in a symbol table.
        var preferredRules = new HashSet<int>()
        {
            MoiraiParser.RULE_property_id,
            MoiraiParser.RULE_var,
            MoiraiParser.RULE_call,
            MoiraiParser.RULE_raw_call,
            // MoiraiParser.RULE_path,
            MoiraiParser.RULE_value,
        };

        // Ignore operators and the generic ID token.
        core = new CodeCompletionCore(parser, preferredRules, IgnoredTokens);
    }


    class TokenIndexFinder : MoiraiParserBaseListener
    {
        private readonly Position m_Position;

        public TokenIndexFinder(StoryParser.AstVisitor.FilePosition position)
        {
            m_Position = position.ToLspPosition();
        }

        private bool _exactMatch;
        private ITerminalNode? _prev;
        public override void VisitTerminal(ITerminalNode node)
        {
            var r = TokenVisitor.GetRange(node.Symbol);
            if (!_exactMatch && r.Contains(m_Position))
            {
                // if the prev token is not whitespace, take next one: $x.|prop
                // else keep this one, eg. __|prop
                _exactMatch = true;
                if (m_Position == r.Start && _prev != null && _prev.Symbol.TokenIndex == node.Symbol.TokenIndex - 1 && !IgnoredTokens.Contains(_prev.Symbol.Type))
                {
                    TokenIndex = _prev.Symbol.TokenIndex;
                }
                else
                    TokenIndex = node.Symbol.TokenIndex;
            }
            if (!_exactMatch && r.Start.Line == m_Position.Line)
            {
                if(TokenIndex ==  -1 && m_Position.Character < r.Start.Character)
                    TokenIndex = node.Symbol.TokenIndex;
                    
                // if (m_Position.Character > r.End.Character)
                //     TokenIndex = node.Symbol.TokenIndex+1;
            }

            _prev = node;
        }

        public int TokenIndex { get; private set; } = -1;
    }
    public static int FindTokenIndex(MoiraiParser parser, Position position)
    {
        var finder = new TokenIndexFinder(position.ToParserPosition());
        parser.AddParseListener(finder);
        parser.InputStream.Seek(0);
        
        var root= parser.r();
        var pos = finder.TokenIndex;
        return pos;
    }
}
