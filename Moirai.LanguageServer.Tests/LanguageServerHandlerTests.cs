using Microsoft.Extensions.Logging.Testing;
using NUnit.Framework;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

namespace Moirai.LanguageServer.Tests;

// Coverage for the LSP request handlers and the MoiraiCache/MoiraiDocument logic they sit on top
// of. These exercise the real parse -> AstVisitor -> TokenVisitor/SourceLinker pipeline without
// standing up the JSON-RPC transport: handlers are constructed directly with a FakeLogger and a
// primed MoiraiCache. Database.Instance is a mutable static, so the fixture is non-parallel.
[NonParallelizable]
public class LanguageServerHandlerTests
{
    // 0: entity Person {
    // 1:     prop age: number
    // 2:     prop partner: Person   <- 2nd occurrence of "Person" is a type reference
    // 3: }
    // 4:
    // 5: @start
    // 6: event start {
    // 7:     create Person $p: ('hello')
    // 8:     set $p.age = 3
    // 9: }
    private const string Source = @"entity Person {
    prop age: number
    prop partner: Person
}

@start
event start {
    create Person $p: ('hello')
    set $p.age = 3
}
";

    private static string N(string s) => s.Replace("\r\n", "\n");

    private static DocumentUri Uri() => new("file", null, "/test.sg", null, null);

    private static async Task<(MoiraiCache cache, DocumentUri uri, string content)> OpenAsync(string source)
    {
        var content = N(source);
        var uri = Uri();
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

    private static MoiraiDocument Process(string source)
    {
        var uri = Uri();
        var doc = new MoiraiDocument(uri,
            new TextDocumentItem { Uri = uri, LanguageId = "moirai", Text = N(source), Version = 1 });
        doc.Process(new FakeLogger<MoiraiCache>()).GetAwaiter().GetResult();
        return doc;
    }

    // 0-based Position pointing into the middle of the `occurrence`-th appearance of `needle`.
    private static Position PositionInside(string content, string needle, int occurrence = 1)
    {
        int idx = -1;
        for (int i = 0; i < occurrence; i++)
        {
            idx = content.IndexOf(needle, idx + 1, StringComparison.Ordinal);
            if (idx < 0)
                throw new ArgumentException($"'{needle}' #{occurrence} not found");
        }

        int target = idx + needle.Length / 2;
        int line = 0, col = 0;
        for (int i = 0; i < target; i++)
        {
            if (content[i] == '\n')
            {
                line++;
                col = 0;
            }
            else
            {
                col++;
            }
        }

        return new Position(line, col);
    }

    // ---- MoiraiDocument.Process ----

    [Test]
    public void Process_valid_source_has_no_errors_and_emits_tokens_and_symbols()
    {
        var doc = Process(Source);
        Assert.That(doc.Errors, Is.Empty, () => string.Join("\n", doc.Errors.Select(e => $"{e.Code}: {e.Message}")));
        Assert.That(doc.SemanticTokens, Is.Not.Empty);
        Assert.That(doc.Symbols, Is.Not.Empty);
    }

    [Test]
    public void Process_invalid_source_reports_errors()
    {
        var doc = Process("entity { borked");
        Assert.That(doc.Errors, Is.Not.Empty);
    }

    [Test]
    public void Apply_incremental_change_throws_not_implemented()
    {
        var uri = Uri();
        var doc = new MoiraiDocument(uri, new TextDocumentItem { Uri = uri, Text = "a", Version = 1 });
        var changes = new[]
        {
            new TextDocumentContentChangeEvent { Range = new Range(0, 0, 0, 1), Text = "b" },
        };
        Assert.That(() => doc.Apply(changes, 2), Throws.TypeOf<NotImplementedException>());
    }

    // ---- MoiraiCache lifecycle ----

    [Test]
    public async Task OnChange_replaces_full_content()
    {
        var (cache, uri, _) = await OpenAsync(Source);
        await cache.OnChange(new DidChangeTextDocumentParams
        {
            TextDocument = new OptionalVersionedTextDocumentIdentifier { Uri = uri, Version = 2 },
            ContentChanges = new Container<TextDocumentContentChangeEvent>(
                new TextDocumentContentChangeEvent { Text = "entity Cat {\n}\n" }),
        });
        Assert.That(cache.GetContent(uri), Is.EqualTo("entity Cat {\n}\n"));
    }

    [Test]
    public async Task OnClose_removes_document()
    {
        var (cache, uri, _) = await OpenAsync(Source);
        Assert.That(cache.GetContent(uri), Is.Not.Empty);

        cache.OnClose(new DidCloseTextDocumentParams { TextDocument = new TextDocumentIdentifier(uri) });
        Assert.That(cache.GetContent(uri), Is.Empty);
    }

    // ---- MyDocumentSymbolHandler ----

    [Test]
    public async Task DocumentSymbolHandler_returns_event_and_property_symbols()
    {
        var (cache, uri, _) = await OpenAsync(Source);
        var handler = new MyDocumentSymbolHandler(new FakeLogger<MyDocumentSymbolHandler>(), cache);

        var result = await handler.Handle(
            new DocumentSymbolParams { TextDocument = new TextDocumentIdentifier(uri) }, default);

        Assert.That(result, Is.Not.Null);
        var names = result!.Select(s => s.SymbolInformation?.Name ?? s.DocumentSymbol?.Name).ToList();
        Assert.That(names, Does.Contain("start"));
        Assert.That(names, Does.Contain("age"));
        Assert.That(names, Does.Contain("partner"));
    }

    [Test]
    public async Task DocumentSymbolHandler_returns_null_for_unknown_document()
    {
        var cache = new MoiraiCache(new FakeLogger<MoiraiCache>());
        var handler = new MyDocumentSymbolHandler(new FakeLogger<MyDocumentSymbolHandler>(), cache);

        var result = await handler.Handle(
            new DocumentSymbolParams { TextDocument = new TextDocumentIdentifier(Uri()) }, default);

        Assert.That(result, Is.Null);
    }

    // ---- MyDeclarationHandler (go to definition) ----

    [Test]
    public async Task DeclarationHandler_type_reference_links_to_declaration()
    {
        var (cache, uri, content) = await OpenAsync(Source);
        var handler = new MyDeclarationHandler(new FakeLogger<MyDeclarationHandler>(), cache);

        var pos = PositionInside(content, "Person", occurrence: 2); // prop partner: Person
        var result = await handler.Handle(
            new DefinitionParams { TextDocument = new TextDocumentIdentifier(uri), Position = pos }, default);

        Assert.That(result, Is.Not.Null);
        var link = result!.Single();
        Assert.That(link.Location, Is.Not.Null);
        Assert.That(link.Location!.Uri, Is.EqualTo((DocumentUri)uri));
        // FullDefinition spans the whole `entity Person { ... }` block, which starts on line 0.
        Assert.That(link.Location!.Range.Start.Line, Is.EqualTo(0));
    }

    [Test]
    public async Task DeclarationHandler_returns_null_on_blank_position()
    {
        var (cache, uri, _) = await OpenAsync(Source);
        var handler = new MyDeclarationHandler(new FakeLogger<MyDeclarationHandler>(), cache);

        var result = await handler.Handle(
            new DefinitionParams { TextDocument = new TextDocumentIdentifier(uri), Position = new Position(4, 0) },
            default);

        Assert.That(result, Is.Null);
    }

    // ---- MyUsageHandler (find references) ----

    [Test]
    public async Task UsageHandler_type_from_usage_returns_declaration_and_usages()
    {
        var (cache, uri, content) = await OpenAsync(Source);
        var handler = new MyUsageHandler(new FakeLogger<MyUsageHandler>(), cache);

        var pos = PositionInside(content, "Person", occurrence: 2); // prop partner: Person
        var result = await handler.Handle(
            new ReferenceParams
            {
                TextDocument = new TextDocumentIdentifier(uri),
                Position = pos,
                Context = new ReferenceContext { IncludeDeclaration = true },
            }, default);

        Assert.That(result, Is.Not.Null);
        // `Person` is declared on line 0 (`entity Person`) and used on the `prop partner` and
        // `create Person` lines.
        var lines = result!.Select(l => l.Range.Start.Line).OrderBy(x => x).ToList();
        Assert.That(lines, Is.EqualTo(new[] { 0, 2, 7 }));
        Assert.That(result.All(l => l.Uri == (DocumentUri)uri));
    }

    [Test]
    public async Task UsageHandler_type_from_declaration_returns_declaration_and_usages()
    {
        var (cache, uri, content) = await OpenAsync(Source);
        var handler = new MyUsageHandler(new FakeLogger<MyUsageHandler>(), cache);

        var pos = PositionInside(content, "Person", occurrence: 1); // the `entity Person` declaration
        var result = await handler.Handle(
            new ReferenceParams
            {
                TextDocument = new TextDocumentIdentifier(uri),
                Position = pos,
                Context = new ReferenceContext { IncludeDeclaration = true },
            }, default);

        Assert.That(result, Is.Not.Null);
        var lines = result!.Select(l => l.Range.Start.Line).OrderBy(x => x).ToList();
        Assert.That(lines, Is.EqualTo(new[] { 0, 2, 7 }));
    }

    [Test]
    public async Task UsageHandler_type_excludes_declaration_when_not_requested()
    {
        var (cache, uri, content) = await OpenAsync(Source);
        var handler = new MyUsageHandler(new FakeLogger<MyUsageHandler>(), cache);

        var pos = PositionInside(content, "Person", occurrence: 1); // on the declaration name
        var result = await handler.Handle(
            new ReferenceParams
            {
                TextDocument = new TextDocumentIdentifier(uri),
                Position = pos,
                Context = new ReferenceContext { IncludeDeclaration = false },
            }, default);

        Assert.That(result, Is.Not.Null);
        // Declaration on line 0 is dropped; only the two usages remain.
        var lines = result!.Select(l => l.Range.Start.Line).OrderBy(x => x).ToList();
        Assert.That(lines, Is.EqualTo(new[] { 2, 7 }));
    }

    [Test]
    public async Task UsageHandler_variable_respects_include_declaration()
    {
        var (cache, uri, content) = await OpenAsync(Source);
        var handler = new MyUsageHandler(new FakeLogger<MyUsageHandler>(), cache);

        // `$p` is declared on the `create` line (7) and used on the `set` line (8).
        var pos = PositionInside(content, "$p", occurrence: 2); // the usage in `set $p.age`

        var withDecl = await handler.Handle(
            new ReferenceParams
            {
                TextDocument = new TextDocumentIdentifier(uri),
                Position = pos,
                Context = new ReferenceContext { IncludeDeclaration = true },
            }, default);
        Assert.That(withDecl!.Select(l => l.Range.Start.Line).OrderBy(x => x), Is.EqualTo(new[] { 7, 8 }));

        var withoutDecl = await handler.Handle(
            new ReferenceParams
            {
                TextDocument = new TextDocumentIdentifier(uri),
                Position = pos,
                Context = new ReferenceContext { IncludeDeclaration = false },
            }, default);
        Assert.That(withoutDecl!.Select(l => l.Range.Start.Line), Is.EqualTo(new[] { 8 }));
    }

    [Test]
    public async Task UsageHandler_returns_empty_on_blank_position()
    {
        var (cache, uri, _) = await OpenAsync(Source);
        var handler = new MyUsageHandler(new FakeLogger<MyUsageHandler>(), cache);

        var result = await handler.Handle(
            new ReferenceParams
            {
                TextDocument = new TextDocumentIdentifier(uri),
                Position = new Position(4, 0),
                Context = new ReferenceContext { IncludeDeclaration = true },
            }, default);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!, Is.Empty);
    }

    // ---- MoiraiCodeLensHandler (usage-count overlay) ----

    [Test]
    public async Task CodeLensHandler_reports_usage_counts_for_declarations()
    {
        var (cache, uri, _) = await OpenAsync(Source);
        var handler = new MoiraiCodeLensHandler(cache);

        var result = await handler.Handle(
            new CodeLensParams { TextDocument = new TextDocumentIdentifier(uri) }, default);

        Assert.That(result, Is.Not.Null);
        // One lens per declaration: the `Person` type and the two props. Builtins and the `start`
        // event are not tracked as linkable declarations, so they get no lens.
        var byLine = result!.ToDictionary(l => l.Range.Start.Line, l => l.Command!.Title);
        Assert.That(byLine, Has.Count.EqualTo(3));
        Assert.That(byLine[0], Is.EqualTo("2 usages")); // entity Person -> `partner: Person`, `create Person`
        Assert.That(byLine[1], Is.EqualTo("1 usage"));  // prop age -> `set $p.age`
        Assert.That(byLine[2], Is.EqualTo("0 usages")); // prop partner -> unused
    }

    [Test]
    public async Task CodeLensHandler_lens_invokes_show_references_command()
    {
        var (cache, uri, _) = await OpenAsync(Source);
        var handler = new MoiraiCodeLensHandler(cache);

        var result = await handler.Handle(
            new CodeLensParams { TextDocument = new TextDocumentIdentifier(uri) }, default);

        var personLens = result!.Single(l => l.Range.Start.Line == 0);
        Assert.That(personLens.Command, Is.Not.Null);
        Assert.That(personLens.Command!.Name, Is.EqualTo("moirai.showReferences"));
        // Arguments are (uri, line, character) consumed by the client-side command.
        Assert.That(personLens.Command.Arguments!.Count, Is.EqualTo(3));
        Assert.That((int)personLens.Command.Arguments![1]!, Is.EqualTo(0));
    }

    // ---- MyHoverHandler ----

    [Test]
    public async Task HoverHandler_type_reference_returns_contents_and_range()
    {
        var (cache, uri, content) = await OpenAsync(Source);
        var handler = new MyHoverHandler(new FakeLogger<MyHoverHandler>(), cache);

        var pos = PositionInside(content, "Person", occurrence: 2);
        var hover = await handler.Handle(
            new HoverParams { TextDocument = new TextDocumentIdentifier(uri), Position = pos }, default);

        Assert.That(hover, Is.Not.Null);
        Assert.That(hover!.Contents, Is.Not.Null);
        Assert.That(hover.Range, Is.Not.Null);
        Assert.That(hover.Range!.Start.Line, Is.EqualTo(0));
    }

    [Test]
    public async Task HoverHandler_returns_null_on_blank_position()
    {
        var (cache, uri, _) = await OpenAsync(Source);
        var handler = new MyHoverHandler(new FakeLogger<MyHoverHandler>(), cache);

        var hover = await handler.Handle(
            new HoverParams { TextDocument = new TextDocumentIdentifier(uri), Position = new Position(4, 0) }, default);

        Assert.That(hover, Is.Null);
    }

    // ---- MoiraiDocumentFormattingHandler ----

    [Test]
    public async Task FormattingHandler_emits_edits_for_misformatted_source()
    {
        // `age:number` is missing the required space after the colon.
        var (cache, uri, _) = await OpenAsync("entity Person {\n    prop age:number\n}\n");
        var handler = new MoiraiDocumentFormattingHandler(new FakeLogger<MoiraiDocumentFormattingHandler>(), cache);

        var result = await handler.Handle(
            new DocumentFormattingParams { TextDocument = new TextDocumentIdentifier(uri) }, default);

        Assert.That(result, Is.Not.Null);
        Assert.That(result!.Count(), Is.GreaterThan(0));
    }

    [Test]
    public async Task FormattingHandler_returns_null_for_unknown_document()
    {
        var cache = new MoiraiCache(new FakeLogger<MoiraiCache>());
        var handler = new MoiraiDocumentFormattingHandler(new FakeLogger<MoiraiDocumentFormattingHandler>(), cache);

        var result = await handler.Handle(
            new DocumentFormattingParams { TextDocument = new TextDocumentIdentifier(Uri()) }, default);

        Assert.That(result, Is.Null);
    }

    // ---- Trigger read-prop CodeLens (property-gated dispatch surfaced in the editor) ----

    [Test]
    public void TriggerReadPropLenses_describe_gated_properties()
    {
        var doc = Process(@"
entity Person {
    prop alive: bool
    prop prosperity: percentage
    prop age: number
}
function adult($p: Person): bool {
    $p.age > 1
}
trigger on_death {
    when Person and alive = false and $old.alive
    record('died')
}
trigger poor_death {
    when Person and alive = false and prosperity < 10%
    record('poor')
}
trigger any_change {
    when Person
    record('changed')
}
trigger complex {
    when Person and adult($new)
    record('complex')
}
trigger spawned {
    when_created Person
    record('born')
}
");
        Assert.That(doc.Errors, Is.Empty, () => string.Join("\n", doc.Errors.Select(e => e.Message)));

        var titles = doc.TriggerReadPropLenses.Select(l => l.title).ToList();
        // on_death reads only `alive`; poor_death reads both (sorted); a bare `when Person` reacts to
        // any change; a function-call predicate can't be gated; a when_created trigger reacts to creation.
        Assert.That(titles, Does.Contain("reads: alive"));
        Assert.That(titles, Does.Contain("reads: alive, prosperity"));
        Assert.That(titles, Does.Contain("reacts to every change"));
        Assert.That(titles, Does.Contain("reacts to every change (predicate not gated)"));
        Assert.That(titles, Does.Contain("reacts to new Person"));
        Assert.That(doc.TriggerReadPropLenses.Count, Is.EqualTo(5));
    }
}
