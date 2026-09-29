using Moirai.Parser;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Superpower.Model;
using Position = OmniSharp.Extensions.LanguageServer.Protocol.Models.Position;

/// Code completion, driven by the token stream rather than the parse tree.
///
/// That is not a stylistic choice. The definition the caret sits in is, almost by construction, the
/// one that does not parse -- you are halfway through typing it -- and the grammar has no error
/// productions, so chunked recovery drops that whole definition from the AST. Asking the tree "what
/// is at the caret" therefore returns nothing exactly when completion is wanted.
///
/// The tokenizer, by contrast, never fails: it always yields a full, positioned token list even for
/// nonsense. So the caret's context is worked out from the tokens around it, and the *contents* of
/// the suggestions come from the symbols of the definitions that did parse (the Database and
/// SourceLinker on the document).
///
/// This replaces a vendored port of antlr4-c3, which derived the same answer by simulating the
/// generated parser's ATN. There is no ATN here, and the follow sets it would need exist only as
/// control flow inside the hand-written recursive-descent parser -- hence the rule table below,
/// which states directly what the ATN walk used to infer.
public static class MoiraiCompletion
{
    /// What the caret is positioned to accept. Deliberately coarse: these are the distinctions that
    /// change which symbols are worth offering, not a mirror of the grammar.
    public enum Context
    {
        /// Nothing useful to say -- inside a string literal, or naming something new.
        None,
        /// Between definitions: the keywords that can start one.
        TopLevel,
        /// Inside `entity`/`singleton` braces: `prop` and `function`.
        TypeBody,
        /// Start of a statement in a code scope: statement keywords and function names.
        Statement,
        /// Anywhere a value is expected: variables, types, functions, enum values.
        Expression,
        /// A type is required: after `create`, `when`, or a `:` annotation.
        TypeName,
        /// After `set`: the assignment target.
        VariableOrType,
        /// After `.`: the members of whatever the path resolved to.
        Property,
        /// After `@`.
        AttributeName,
        /// Inside an attribute's parentheses, at an argument whose parameter has a closed set of answers:
        /// a choice (`PerXYear`), an entity type, or a property of the annotated type.
        AttributeArgument,
    }

    /// Which argument of which attribute the caret is on: `@frequency(1, |` is (@frequency, 1).
    /// `AttributeIndex` is the position of the attribute's `@` in the token list.
    public readonly record struct AttributeSlot(AttributeDescriptor Attribute, int Argument, int AttributeIndex)
    {
        public AttributeParam? Param => Attribute.ParamAt(Argument);
    }

    public static List<CompletionItem> Complete(MoiraiDocument document, Position caret)
    {
        var tokens = MoiraiTokenizer.Tokenize(document.Content).FullTokens.ToArray();
        var analysis = Analyze(tokens, caret);
        return new ItemBuilder(document, tokens, caret).Build(analysis);
    }

    // ---- Locating the caret ---------------------------------------------------------------

    public readonly record struct Analysis(Context Context, int? TargetIndex, int? BeforeIndex, AttributeSlot? Slot = null);

    static bool IsTrivia(MoiraiTokenKind k) => k is MoiraiTokenKind.Space or MoiraiTokenKind.Comment;

    /// Word-like tokens are the ones a caret can sit *inside* and be part-way through typing.
    /// Unknown counts: a lone `$` is not a valid VAR_ID, so `set $|` -- one of the positions
    /// completion most needs to handle -- lexes as Unknown.
    static readonly HashSet<MoiraiTokenKind> KeywordKinds =
        MoiraiTokenizer.ReservedWords.Values.ToHashSet();

    static bool IsWordLike(MoiraiTokenKind k) =>
        k is MoiraiTokenKind.Id or MoiraiTokenKind.TypeId or MoiraiTokenKind.VarId
            or MoiraiTokenKind.SingletonId or MoiraiTokenKind.PropId or MoiraiTokenKind.Unknown
        || KeywordKinds.Contains(k);

    // Superpower is 1-based on both axes; LSP is 0-based on both.
    static int Line(Token<MoiraiTokenKind> t) => t.Span.Position.Line - 1;
    static int StartCol(Token<MoiraiTokenKind> t) => t.Span.Position.Column - 1;
    static int EndCol(Token<MoiraiTokenKind> t) => StartCol(t) + t.Span.Length;

