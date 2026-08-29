using Microsoft.Extensions.Logging.Testing;
using NUnit.Framework;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

namespace Moirai.LanguageServer.Tests;

/// Snapshot coverage for the two LSP outputs that had none: the text the formatter produces, and
/// the semantic-token stream. Both are about to be reimplemented on top of Moirai.Parser
/// (Superpower) instead of the frozen ANTLR snapshot, and neither had an assertion strong enough to
/// notice a regression -- the formatter was checked only for "more than zero edits".
///
/// These are written against the *current* implementation and are expected to pass unchanged
/// across the migration. Where the new implementation deliberately differs, the golden gets
/// re-blessed in the same commit that causes the diff, so the change is visible in review.
///
/// Database.Instance is a mutable static shared by the parse pipeline, so this fixture is serial.
[NonParallelizable]
public class LspGoldenTests
{
    static DocumentUri UriFor(string relativePath) =>
        new("file", null, "/" + relativePath, null, null);

    static async Task<(MoiraiCache cache, DocumentUri uri, string content)> Open(string relativePath, string content)
    {
        var uri = UriFor(relativePath);
        var cache = new MoiraiCache(new FakeLogger<MoiraiCache>());
        await cache.OnOpen(new DidOpenTextDocumentParams
        {
            TextDocument = new TextDocumentItem
            {
                Uri = uri, LanguageId = "moirai", Text = content, Version = 1,
            },
        });
        return (cache, uri, content);
    }

    static async Task<string> Format(string relativePath, string content)
    {
        var (cache, uri, _) = await Open(relativePath, content);
        var handler = new MoiraiDocumentFormattingHandler(
            new FakeLogger<MoiraiDocumentFormattingHandler>(), cache);
        var edits = await handler.Handle(
            new DocumentFormattingParams { TextDocument = new TextDocumentIdentifier(uri) }, default);
        return edits == null ? content : TextEdits.Apply(content, edits.ToList());
    }

    static string GoldenName(string relativePath, string suffix) =>
        relativePath.Replace('/', '_') + suffix;

    // ---- Formatting ----

    [Test, TestCaseSource(typeof(Corpus), nameof(Corpus.Files))]
    public async Task Formatting_matches_golden(string relativePath)
    {
        var content = Corpus.Read(relativePath);
        Golden.Verify(GoldenName(relativePath, ".formatted"), await Format(relativePath, content));
    }

    /// Formatting twice must equal formatting once. Goldens pin *what* the formatter emits;
    /// idempotence catches a class of bug they cannot -- a rule that keeps nudging the same token,
    /// which shows up in an editor as "format on save never settles".
    [Test, TestCaseSource(typeof(Corpus), nameof(Corpus.Files))]
    public async Task Formatting_is_idempotent(string relativePath)
    {
        var once = await Format(relativePath, Corpus.Read(relativePath));
        var twice = await Format(relativePath, once);
        if (once != twice)
            Assert.Fail("formatting is not idempotent.\n" + Golden.DescribeFirstDivergence(once, twice));
    }

    /// The strongest formatter guard, and the one that survives a reimplementation: whatever the
    /// formatter does to the whitespace, the program must still mean the same thing. Parses both the
    /// original and the formatted text and compares the engine's own pretty-print of the resulting
    /// Database -- the canonical-serialization trick ParserDifferentialTests uses to compare two
    /// parsers without structurally diffing an object graph.
    [Test, TestCaseSource(typeof(Corpus), nameof(Corpus.Files))]
    public async Task Formatting_preserves_meaning(string relativePath)
    {
        var content = Corpus.Read(relativePath);
        var formatted = await Format(relativePath, content);

        var before = global::Moirai.Parser.StoryParser.Parse(content, out var beforeErrors);
        var after = global::Moirai.Parser.StoryParser.Parse(formatted, out var afterErrors);

        Assert.That(afterErrors.Count, Is.EqualTo(beforeErrors.Count),
            () => "formatting introduced or removed parse errors:\n" +
                  string.Join("\n", afterErrors.Select(e => $"{e.Line}:{e.Col} {e.Code} {e.Message}")));
        var beforePrint = before.Printer.Print();
        var afterPrint = after.Printer.Print();
        if (beforePrint != afterPrint)
            Assert.Fail("formatting changed the meaning of the program.\n" +
                        Golden.DescribeFirstDivergence(beforePrint, afterPrint));
    }

    // ---- Semantic tokens ----

    /// One line per token, sorted, as a canonical string. Comparing a serialized form rather than
    /// diffing the list element-wise is the trick ParserDifferentialTests uses with
    /// Printer.Print(): the failure message stays readable at corpus scale.
    static string Serialize(MoiraiDocument doc) =>
        string.Join("\n", doc.SemanticTokens
            .Select(t =>
                $"{t.range.Start.Line}:{t.range.Start.Character}-{t.range.End.Line}:{t.range.End.Character} " +
                $"{t.type}{(t.modifiers.Length == 0 ? "" : " [" + string.Join(",", t.modifiers) + "]")}")
            .OrderBy(s => s, StringComparer.Ordinal)) + "\n";

    [Test, TestCaseSource(typeof(Corpus), nameof(Corpus.Files))]
    public async Task SemanticTokens_match_golden(string relativePath)
    {
        var (cache, uri, _) = await Open(relativePath, Corpus.Read(relativePath));
        Assert.That(cache.GetDocument(uri, out var doc), Is.True);
        Golden.Verify(GoldenName(relativePath, ".tokens"), Serialize(doc!));
    }

    // ---- Diagnostics ----

    /// The corpus is the shipped sample content, so it should parse clean. This is the guard that
    /// the migrated pipeline does not start inventing syntax errors on valid input -- the most
    /// visible way an LSP parser swap can go wrong.
    [Test, TestCaseSource(typeof(Corpus), nameof(Corpus.Files))]
    public async Task Corpus_has_no_error_diagnostics(string relativePath)
    {
        var (cache, uri, _) = await Open(relativePath, Corpus.Read(relativePath));
        Assert.That(cache.GetDocument(uri, out var doc), Is.True);
        var errors = doc!.Errors
            .Where(e => e.Severity == global::Moirai.Parser.StoryParser.Severity.Error)
            .Select(e => $"{e.Line}:{e.Col} {e.Code} {e.Message}")
            .ToList();
        Assert.That(errors, Is.Empty, () => string.Join("\n", errors));
    }
}
