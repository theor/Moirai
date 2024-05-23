using Antlr4CodeCompletion.Core.CodeCompletion;
using NUnit.Framework;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

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
    [Test]
    [TestCase(2,4, "PROP")]// |prop
    [TestCase(2,2, "PROP")]// |  prop
    [TestCase(2,8, "PROP")]// prop|
    [TestCase(9,4, "SET")]// |set
    [TestCase(9,6, "SET")]// se|t
    [TestCase(9,7, "SET")]// set|
    [TestCase(9,8, "VAR_ID")]// set |
    [TestCase(9,9, "VAR_ID")]// set $|
    [TestCase(9,10, "VAR_ID")]// set $p| not dot but continue $p
    [TestCase(9,11, "ID")]// set $p.|
    public void FindTokenIndex(int line, int column, string expectedToken)
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
        CompletionParams request = new CompletionParams { };
        var i = MoiraiCodeCompletion.FindTokenIndex(parser, new Position(line, column));
        Console.WriteLine();
        var symbolicName = parser.Vocabulary.GetSymbolicName(parser.TokenStream.Get(i).Type);
        Console.WriteLine($"result:{i} {symbolicName}");
        
        CandidatesCollection candidates;
        candidates = core.CollectCandidates(i, null);
        PrintCandidates("comp", candidates, parser);
        
        TokenIndexFromLineColumn(tree);
        Assert.That(symbolicName, Is.EqualTo(expectedToken));
        // Assert.AreEqual(8, i);
    }
}