    public static Analysis Analyze(Token<MoiraiTokenKind>[] tokens, Position caret)
    {
        // The token being completed: the caret is strictly inside it, or resting on its end.
        int? target = null;
        for (int i = 0; i < tokens.Length; i++)
        {
            var t = tokens[i];
            if (IsTrivia(t.Kind) || Line(t) != caret.Line)
                continue;
            if (StartCol(t) < caret.Character && caret.Character <= EndCol(t) && IsWordLike(t.Kind))
            {
                target = i;
                break;
            }
        }

        // The last significant token that finishes before whatever we are completing starts. This
        // is what actually decides the context.
        var limit = target is { } ti
            ? new Position(Line(tokens[ti]), StartCol(tokens[ti]))
            : caret;

        int? before = null;
        for (int i = 0; i < tokens.Length; i++)
        {
            var t = tokens[i];
            if (IsTrivia(t.Kind))
                continue;
            var end = new Position(Line(t), EndCol(t));
            if (end.Line > limit.Line || (end.Line == limit.Line && end.Character > limit.Character))
                break;
            before = i;
        }

        if (before is { } b && AttributeArgumentAt(tokens, b) is { } slot)
        {
            // What an argument can be is its parameter's kind, from the same table the parser checks.
            var context = slot.Param?.Kind switch
            {
                AttributeArgKind.Choice or AttributeArgKind.EntityType or AttributeArgKind.Property => Context.AttributeArgument,
                AttributeArgKind.Predicate => Context.Expression,
                _ => Context.None,
            };
            return new Analysis(context, target, before, slot);
        }

        return new Analysis(Classify(tokens, before), target, before);
    }

    /// The attribute argument the caret is on, when the last token before it is inside the parentheses of
    /// a top-level `@name(...)`: walks back along the line to the unmatched `(`, counting the commas at
    /// that depth. An attribute is one line, so the walk never leaves it.
    static AttributeSlot? AttributeArgumentAt(Token<MoiraiTokenKind>[] tokens, int beforeIndex)
    {
        if (InString(tokens, beforeIndex))
            return null;

        var line = Line(tokens[beforeIndex]);
        int depth = 0, commas = 0;
        for (int i = beforeIndex; i >= 0 && Line(tokens[i]) == line; i--)
        {
            switch (tokens[i].Kind)
            {
                case MoiraiTokenKind.ParenClose:
                    if (i == beforeIndex)
                        return null; // `@x(...)|`: past the attribute
                    depth++;
                    break;
                case MoiraiTokenKind.Comma when depth == 0:
                    commas++;
                    break;
                case MoiraiTokenKind.ParenOpen when depth > 0:
                    depth--;
                    break;
                case MoiraiTokenKind.ParenOpen:
                {
                    var name = PreviousSignificant(tokens, i);
                    var at = name is { } n ? PreviousSignificant(tokens, n) : null;
                    if (name is not { } ni || at is not { } ai || tokens[ai].Kind != MoiraiTokenKind.At
                        || StartCol(tokens[ai]) != 0)
                        return null;
                    var attribute = StoryParser.Attributes.Find(a => a.Name == tokens[ni].ToStringValue());
                    return attribute == null ? null : new AttributeSlot(attribute, commas, ai);
                }
            }
        }

        return null;
    }

    // ---- The rule table --------------------------------------------------------------------

    /// What kind of brace we are inside. `enum`/`table` bodies are called out because they hold
    /// names and values rather than statements.
    enum Enclosing { TopLevel, TypeBody, EnumBody, TableBody, Code }

