using Moirai.Parser;
using Moirai.Parser.Ast;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Superpower.Model;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

/// Builds the semantic-token stream and the document-symbol list.
///
/// This replaces the ANTLR parse-tree visitor that used to do both. It is not a port of it: colour
/// is decided by three different kinds of knowledge, and saying so explicitly is what makes the
/// awkward cases tractable.
///
///   Lexical    - keywords, operators, literals and comments are settled by the token kind alone,
///                so they come straight off the token stream. This also removes the failure mode
///                SyntaxHighlightingDriftTests was written to catch: the old base-visitor silently
///                walked past a newly added keyword and left it uncoloured, whereas here a keyword
///                is highlighted the moment the tokenizer knows about it.
///
///   Syntactic  - whether a bare `Id` is a property, a function or an event name follows from where
///                it sits, so a walk over the AST supplies it. This is also the only source for
///                things the linker never sees: primitive annotations like `bool`, and the
///                paren-less call form (`create Person $p: '...'`).
///
///   Resolved   - whether the name in `prop kind: Kind` is an entity or an enum is neither lexical
///                nor positional. StoryParser.ILinker already worked it out while lowering the AST,
///                so SourceLinker.Occurrences() answers it without a second traversal.
///
/// Each layer refines the one before it, but not uniformly -- see the Layer precedence note below,
/// which is where the genuinely surprising case lives.
public static class MoiraiSemanticTokens
{
    public static void Build(
        StoryParser.ToolingParse parse,
        SourceLinker linker,
        DocumentUri documentUri,
        List<(Range range, SemanticTokenType type, string[] modifiers)> tokens,
        List<SymbolInformationOrDocumentSymbol> symbols)
    {
        var sink = new TokenSink();
        Lexical(parse.Tokens.FullTokens, sink);
        new Walker(documentUri, sink, symbols).Defs(parse.Defs);
        Identifiers(linker, sink);
        sink.Flush(tokens);
    }

    /// How much authority a layer has over a given range. The layers overlap on purpose, and the
    /// overlaps are not all resolved the same way, so "last one wins" is wrong in both directions:
    ///
    ///  - The linker must beat the syntactic walker, because only it knows that the type named in
    ///    `prop kind: Kind` is an enum rather than an entity.
    ///  - The lexical layer must beat the linker, because the AST anchors some variable declarations
    ///    at a keyword: `$old`/`$new` are declared at the `when` token and `$self`/`$other` at an
    ///    attribute's name, so the linker reports a variable occurrence sitting exactly on top of a
    ///    keyword or a decorator. Those tokens are lexically certain and cannot be an identifier.
    static class Layer
    {
        /// Keywords, operators, literals, comments, decorators -- decided by token kind alone.
        public const int Lexical = 3;
        /// A resolved symbol: entity vs enum, and so on.
        public const int Resolved = 2;
        /// The role the identifier's position gives it.
        public const int Syntactic = 1;
    }

    sealed class TokenSink
    {
        readonly Dictionary<(int, int, int, int), (Range range, SemanticTokenType type, string[] modifiers, int layer)>
            _best = new();

        public void Add(Range range, SemanticTokenType type, string[] modifiers, int layer)
        {
            var key = (range.Start.Line, range.Start.Character, range.End.Line, range.End.Character);
            if (_best.TryGetValue(key, out var existing))
            {
                if (existing.layer > layer)
                    return;
                // Layers refine the *type*, not the modifiers: the linker resolves what a name means
                // but has no idea whether this occurrence is the declaration, so a plain `function`
                // from it must not erase the `[definition]` the syntactic pass established.
                if (modifiers.Length == 0)
                    modifiers = existing.modifiers;
            }

            _best[key] = (range, type, modifiers, layer);
        }

        /// The protocol wants tokens in document order and without overlap.
        public void Flush(List<(Range range, SemanticTokenType type, string[] modifiers)> tokens)
        {
            tokens.Clear();
            tokens.AddRange(_best.Values
                .OrderBy(t => t.range.Start.Line)
                .ThenBy(t => t.range.Start.Character)
                .Select(t => (t.range, t.type, t.modifiers)));
        }
    }

    // ---- Lexical layer -------------------------------------------------------------------

