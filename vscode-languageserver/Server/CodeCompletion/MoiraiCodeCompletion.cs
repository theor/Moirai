using System.Diagnostics;
using System.Text;
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
        moirai_lexer.SPACE,
        
        MoiraiParser.Eof,
    };

    private static readonly HashSet<int> PreferredRules = new HashSet<int>()
    {
        MoiraiParser.RULE_type_id,
        MoiraiParser.RULE_fun_id,
        // MoiraiParser.RULE_var,
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
        parser.ErrorHandler = new NoRecoveryStrategy();
        SetupMoiraiCompletion(parser, out core);
    }

    public class NoRecoveryStrategy : DefaultErrorStrategy
    {
        public override void Recover(Parser recognizer, RecognitionException e)
        {
            // if (this.lastErrorIndex == recognizer.InputStream.Index && this.lastErrorStates != null && this.lastErrorStates.Contains(recognizer.State))
            // recognizer.Consume();
            // this.lastErrorIndex = recognizer.InputStream.Index;
        }
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

        public TokenIndexFinder(FilePosition position)
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
        bool IsOfType(IToken token, moirai_lexer.Tokens type) => token.Type == (int)type;
        IToken? _prev = null;
        bool _exactMatch = false;
        int TokenIndex = -1;
        for (int i = 0; i < parser.TokenStream.Size; i++)
        {
            var t = parser.TokenStream.Get(i);
            var r = TokenVisitor.GetRange(t);
            var type = (moirai_lexer.Tokens)t.Type;
            if (!_exactMatch && r.Contains(position) && 
                !IsOfType(t, moirai_lexer.Tokens.Space))
            {
                _exactMatch = true;
                if (position == r.Start && _prev != null && _prev.TokenIndex == t.TokenIndex - 1 &&
                    !IsOfType(_prev, moirai_lexer.Tokens.Line_break) && !IsOfType(_prev, moirai_lexer.Tokens.Space) &&
                    !IgnoredTokens.Contains(_prev.Type))
                {
                    TokenIndex = _prev.TokenIndex;
                }
                else
                    TokenIndex = t.TokenIndex;
            }

            if (!_exactMatch && r.Start.Line == position.Line)
            {
                if (TokenIndex == -1 && position.Character < r.Start.Character)
                    TokenIndex = t.TokenIndex;

                // if (m_Position.Character > r.End.Character)
                //     TokenIndex = node.Symbol.TokenIndex+1;
            }

            _prev = t;
        }

        return TokenIndex;
        // var finder = new TokenIndexFinder(position.ToParserPosition());
        // parser.AddParseListener(finder);
        // parser.InputStream.Seek(0);
        //
        // var root = parser.r();
        // var pos = finder.TokenIndex;
        // return pos;
    }

    public static async Task<List<CompletionItem>> Complete(ILogger logger, MoiraiParser parser,
        CandidatesCollection candidates,
        MoiraiDocument document, Position position, int tokenIndex)
    {
        var items = new List<CompletionItem>();

        foreach (var (key, value) in candidates.Rules)
        {
            var ruleName = parser.RuleNames[key];
            switch ((MoiraiParser.Rules)key)
            {
                case MoiraiParser.Rules.Fun_id:
                    DefinitionsToCompletions(document.Linker.GetDefinitions(position,
                        TokenVisitor.DefinitionType.Function));
                    break;
                case MoiraiParser.Rules.Var_id_read:
                    DefinitionsToCompletions(document.Linker.GetDefinitions(position,
                        TokenVisitor.DefinitionType.VariableScope));
                    DefinitionsToCompletions(document.Linker.TypeDefinitions.Values);
                    break;
                case MoiraiParser.Rules.Dot_property:
                    var prevToken = parser.TokenStream.Get(tokenIndex - 1);
                    if (prevToken != null && prevToken.Type == moirai_lexer.VAR_ID)
                    {
                        TokenVisitor.VariableDefinition? completedVariable =
                            document.Linker.GetDefinitions(TokenVisitor.GetRange(prevToken).Start,
                                    TokenVisitor.DefinitionType.Variable)
                                .FirstOrDefault(d => d.Name == prevToken.Text) as TokenVisitor.VariableDefinition;

                        if (completedVariable == null)
                            break;
                        EntityType? varType = Database.Instance.GetEntityType(
                            ((TokenVisitor.VariableDefinition)completedVariable)
                            .Data.Type);
                        if (varType?.Properties != null)
                        {
                            foreach (var property in varType.Properties)
                            {
                                if (property.Name != null)
                                {
                                    var sb = new StringBuilder();
                                    items.Add(new CompletionItem
                                    {
                                        Kind = CompletionItemKind.Property,
                                        // Label = $"{property.Name} (entity {Database.Instance.GetEntityTypeName(property.PropertyId.TypeId)} {{ prop {property.Name}: {Database.Instance.Printer.Print(property.Type)} }})",
                                        Label = $"{property.Name} (entity {Database.Instance.GetEntityTypeName(property.PropertyId.TypeId)} {{ prop {property.Name}: {Database.Instance.Printer.Print(property.Type)} }})",
                                        InsertText = property.Name,
                                        Detail = $"{varType.Name}.{property.Name}: {property.Type}",
                                    });}
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
                        Label = ruleName, InsertText = ruleName, Detail = "RULE"
                    });
                    break;
            }
        }

        foreach (var (key, value) in candidates.Tokens)
        {
            var tokenName = parser.Vocabulary.GetDisplayName(key).Trim('\'');
            items.Add(new CompletionItem
            {
                Label = tokenName,
                Detail =
                    $"TOKEN {(value == null ? null : string.Join(",", value.Select(parser.Vocabulary.GetSymbolicName)))}",
                InsertText = tokenName + ' ',
            });
        }

        foreach (var (key, succ) in candidates.RulePositions)
        {
            items.Add(new CompletionItem
            {
                Label = $"_rulepos: {parser.RuleNames[key]} [{string.Join(", ", succ.Select(i => i))}]",
                InsertText = parser.RuleNames[key],
            });
        }

        return items;

        void DefinitionsToCompletions(IEnumerable<TokenVisitor.Definition> symbolTable)
        {
            foreach (var definition in symbolTable.DistinctBy(x => x.Name))
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
