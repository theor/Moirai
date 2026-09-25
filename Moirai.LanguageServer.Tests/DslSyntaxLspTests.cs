using Microsoft.Extensions.Logging.Testing;
using NUnit.Framework;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Moirai.LanguageServer.Tests;

/// The corpus uses none of block comments, `pick ... else`, `chance(...) { }`, `random_weighted` without a
/// total, or aggregates, so the golden suites never see them. These pin that the formatter settles on them
/// and leaves their meaning alone, and that a block comment highlights one line at a time.
[NonParallelizable]
public class DslSyntaxLspTests
{
    const string Story = @"/// A thing.
entity Thing {
    prop x: number
    prop alive: bool
}
event e {
    /* an indented comment
       over two lines */
    pick Thing $t: (x = 99)   else {
        record('none')
    }
    chance(10%)   {
        record('lucky')
    }
    random_weighted   {
        3 => record('a')
        1 => record('b')
    }
    var $n: count Thing $u: (alive, x > 1)
    var $m: avg Thing $u: (alive, $u.x)
}
";

    static async Task<(MoiraiCache cache, DocumentUri uri)> Open(string content)
    {
        var uri = new DocumentUri("file", null, "/dsl.sg", null, null);
        var cache = new MoiraiCache(new FakeLogger<MoiraiCache>());
        await cache.OnOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem { Uri = uri, LanguageId = "moirai", Text = content, Version = 1 },
        });
        return (cache, uri);
    }

    static async Task<string> Format(string content)
    {
        var (cache, uri) = await Open(content);
        var handler = new MoiraiDocumentFormattingHandler(new FakeLogger<MoiraiDocumentFormattingHandler>(), cache);
        var edits = await handler.Handle(
            new DocumentFormattingParams { TextDocument = new TextDocumentIdentifier(uri) }, default);
        return edits == null ? content : TextEdits.Apply(content, edits.ToList());
    }

    [Test]
    public async Task Formatting_settles_and_keeps_the_meaning()
    {
        var once = await Format(Story);
        Assert.That(await Format(once), Is.EqualTo(once), once);
        Assert.That(once, Does.Contain("pick Thing $t: (x = 99) else {"), once);
        Assert.That(once, Does.Contain("       over two lines */"), "a comment's own lines are left as written");

        var before = global::Moirai.Parser.StoryParser.Parse(Story, out var beforeErrors);
        var after = global::Moirai.Parser.StoryParser.Parse(once, out var afterErrors);
        Assert.That(beforeErrors, Is.Empty);
        Assert.That(afterErrors, Is.Empty);
        Assert.That(after.Printer.Print(), Is.EqualTo(before.Printer.Print()));
    }

    [Test]
    public async Task A_block_comment_highlights_one_line_at_a_time()
    {
        var (cache, uri) = await Open(Story);
        Assert.That(cache.GetDocument(uri, out var doc), Is.True);

        var comments = doc!.SemanticTokens.Where(t => t.type.ToString() == "comment").ToList();
        Assert.That(comments.Select(t => t.range.Start.Line), Is.SupersetOf(new[] { 6, 7 }));
        Assert.That(comments.All(t => t.range.Start.Line == t.range.End.Line), Is.True);
    }
}