    /// Token kinds whose colour never depends on context. Kinds absent from this table are either
    /// punctuation we do not colour (braces, parens, commas, `:`) or identifiers, which the semantic
    /// layer classifies.
    ///
    /// A lookup table rather than a switch expression on purpose: SemanticTokenType is a struct with
    /// an implicit conversion from string, so a `_ => null` default arm silently converts to a
    /// *present* token type wrapping a null string instead of behaving as "no colour".
    static readonly Dictionary<MoiraiTokenKind, SemanticTokenType> LexicalTypes = new()
    {
        [MoiraiTokenKind.Event] = SemanticTokenType.Keyword,
        [MoiraiTokenKind.Entity] = SemanticTokenType.Keyword,
        [MoiraiTokenKind.Singleton] = SemanticTokenType.Keyword,
        [MoiraiTokenKind.Trigger] = SemanticTokenType.Keyword,
        [MoiraiTokenKind.Prop] = SemanticTokenType.Keyword,
        [MoiraiTokenKind.Function] = SemanticTokenType.Keyword,
        [MoiraiTokenKind.Enum] = SemanticTokenType.Keyword,
        [MoiraiTokenKind.Table] = SemanticTokenType.Keyword,
        [MoiraiTokenKind.When] = SemanticTokenType.Keyword,
        [MoiraiTokenKind.WhenCreated] = SemanticTokenType.Keyword,
        [MoiraiTokenKind.Set] = SemanticTokenType.Keyword,
        [MoiraiTokenKind.Var] = SemanticTokenType.Keyword,
        [MoiraiTokenKind.Match] = SemanticTokenType.Keyword,
        [MoiraiTokenKind.MatchWeight] = SemanticTokenType.Keyword,
        [MoiraiTokenKind.If] = SemanticTokenType.Keyword,
        [MoiraiTokenKind.Else] = SemanticTokenType.Keyword,

        [MoiraiTokenKind.ColonEq] = SemanticTokenType.Operator,
        [MoiraiTokenKind.Eq] = SemanticTokenType.Operator,
        [MoiraiTokenKind.Neq] = SemanticTokenType.Operator,
        [MoiraiTokenKind.Ge] = SemanticTokenType.Operator,
        [MoiraiTokenKind.Le] = SemanticTokenType.Operator,
        [MoiraiTokenKind.Gt] = SemanticTokenType.Operator,
        [MoiraiTokenKind.Lt] = SemanticTokenType.Operator,
        [MoiraiTokenKind.Add] = SemanticTokenType.Operator,
        [MoiraiTokenKind.Sub] = SemanticTokenType.Operator,
        [MoiraiTokenKind.Mul] = SemanticTokenType.Operator,
        [MoiraiTokenKind.Div] = SemanticTokenType.Operator,
        [MoiraiTokenKind.Mod] = SemanticTokenType.Operator,
        [MoiraiTokenKind.Qq] = SemanticTokenType.Operator,
        [MoiraiTokenKind.Dot] = SemanticTokenType.Operator,
        [MoiraiTokenKind.And] = SemanticTokenType.Operator,
        [MoiraiTokenKind.Or] = SemanticTokenType.Operator,

        // `true`/`false`/`null` are coloured as literals, matching the previous highlighter.
        [MoiraiTokenKind.Number] = SemanticTokenType.Number,
        [MoiraiTokenKind.NumberFloat] = SemanticTokenType.Number,
        [MoiraiTokenKind.Percent] = SemanticTokenType.Number,
        [MoiraiTokenKind.True] = SemanticTokenType.Number,
        [MoiraiTokenKind.False] = SemanticTokenType.Number,
        [MoiraiTokenKind.Null] = SemanticTokenType.Number,

        [MoiraiTokenKind.Quote] = SemanticTokenType.String,
        [MoiraiTokenKind.Text] = SemanticTokenType.String,
        [MoiraiTokenKind.Comment] = SemanticTokenType.Comment,

        // A sigil settles these two without any context: `$x` is always a variable read or
        // declaration, `#Time` is always a singleton type.
        [MoiraiTokenKind.VarId] = SemanticTokenType.Variable,
        [MoiraiTokenKind.SingletonId] = SemanticTokenType.Type,
    };

    static void Lexical(TokenList<MoiraiTokenKind> full, TokenSink sink)
    {
        var all = full.ToArray();
        for (int i = 0; i < all.Length; i++)
        {
            var kind = all[i].Kind;

            // `@attr` is two tokens and both are the decorator. Consuming the name here keeps the
            // semantic layer from also classifying it as a function or property.
            if (kind == MoiraiTokenKind.At)
            {
                sink.Add(RangeOf(all[i].Span), SemanticTokenType.Decorator, NoModifiers, Layer.Lexical);
                if (i + 1 < all.Length && all[i + 1].Kind == MoiraiTokenKind.Id)
                {
                    sink.Add(RangeOf(all[i + 1].Span), SemanticTokenType.Decorator, NoModifiers, Layer.Lexical);
                    i++;
                }

                continue;
            }

            if (LexicalTypes.TryGetValue(kind, out var type))
            {
                // `$x` and `#Time` are identifiers that happen to be lexically unambiguous. They are
                // classified here for convenience, but the linker may know better, so they do not
                // claim lexical authority.
                var layer = kind is MoiraiTokenKind.VarId or MoiraiTokenKind.SingletonId
                    ? Layer.Syntactic
                    : Layer.Lexical;
                sink.Add(RangeOf(all[i].Span), type, NoModifiers, layer);
            }
        }
    }