    static Context Classify(Token<MoiraiTokenKind>[] tokens, int? beforeIndex)
    {
        if (beforeIndex is not { } bi)
            return Context.TopLevel;

        var enclosing = EnclosingScope(tokens, bi);
        var before = tokens[bi];

        switch (before.Kind)
        {
            case MoiraiTokenKind.Dot:
                return Context.Property;

            case MoiraiTokenKind.At:
                return Context.AttributeName;

            case MoiraiTokenKind.Set:
                return Context.VariableOrType;

            case MoiraiTokenKind.When:
            case MoiraiTokenKind.WhenCreated:
                return Context.TypeName;

            // The user is naming something new; we have nothing to suggest.
            case MoiraiTokenKind.Entity:
            case MoiraiTokenKind.Singleton:
            case MoiraiTokenKind.Enum:
            case MoiraiTokenKind.Table:
            case MoiraiTokenKind.Event:
            case MoiraiTokenKind.Trigger:
            case MoiraiTokenKind.Function:
            case MoiraiTokenKind.Prop:
            case MoiraiTokenKind.Var:
                return Context.None;

            // Inside a string literal, and not in an interpolation hole.
            case MoiraiTokenKind.Quote:
            case MoiraiTokenKind.Text:
                return InString(tokens, bi) ? Context.None : Context.Expression;

            case MoiraiTokenKind.Colon:
                return IsTypeAnnotation(tokens, bi) ? Context.TypeName : Context.Expression;

            // A statement boundary: what may start here depends on the enclosing brace.
            case MoiraiTokenKind.LineBreak:
            case MoiraiTokenKind.ScopeOpen:
            case MoiraiTokenKind.ScopeClose:
            case MoiraiTokenKind.Arrow:
                return enclosing switch
                {
                    Enclosing.TopLevel => Context.TopLevel,
                    Enclosing.TypeBody => Context.TypeBody,
                    Enclosing.EnumBody => Context.None,
                    Enclosing.TableBody => Context.Expression,
                    _ => Context.Statement,
                };

            case MoiraiTokenKind.Id:
                // `create`, `pick`, `each` and friends are followed by `Type $var`. The parser's own
                // table says which, so completion cannot drift from the language.
                return StoryParser.GetFunctionDescriptor(before.ToStringValue(), out var descriptor)
                       && descriptor.ExpectVariable
                    ? Context.TypeName
                    : Context.Expression;

            // `create Person |` / `create Person $p |`: naming the variable.
            case MoiraiTokenKind.TypeId:
            case MoiraiTokenKind.VarId:
                return Context.None;

            default:
                // Operators, commas, brackets: a value goes here.
                return Context.Expression;
        }
    }

    /// Walks the braces up to `index`. A `{` is labelled by the definition keyword that introduced
    /// it, which is what separates "inside an entity" from "inside an event body".
    static Enclosing EnclosingScope(Token<MoiraiTokenKind>[] tokens, int index)
    {
        var stack = new Stack<Enclosing>();
        Enclosing pending = Enclosing.Code;

        for (int i = 0; i <= index && i < tokens.Length; i++)
        {
            switch (tokens[i].Kind)
            {
                case MoiraiTokenKind.Entity:
                case MoiraiTokenKind.Singleton:
                    pending = Enclosing.TypeBody;
                    break;
                case MoiraiTokenKind.Enum:
                    pending = Enclosing.EnumBody;
                    break;
                case MoiraiTokenKind.Table:
                    pending = Enclosing.TableBody;
                    break;
                case MoiraiTokenKind.ScopeOpen:
                    stack.Push(pending);
                    pending = Enclosing.Code;
                    break;
                // An interpolation hole (`'{expr}'`) opens a brace the same way a block does.
                case MoiraiTokenKind.ExprOpen:
                    stack.Push(Enclosing.Code);
                    pending = Enclosing.Code;
                    break;
                case MoiraiTokenKind.ScopeClose:
                    if (stack.Count > 0)
                        stack.Pop();
                    pending = Enclosing.Code;
                    break;
                case MoiraiTokenKind.LineBreak:
                    pending = Enclosing.Code;
                    break;
            }
        }

        return stack.Count == 0 ? Enclosing.TopLevel : stack.Peek();
    }

    /// True when the token at `index` is inside an unterminated string -- an odd number of quotes
    /// precedes it. Used to keep suggestions out of prose.
    static bool InString(Token<MoiraiTokenKind>[] tokens, int index)
    {
        int quotes = 0;
        for (int i = 0; i <= index && i < tokens.Length; i++)
            if (tokens[i].Kind == MoiraiTokenKind.Quote)
                quotes++;
        return quotes % 2 == 1;
    }

