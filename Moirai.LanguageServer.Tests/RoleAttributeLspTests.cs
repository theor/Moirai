using Microsoft.Extensions.Logging.Testing;
using NUnit.Framework;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Moirai.LanguageServer.Tests;

/// The corpus has no role attributes (@parents, @dead, @period...), so the golden suites never see one.
/// These pin what the editor does with them: formatting leaves them meaning the same thing, and their
/// arguments are properties -- linked, so they highlight, hover and go to definition like any other.
[NonParallelizable]
public class RoleAttributeLspTests
{
    const string Story = "@parents(mother, father)\n@dead(fallen)\n@population\nentity Kin {\n" +
                         "    prop mother: Kin\n    prop father: Kin\n    prop fallen: bool\n}\n";

    static async Task<(MoiraiCache cache, DocumentUri uri)> Open(string content)
    {
        var uri = new DocumentUri("file", null, "/roles.sg", null, null);
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
    public async Task Formatting_keeps_the_roles_and_settles()
    {
        var once = await Format(Story);
        Assert.That(await Format(once), Is.EqualTo(once));

        var before = global::Moirai.Parser.StoryParser.Parse(Story, out var beforeErrors);
        var after = global::Moirai.Parser.StoryParser.Parse(once, out var afterErrors);
        Assert.That(beforeErrors, Is.Empty);
        Assert.That(afterErrors, Is.Empty);
        // The printer writes declared roles back, so this compares them too.
        Assert.That(after.Printer.Print(), Is.EqualTo(before.Printer.Print()));
        Assert.That(before.Printer.Print(), Does.Contain("@parents(mother, father)"));
    }

    [Test]
    public async Task Role_arguments_highlight_as_properties()
    {
        var (cache, uri) = await Open(Story);
        Assert.That(cache.GetDocument(uri, out var doc), Is.True);

        // `mother` in @parents(mother, father): line 0, columns 9-15.
        var token = doc!.SemanticTokens.FirstOrDefault(t => t.range.Start.Line == 0 && t.range.Start.Character == 9);
        Assert.That(token.type.ToString(), Is.EqualTo("property"),
            () => string.Join("\n", doc.SemanticTokens.Where(t => t.range.Start.Line == 0)
                .Select(t => $"{t.range.Start.Character} {t.type}")));
    }
}