    // ---- Semantic layer ------------------------------------------------------------------

    static void Identifiers(SourceLinker linker, TokenSink sink)
    {
        foreach (var (range, def) in linker.Occurrences())
        {
            if (SemanticTypes.TryGetValue(def.Type, out var type))
                sink.Add(range, type, NoModifiers, Layer.Resolved);
        }
    }

    /// Same lookup-table reasoning as LexicalTypes: an unmapped DefinitionType must mean "do not
    /// colour", which a switch expression's null default arm would not give us.
    static readonly Dictionary<MoiraiSymbol.DefinitionType, SemanticTokenType> SemanticTypes = new()
    {
        [MoiraiSymbol.DefinitionType.Type] = SemanticTokenType.Type,
        [MoiraiSymbol.DefinitionType.TypeProperty] = SemanticTokenType.Property,
        [MoiraiSymbol.DefinitionType.Enum] = SemanticTokenType.Enum,
        [MoiraiSymbol.DefinitionType.EnumMember] = SemanticTokenType.EnumMember,
        [MoiraiSymbol.DefinitionType.Function] = SemanticTokenType.Function,
        [MoiraiSymbol.DefinitionType.Variable] = SemanticTokenType.Variable,
    };

    // ---- Syntactic layer -----------------------------------------------------------------