    /// Distinguishes the `:` of a type annotation from the `:` of `create T $v: value`. The three
    /// annotation shapes are `prop x:`, a parameter `($x:` / `, $x:`, and a return type `):`.
    static bool IsTypeAnnotation(Token<MoiraiTokenKind>[] tokens, int colonIndex)
    {
        var previous = PreviousSignificant(tokens, colonIndex);
        if (previous is not { } p)
            return false;

        if (tokens[p].Kind == MoiraiTokenKind.ParenClose)
            return true; // `function f(): |`

        if (tokens[p].Kind == MoiraiTokenKind.Id)
        {
            // `prop x: |`
            var beforeName = PreviousSignificant(tokens, p);
            return beforeName is { } b && tokens[b].Kind == MoiraiTokenKind.Prop;
        }

        if (tokens[p].Kind == MoiraiTokenKind.VarId)
        {
            // A parameter list, as opposed to `create T $v: ...`.
            var beforeName = PreviousSignificant(tokens, p);
            return beforeName is { } b &&
                   tokens[b].Kind is MoiraiTokenKind.ParenOpen or MoiraiTokenKind.Comma;
        }

        return false;
    }

    static int? PreviousSignificant(Token<MoiraiTokenKind>[] tokens, int index)
    {
        for (int i = index - 1; i >= 0; i--)
            if (!IsTrivia(tokens[i].Kind))
                return i;
        return null;
    }

    // ---- Turning a context into suggestions ------------------------------------------------

    sealed class ItemBuilder(MoiraiDocument document, Token<MoiraiTokenKind>[] tokens, Position caret)
    {
        readonly List<CompletionItem> _items = new();

        public List<CompletionItem> Build(Analysis analysis)
        {
            switch (analysis.Context)
            {
                case Context.None:
                    break;

                case Context.TopLevel:
                    Keywords("entity", "singleton", "enum", "table", "event", "trigger", "function");
                    break;

                case Context.TypeBody:
                    Keywords("prop", "function");
                    break;

                case Context.AttributeName:
                    Attributes();
                    break;

                case Context.AttributeArgument:
                    AttributeArgument(analysis.Slot!.Value);
                    break;

                case Context.TypeName:
                    Keywords("number", "string", "bool", "percentage");
                    Types();
                    break;

                case Context.VariableOrType:
                    Variables(analysis);
                    Types();
                    break;

                case Context.Property:
                    Properties(analysis);
                    break;

                case Context.Statement:
                    Keywords("set", "var", "if", "match", "random_weighted", "when", "when_created");
                    Functions();
                    break;

                case Context.Expression:
                    Variables(analysis);
                    Functions();
                    Types();
                    break;
            }

            return _items;
        }

        void Keywords(params string[] words)
        {
            foreach (var word in words)
                _items.Add(new CompletionItem
                {
                    Label = word,
                    InsertText = word,
                    Kind = CompletionItemKind.Keyword,
                });
        }

        void Attributes()
        {
            foreach (var attribute in StoryParser.Attributes)
                _items.Add(new CompletionItem
                {
                    Label = attribute.Name,
                    InsertText = attribute.Name,
                    Kind = CompletionItemKind.Keyword,
                    Detail = "attribute",
                    Documentation = Markdown(attribute.Doc.ToMarkdown()),
                });
        }

        void AttributeArgument(AttributeSlot slot)
        {
            var param = slot.Param!;
            switch (param.Kind)
            {
                case AttributeArgKind.Choice:
                    foreach (var choice in param.Choices!)
                        _items.Add(new CompletionItem
                        {
                            Label = choice,
                            InsertText = choice,
                            Kind = CompletionItemKind.EnumMember,
                            Detail = $"@{slot.Attribute.Name} {param.Name}",
                        });
                    break;

                case AttributeArgKind.EntityType:
                    Types();
                    break;

                case AttributeArgKind.Property:
                    foreach (var (name, type) in AnnotatedTypeProperties(slot.AttributeIndex, param.PropertyKind!.Value))
                        _items.Add(new CompletionItem
                        {
                            Label = name,
                            InsertText = name,
                            Kind = CompletionItemKind.Property,
                            Detail = type,
                        });
                    break;
            }
        }

