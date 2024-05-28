using System.Collections;
using Antlr4CodeCompletion.Core.CodeCompletion;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Moirai.Parser;
using NUnit.Framework;
using NUnit.Framework.Internal;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Antlr4CodeCompletion.CoreUnitTest.CodeCompletion;

public class MoiraiCodeCompletionTests : CodeCompletionCoreUnitTestsBase
{
    [Test]
    public void Complete()
    {
        var (core, parser, tree) = Setup(@"
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

public class TokenIndexFinderTests : CodeCompletionCoreUnitTestsBase
{
    public class CompletionTestCase : TestCaseParameters
    {
        public CompletionTestCase(string code, int line, int column, moirai_lexer.Tokens expectedToken, moirai_lexer.Tokens? expectedCompletedToken = null, MoiraiParser.Rules? expectedCompletedRule = null)
            : base(new object[]{code, line, column, expectedToken, expectedCompletedToken, expectedCompletedRule})
        {
            this.TestName = $"({line}:{column}) find {expectedToken}";
            if (expectedCompletedToken.HasValue)
                TestName += $" token:{moirai_lexer.ruleNames[(int)expectedCompletedToken-1]}";
            if (expectedCompletedRule.HasValue)
                TestName += $" rule:{MoiraiParser.ruleNames[(int)expectedCompletedRule]}";
        }
        
        
    }

    private const string CodePersonBirthPlace = @"
entity Person {
    prop birthplace: string

}

@start
event start {
    create Person $p: ('{random(Name)}')
    set $p.birthplace =  '{random(Name)}'
}
";

    public static IEnumerable<CompletionTestCase> Cases
    {
        get
        {
            return new []
            {
                new CompletionTestCase(CodePersonBirthPlace, 2,2, moirai_lexer.Tokens.Prop, moirai_lexer.Tokens.Prop),
                new CompletionTestCase(CodePersonBirthPlace, 2, 4, moirai_lexer.Tokens.Prop, moirai_lexer.Tokens.Prop),// |  prop
                new CompletionTestCase(CodePersonBirthPlace, 2,8, moirai_lexer.Tokens.Prop, moirai_lexer.Tokens.Prop),// prop|
                new CompletionTestCase(CodePersonBirthPlace, 8,11, moirai_lexer.Tokens.Type_id, null, MoiraiParser.Rules.Type_id),// create |
                new CompletionTestCase(CodePersonBirthPlace, 9,4, moirai_lexer.Tokens.Set, moirai_lexer.Tokens.Set),// |set
                new CompletionTestCase(CodePersonBirthPlace, 9,6, moirai_lexer.Tokens.Set, moirai_lexer.Tokens.Set),// se|t
                new CompletionTestCase(CodePersonBirthPlace, 9,7, moirai_lexer.Tokens.Set, moirai_lexer.Tokens.Set),// set|
                new CompletionTestCase(CodePersonBirthPlace, 9,8, moirai_lexer.Tokens.Var_id, null, MoiraiParser.Rules.Var_id_read) ,// set |
                new CompletionTestCase(CodePersonBirthPlace, 9,9, moirai_lexer.Tokens.Var_id, null, MoiraiParser.Rules.Var_id_read) ,// set $|
                new CompletionTestCase(CodePersonBirthPlace, 9,10, moirai_lexer.Tokens.Var_id, null, MoiraiParser.Rules.Var_id_read),// set $p| not dot but continue $p
                new CompletionTestCase(CodePersonBirthPlace, 9,11, moirai_lexer.Tokens.Dot, null, MoiraiParser.Rules.Dot_property) ,// set $p.|
            };
        }
    }
    
    private readonly DocumentUri _uri = new DocumentUri("moirai", null, "test.sg", "asd", "qwe");

    [Test, TestCaseSource(nameof(Cases))]
    public async Task CompletionTestAsync(string code, int line, int column, moirai_lexer.Tokens expectedToken,
        moirai_lexer.Tokens? expectedCompletedToken, MoiraiParser.Rules? expectedCompletedRule)
    {
        
        var (core, parser, tree) = Setup(CodePersonBirthPlace);
        var position = new Position(line, column);
        var i = MoiraiCodeCompletion.FindTokenIndex(parser, position);
        CandidatesCollection candidates = core.CollectCandidates(i, null);

        var doc = new MoiraiDocument(_uri, new TextDocumentItem{Uri = _uri, LanguageId = "moirai", Text = CodePersonBirthPlace, Version = 1});
        ILogger logger = new FakeLogger(cw => Console.WriteLine(cw.ToString()));
        logger.LogCritical("TEST");
        await doc.Process(logger);
        var completionList = await MoiraiCodeCompletion.Complete(logger, parser, candidates, doc, position, i);
        foreach (var completionItem in completionList)
        {
            Console.WriteLine($"{completionItem.Label} {completionItem.Detail}");
        }
    }

    [Test,TestCaseSource(nameof(Cases))]
    public void FindTokenIndex2(string code, int line, int column, moirai_lexer.Tokens expectedToken, moirai_lexer.Tokens? expectedCompletedToken, MoiraiParser.Rules? expectedCompletedRule)
    {
        var (core, parser, tree) = Setup(CodePersonBirthPlace);
        var i = MoiraiCodeCompletion.FindTokenIndex(parser, new Position(line, column));
        Console.WriteLine();
        var symbolicName = parser.Vocabulary.GetSymbolicName(parser.TokenStream.Get(i).Type);
        Console.WriteLine($"result:{i} {symbolicName}");

        CandidatesCollection candidates = core.CollectCandidates(i, null);
        PrintCandidates("comp", candidates, parser);
        
        TokenIndexFromLineColumn(tree, line, column);
        Assert.That((moirai_lexer.Tokens)parser.TokenStream.Get(i).Type, Is.EqualTo(expectedToken));
        if(expectedCompletedToken.HasValue)
            Assert.That(candidates.Tokens.Keys.Select(x => (moirai_lexer.Tokens)x), Contains.Item(expectedCompletedToken));
        if(expectedCompletedRule.HasValue)
            Assert.That(candidates.Rules.Keys.Select(x => (MoiraiParser.Rules)x), Contains.Item(expectedCompletedRule));
        // Assert.AreEqual(8, i);
    }
}
