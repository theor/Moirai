using System.Diagnostics;
using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using Antlr4.Runtime.Tree;
using Microsoft.Extensions.Logging;
using Moirai.Parser;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Antlr4CodeCompletion.Core.CodeCompletion;

public static class MoiraiCodeCompletion
{
    private static readonly HashSet<int> IgnoredTokens = new HashSet<int>()
    {
        moirai_lexer.TYPE_ID,
        moirai_lexer.DOT,
        moirai_lexer.ID,
        moirai_lexer.AT,
        moirai_lexer.PAREN_OPEN,
        moirai_lexer.PAREN_CLOSE,
        moirai_lexer.EQ,
        moirai_lexer.LINE_BREAK,
    };

    private static readonly HashSet<int> PreferredRules = new HashSet<int>()
    {
        MoiraiParser.RULE_type_id,
        MoiraiParser.RULE_var,
        // MoiraiParser.RULE_call,
        // MoiraiParser.RULE_raw_call,
        // MoiraiParser.RULE_value,
        MoiraiParser.RULE_dot_property,
        MoiraiParser.RULE_var_id_read,
        // MoiraiParser.RULE_path,
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

        // Ignore operators and the generic ID token.
        core = new CodeCompletionCore(parser, PreferredRules, IgnoredTokens);
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
                if (m_Position == r.Start && _prev != null && _prev.Symbol.TokenIndex == node.Symbol.TokenIndex - 1 &&
                    !IgnoredTokens.Contains(_prev.Symbol.Type))
                {
                    TokenIndex = _prev.Symbol.TokenIndex;
                }
                else
                    TokenIndex = node.Symbol.TokenIndex;
            }

            if (!_exactMatch && r.Start.Line == m_Position.Line)
            {
                if (TokenIndex == -1 && m_Position.Character < r.Start.Character)
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

        var root = parser.r();
        var pos = finder.TokenIndex;
        return pos;
    }

    public static async Task<List<CompletionItem>> Complete(ILogger logger, MoiraiParser parser,
        CandidatesCollection candidates,
        MoiraiDocument document, Position position, int tokenIndex)
    {
        var items = new List<CompletionItem>();
        foreach (var (key, succ) in candidates.RulePositions)
        {
            items.Add(new CompletionItem
            {
                Label = $"  {parser.RuleNames[key]} [{string.Join(", ", succ.Select(i => i))}]",
                InsertText = parser.RuleNames[key],
            });
        }

        foreach (var (key, value) in candidates.Rules)
        {
            var ruleName = parser.RuleNames[key];
            switch ((MoiraiParser.Rules)key)
            {
                case MoiraiParser.Rules.Var_id_read:
                    // DefinitionsToCompletions(document.Definitions(position, TokenVisitor.DefinitionType.Variable));
                    break;
                case MoiraiParser.Rules.Dot_property:
                    var prevToken = parser.TokenStream.Get(tokenIndex - 1);
                    if (prevToken != null && prevToken.Type == moirai_lexer.VAR_ID)
                    {
                        TokenVisitor.VariableDefinition? completedVariable = null;
                        // document.Definitions(TokenVisitor.GetRange(prevToken).Start,
                                // TokenVisitor.DefinitionType.Variable)
                            // .FirstOrDefault(d => d.Name == prevToken.Text);

                        if (completedVariable == null)
                            break;
                        EntityType? varType = Database.Instance.GetEntityType(
                            ((TokenVisitor.VariableDefinition)completedVariable)
                            .VariableDeclaration.Type);
                        if (varType?.Properties != null)
                        {
                            foreach (var property in varType.Properties)
                            {
                                if (property.Name != null)
                                    items.Add(new CompletionItem
                                    {
                                        Label = property.Name,
                                        InsertText = property.Name,
                                        Detail = $"{varType.Name}.{property.Name}: {property.Type}",
                                    });
                            }
                        }
                        // items.Add(new CompletionItem
                        // {
                        //     Label = completedVariable.Name,
                        //     InsertText = completedVariable.Name,
                        //     Detail = completedVariable.Type.ToString(),
                        // });
                    }

                    break;
                case MoiraiParser.Rules.Type_id:
                    foreach (var type in Database.Instance.Types.Skip(1))
                    {
                        items.Add(new CompletionItem
                        {
                            Label = type.Name,
                            InsertText = type.Name,
                            Detail = "Type",
                        });
                    }
                    break;
                default:
                    logger.LogError("Rule not handled: {ruleName}", ruleName);
                    items.Add(new CompletionItem
                    {
                        Label = "r:" + ruleName, InsertText = ruleName,
                    });
                    break;
            }
        }

        foreach (var (key, value) in candidates.Tokens)
        {
            var tokenName = parser.Vocabulary.GetSymbolicName(key);
            items.Add(new CompletionItem
            {
                Label =
                    $"t:{tokenName} {(value == null ? null : string.Join(",", value.Select(parser.Vocabulary.GetSymbolicName)))}",
                InsertText = tokenName,
            });
        }

        return items;

        void DefinitionsToCompletions(IEnumerable<TokenVisitor.Definition> symbolTable)
        {
            foreach (var definition in symbolTable)
            {
                items.Add(new CompletionItem
                {
                    Detail = $"d:{definition.Type}",
                    Label = definition.Name, InsertText = definition.Name,
                });
            }
        }
    }
}