    /// Walks the definitions that survived parsing and colours every identifier by the role its
    /// position gives it: a name in a `prop x: T` annotation is a type, the name leading a call is a
    /// function, and so on. This is what the linker cannot do on its own -- it only knows about
    /// symbols it resolved, so it has no entry for a primitive annotation like `bool`, and it never
    /// sees the paren-less call form (`create Person $p: '...'`) at all.
    ///
    /// Runs before the linker layer, which then refines what it resolved -- most visibly, an
    /// annotation naming an enum is coloured `enum` rather than the generic `type` this pass emits.
    sealed class Walker(
        DocumentUri documentUri,
        TokenSink sink,
        List<SymbolInformationOrDocumentSymbol> symbols)
    {
        public void Defs(DefNode[] defs)
        {
            foreach (var def in defs)
                Def(def);
        }

        void Def(DefNode def)
        {
            foreach (var attribute in def.Attributes)
                foreach (var arg in attribute.Args)
                    Expr(arg);

            if (def.Event is { } ev)
            {
                Push(ev.Name, SemanticTokenType.Class);
                Symbol(ev.Name, SymbolKind.Function);
                foreach (var p in ev.Params)
                    Param(p);
                Scope(ev.Scope);
            }
            else if (def.Trigger is { } trigger)
            {
                Push(trigger.Name, SemanticTokenType.Class, DefinitionModifier);
                Symbol(trigger.Name, SymbolKind.Event);
                Scope(trigger.Scope);
            }
            else if (def.EnumDefinition is { } enumDef)
            {
                Push(enumDef.Name, SemanticTokenType.Enum);
                Symbol(enumDef.Name, SymbolKind.Enum);
                foreach (var member in enumDef.Members)
                    Push(member, SemanticTokenType.EnumMember);
            }
            else if (def.TypeDefinition is { } typeDef)
            {
                if (typeDef.TypeName is { } name)
                    Push(name, SemanticTokenType.Type);
                foreach (var prop in typeDef.PropDefinitions)
                {
                    Push(prop.PropertyId, SemanticTokenType.Property);
                    Symbol(prop.PropertyId, SymbolKind.Property);
                    Push(prop.Type.Name, SemanticTokenType.Type);
                }

                foreach (var fn in typeDef.FunctionDefinitions)
                    FunctionDefinition(fn);
            }
            else if (def.FunctionDefinition is { } fn)
            {
                FunctionDefinition(fn);
            }
            else if (def.TableDefinition is { } table)
            {
                Push(table.Name, SemanticTokenType.Type);
                foreach (var entry in table.Entries)
                    Value(entry.Value);
            }
        }

        void FunctionDefinition(FunctionDefinitionNode fn)
        {
            Push(fn.Name, SemanticTokenType.Function, DefinitionModifier);
            foreach (var p in fn.Params)
                Param(p);
            if (fn.ReturnType is { } ret)
                Push(ret.Name, SemanticTokenType.Type);
            Scope(fn.Scope);
        }

        // The `$name` half is coloured lexically; only the annotation needs a role here.
        void Param(ParamNode p) => Push(p.Type.Name, SemanticTokenType.Type);

        void Scope(ScopeNode scope)
        {
            if (scope.When is { } when)
            {
                Push(when.TypeId, SemanticTokenType.Type);
                foreach (var e in when.Exprs)
                    Expr(e);
            }

            if (scope.WhenCreated is { } whenCreated)
            {
                Push(whenCreated.TypeId, SemanticTokenType.Type);
                foreach (var e in whenCreated.Exprs)
                    Expr(e);
            }

            foreach (var effect in scope.Effects)
                Effect(effect);
        }

        void Effect(EffectNode? effect)
        {
            if (effect == null)
                return;
            Expr(effect.Expr);
            if (effect.Var is { } v)
                Expr(v.Expr);
            if (effect.Set is { } set)
            {
                Path(set.Path);
                Expr(set.Expr);
            }

            if (effect.Init is { } init)
            {
                Push(init.PropertyId, SemanticTokenType.Property);
                Expr(init.Expr);
            }
        }

        void Expr(ExprNode? expr)
        {
            if (expr == null)
                return;

            if (expr.If is { } ifNode)
            {
                Expr(ifNode.Cond);
                Scope(ifNode.Then);
                if (ifNode.Else is { } elseScope)
                    Scope(elseScope);
                return;
            }

            if (expr.Match is { } match)
            {
                foreach (var e in match.Exprs)
                    Expr(e);
                foreach (var c in match.Cases)
                {
                    foreach (var v in c.Values)
                        Value(v);
                    Effect(c.Effect);
                    if (c.Scope is { } caseScope)
                        Scope(caseScope);
                }

                return;
            }

            if (expr.Value is { } value)
            {
                Value(value);
                return;
            }

            if (expr.Paren is { } paren)
            {
                Expr(paren);
                return;
            }

            Expr(expr.Left);
            Expr(expr.Right);
        }

        void Value(ValueNode? value)
        {
            if (value == null)
                return;

            if (value.RawCall is { } rawCall)
            {
                Push(rawCall.FunId, SemanticTokenType.Function);
                if (rawCall.DeclType is { } t)
                    Push(t.Name, SemanticTokenType.Type);
                Value(rawCall.Value);
                if (rawCall.Scope is { } s)
                    Scope(s);
            }

            if (value.Call is { } call)
                Call(call);

            // Only the interpolated holes need walking; the literal chunks are lexically strings.
            if (value.StringLit is { } str)
                foreach (var part in str.Parts)
                    if (part is StringExprPart exprPart)
                        Expr(exprPart.Expr);

            if (value.EnumValue is { } enumValue)
            {
                Push(enumValue.EnumType, SemanticTokenType.Enum);
                Push(enumValue.Member, SemanticTokenType.EnumMember);
            }

            if (value.TypeId is { } typeId)
                Push(typeId, SemanticTokenType.Type);

            if (value.Path is { } path)
                Path(path);
        }

        void Call(CallNode call)
        {
            Push(call.FunId, SemanticTokenType.Function);
            if (call.DeclType is { } t)
                Push(t.Name, SemanticTokenType.Type);
            foreach (var arg in call.Args)
                Expr(arg);
            if (call.Scope is { } s)
                Scope(s);
        }

        // `$var` and `#Singleton` roots are coloured lexically; the dotted tail is not.
        void Path(PathNode path)
        {
            if (path.PropertyId is { } rootProp)
                Push(rootProp, SemanticTokenType.Property);

            foreach (var dot in path.DotProperties)
            {
                if (dot.Property is { } prop)
                    Push(prop, SemanticTokenType.Property);
                else if (dot.Call is { } call)
                    Call(call);
            }
        }

        void Push(Ident ident, SemanticTokenType type, string[]? modifiers = null) =>
            sink.Add(RangeOf(ident.Span), type, modifiers ?? NoModifiers, Layer.Syntactic);

        void Symbol(Ident name, SymbolKind kind) =>
            symbols.Add(new SymbolInformationOrDocumentSymbol(new SymbolInformation
            {
                Location = new Location { Uri = documentUri, Range = RangeOf(name.Span) },
                Name = name.Text,
                Kind = kind,
            }));
    }

    // ---- Shared --------------------------------------------------------------------------

    static readonly string[] NoModifiers = Array.Empty<string>();
    static readonly string[] DefinitionModifier = { SemanticTokenModifier.Definition };

    static Range RangeOf(TextSpan span) => new FileRange(span).ToLspRange();

}