        /// The properties of the type an attribute annotates that fit a property parameter, read from the
        /// tokens of that type's definition rather than the Database: while an attribute is half-typed, the
        /// definition it sits on does not parse, so it is missing from the Database exactly when needed.
        IEnumerable<(string Name, string Type)> AnnotatedTypeProperties(int attributeIndex, PropertyKind kind)
        {
            int i = attributeIndex;
            while (i < tokens.Length && tokens[i].Kind is not (MoiraiTokenKind.Entity or MoiraiTokenKind.Singleton))
                i++;
            if (NextSignificant(i) is not { } ni)
                yield break;
            var typeName = tokens[ni].ToStringValue();

            int depth = 0;
            for (int j = ni + 1; j < tokens.Length; j++)
            {
                var k = tokens[j].Kind;
                if (k == MoiraiTokenKind.ScopeOpen)
                    depth++;
                else if (k == MoiraiTokenKind.ScopeClose && --depth <= 0)
                    yield break;
                if (k != MoiraiTokenKind.Prop || depth != 1)
                    continue;

                // `prop name: type`; a collection (`[T]`) never fits a role.
                if (NextSignificant(j) is not { } name || NextSignificant(name) is not { } colon
                    || tokens[colon].Kind != MoiraiTokenKind.Colon || NextSignificant(colon) is not { } type
                    || tokens[type].Kind == MoiraiTokenKind.LBrack)
                    continue;
                var propType = tokens[type].ToStringValue();
                var fits = kind switch
                {
                    PropertyKind.SelfReference => propType == typeName,
                    PropertyKind.Number => propType is "number" or "float",
                    _ => propType == "bool",
                };
                if (fits)
                    yield return (tokens[name].ToStringValue(), propType);
            }
        }

        int? NextSignificant(int index)
        {
            for (int i = index + 1; i < tokens.Length; i++)
                if (!IsTrivia(tokens[i].Kind))
                    return i;
            return null;
        }

        static StringOrMarkupContent Markdown(string text) =>
            new(new MarkupContent { Kind = MarkupKind.Markdown, Value = text });

        void Types()
        {
            var db = document.Database;
            if (db == null)
                return;

            foreach (var type in db.Types.Skip(1))
                _items.Add(new CompletionItem
                {
                    Label = type.Name,
                    InsertText = type.Name,
                    Kind = CompletionItemKind.Class,
                    Detail = type.IsSingleton ? "singleton" : "entity",
                });

            foreach (var e in db.Enums.Skip(1))
                _items.Add(new CompletionItem
                {
                    Label = e.Name,
                    InsertText = e.Name,
                    Kind = CompletionItemKind.Enum,
                    Detail = "enum { " + string.Join(", ", e.Values) + " }",
                });

            foreach (var table in db.Tables.Skip(1))
                _items.Add(new CompletionItem
                {
                    Label = table.Name,
                    InsertText = table.Name,
                    Kind = CompletionItemKind.Struct,
                    Detail = "table",
                });
        }

        void Functions()
        {
            foreach (var descriptor in StoryParser.Functions)
                _items.Add(new CompletionItem
                {
                    Label = descriptor.FuncName,
                    InsertText = descriptor.FuncName,
                    Kind = CompletionItemKind.Function,
                    Detail = "builtin",
                    Documentation = string.IsNullOrEmpty(descriptor.Documentation)
                        ? null
                        : Markdown(descriptor.Documentation),
                });

            var db = document.Database;
            if (db == null)
                return;

            foreach (var fn in db.Functions.Skip(1))
                _items.Add(new CompletionItem
                {
                    Label = fn.Name,
                    InsertText = fn.Name,
                    Kind = CompletionItemKind.Function,
                    Detail = Signature(db, fn),
                });
        }

        static string Signature(Database db, FunctionDefinition fn) =>
            $"{fn.Name}({string.Join(", ", fn.Parameters.Select(p => "$" + p.ParamName))})";

