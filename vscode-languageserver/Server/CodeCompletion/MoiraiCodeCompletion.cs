using Antlr4.Runtime;
using Antlr4.Runtime.Atn;
using Moirai.Parser;

namespace Antlr4CodeCompletion.Core.CodeCompletion;

public static class MoiraiCodeCompletion
{
    public static void SetupCompletion()
    {
        
    }

    public static void SetupMoiraiCompletion(string expression, out moirai_lexer lexer, out MoiraiParser parser,
        out CodeCompletionCore core)
    {
        var inputStream = new AntlrInputStream(expression);
        lexer = new moirai_lexer(inputStream);
        var tokenStream = new CommonTokenStream(lexer);
        parser = new MoiraiParser(tokenStream);
        parser.Interpreter.PredictionMode = PredictionMode.LL_EXACT_AMBIG_DETECTION;


        // Tell the engine to return certain rules to us, which we could use to look up values in a symbol table.
        var preferredRules = new HashSet<int>()
        {
            MoiraiParser.RULE_var,
            MoiraiParser.RULE_call,
            MoiraiParser.RULE_raw_call,
            MoiraiParser.RULE_path,
            MoiraiParser.RULE_value,
        };

        // Ignore operators and the generic ID token.
        var ignoredTokens = new HashSet<int>()
        {
            moirai_lexer.ID,
            moirai_lexer.AT,
            moirai_lexer.PAREN_OPEN,
            moirai_lexer.PAREN_CLOSE,
            moirai_lexer.EQ,
            moirai_lexer.LINE_BREAK,
        };
        core = new CodeCompletionCore(parser, preferredRules, ignoredTokens);
    }
}