        void Variables(Analysis analysis)
        {
            var seen = new HashSet<string>();

            // Variables the parser resolved, which carry a type.
            foreach (var definition in document.Linker
                         .GetDefinitions(caret, MoiraiSymbol.DefinitionType.VariableScope))
            {
                if (!seen.Add(definition.Name))
                    continue;
                var hover = new List<MarkedString>();
                definition.GetHoverText(hover);
                _items.Add(new CompletionItem
                {
                    Label = definition.Name,
                    InsertText = definition.Name,
                    Kind = CompletionItemKind.Variable,
                    Detail = hover.Count > 0 ? hover[0].Value : null,
                });
            }

            // ...and the ones it could not, because the definition being typed in did not parse and
            // so never reached the linker. Recovering these from the token stream is the difference
            // between completion working and not working in exactly the file you are editing.
            foreach (var name in DeclaredInTokens(analysis))
                if (seen.Add(name))
                    _items.Add(new CompletionItem
                    {
                        Label = name,
                        InsertText = name,
                        Kind = CompletionItemKind.Variable,
                        Detail = "in scope (unparsed)",
                    });
        }

        /// Every `$name` appearing before the caret in the current top-level definition. Crude on
        /// purpose: inside a definition that does not parse there is no scope tree to consult, and
        /// offering a variable that is out of scope costs less than offering nothing.
        IEnumerable<string> DeclaredInTokens(Analysis analysis)
        {
            int limit = analysis.TargetIndex ?? analysis.BeforeIndex ?? -1;
            if (limit < 0)
                yield break;

            for (int i = StartOfEnclosingDefinition(limit); i <= limit; i++)
                if (tokens[i].Kind == MoiraiTokenKind.VarId)
                    yield return tokens[i].ToStringValue();
        }

        int StartOfEnclosingDefinition(int index)
        {
            for (int i = index; i >= 0; i--)
            {
                var t = tokens[i];
                if (StartCol(t) != 0)
                    continue;
                if (t.Kind is MoiraiTokenKind.Event or MoiraiTokenKind.Trigger or MoiraiTokenKind.Function
                    or MoiraiTokenKind.Entity or MoiraiTokenKind.Singleton)
                    return i;
            }

            return 0;
        }

        /// Members of whatever precedes the dot. Only a `$variable` receiver is resolved -- that is
        /// what the linker can type -- so `a.b.|` offers nothing rather than guessing.
        void Properties(Analysis analysis)
        {
            var db = document.Database;
            if (db == null || analysis.BeforeIndex is not { } dotIndex)
                return;

            if (PreviousSignificant(tokens, dotIndex) is not { } receiverIndex)
                return;

            var receiver = tokens[receiverIndex];
            EntityType? type = receiver.Kind switch
            {
                MoiraiTokenKind.VarId => VariableType(db, receiver),
                MoiraiTokenKind.SingletonId => db.Types
                    .FirstOrDefault(t => t.IsSingleton && "#" + t.Name == receiver.ToStringValue()),
                _ => null,
            };

            if (type == null)
                return;

            foreach (var property in type.Properties)
            {
                if (property.Name == null)
                    continue;
                _items.Add(new CompletionItem
                {
                    Label = property.Name,
                    InsertText = property.Name,
                    Kind = CompletionItemKind.Property,
                    Detail = $"{type.Name}.{property.Name}: {db.Printer.Print(property.Type)}",
                });
            }

            foreach (var fn in type.Functions.Skip(1))
                _items.Add(new CompletionItem
                {
                    Label = fn.Name,
                    InsertText = fn.Name,
                    Kind = CompletionItemKind.Method,
                    Detail = Signature(db, fn),
                });
        }

        EntityType? VariableType(Database db, Token<MoiraiTokenKind> variable)
        {
            var name = variable.ToStringValue();
            var declaration = document.Linker
                .GetDefinitions(new Position(Line(variable), StartCol(variable)),
                    MoiraiSymbol.DefinitionType.Variable)
                .Concat(document.Linker.GetDefinitions(caret, MoiraiSymbol.DefinitionType.VariableScope))
                .OfType<MoiraiSymbol.Definition<AstVisitor.VariableDeclaration>>()
                .FirstOrDefault(d => d.Name == name);

            return declaration == null ? null : db.GetEntityType(declaration.Data.Type);
        }
    }
}
