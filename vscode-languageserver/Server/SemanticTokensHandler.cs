using System.Text;
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using Microsoft.Extensions.Logging;
using Moirai.Parser;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

public class SemanticTokensHandler : SemanticTokensHandlerBase
{
    private readonly ILogger _logger;

    private readonly MoiraiCache _moiraiCache;

    public SemanticTokensHandler(ILogger<SemanticTokensHandler> logger, MoiraiCache moiraiCache)
    {
        _logger = logger;
        _moiraiCache = moiraiCache;
    }

    public override async Task<SemanticTokens?> Handle(
        SemanticTokensParams request, CancellationToken cancellationToken
    )
    {
        var result = await base.Handle(request, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public override async Task<SemanticTokens?> Handle(
        SemanticTokensRangeParams request, CancellationToken cancellationToken
    )
    {
        var result = await base.Handle(request, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public override async Task<SemanticTokensFullOrDelta?> Handle(
        SemanticTokensDeltaParams request,
        CancellationToken cancellationToken
    )
    {
        var result = await base.Handle(request, cancellationToken).ConfigureAwait(false);
        return result;
    }

    protected override Task Tokenize(
        SemanticTokensBuilder builder, ITextDocumentIdentifierParams identifier,
        CancellationToken cancellationToken
    )
    {
        _logger.LogCritical("Tokenize " + identifier.TextDocument.Uri);
        _moiraiCache.CurrentDoc = identifier.TextDocument.Uri;
        // you would normally get this from a common source that is managed by current open editor, current active editor, etc.
        _moiraiCache.GetSemanticTokens(identifier.TextDocument.Uri, builder);
        return Task.CompletedTask;
    }

    protected override Task<SemanticTokensDocument>
        GetSemanticTokensDocument(ITextDocumentIdentifierParams @params, CancellationToken cancellationToken)
    {
        return Task.FromResult(new SemanticTokensDocument(RegistrationOptions.Legend));
    }

    protected override SemanticTokensRegistrationOptions CreateRegistrationOptions(
        SemanticTokensCapability capability, ClientCapabilities clientCapabilities
    )
    {
        return new SemanticTokensRegistrationOptions
        {
            DocumentSelector = MoiraiLanguage.Selector,
            Legend = new SemanticTokensLegend
            {
                TokenModifiers = capability.TokenModifiers,
                TokenTypes = capability.TokenTypes
            },
            Full = new SemanticTokensCapabilityRequestFull
            {
                Delta = true
            },
            Range = true
        };
    }
}

public class TokenVisitor : MoiraiParserBaseVisitor<object?>, StoryParser.IVisitor
{
    [Flags]
    public enum DefinitionType
    {
        Unknown = 0,
        Enum = 1 << 0,
        Type = 1 << 1,
        Function = 1 << 2,
        EnumMember = 1 << 3,
        TypeProperty = 1 << 4,
        Variable = 1 << 5,
        VariableScope = 1 << 6,
    }

    public abstract class Definition(DefinitionType Type, string Name, Range? FullDefinition)
    {
        public DefinitionType Type { get; init; } = Type;
        public string Name { get; init; } = Name;
        public Range? FullDefinition { get; init; } = FullDefinition;
        public string? InlineDefinition { get; set; }

        public virtual void GetHoverText(List<MarkedString> markedStrings)
        {
        }
    }

    public abstract class Definition<T>(DefinitionType Type, T t, string Name, Range? FullDefinition)
        : Definition(Type, Name, FullDefinition)
    {
        public T Data = t;
    }


    public class TypeDefinition(EntityTypeId typeId, Range? declarationRange)
        : Definition<EntityTypeId>(DefinitionType.Type, typeId, Database.Instance.GetEntityTypeName(typeId), declarationRange)
    {
        public override void GetHoverText(List<MarkedString> markedStrings)
        {
            StringBuilder sb = new();
            Database.Instance.Printer.PrintDefaultProperties(sb);
            markedStrings.Add(new MarkedString("moirai", sb.ToString()));
        }
    }

    public class PropertyDefinition(PropertyId propId, Range? declarationRange)
        : Definition<PropertyId>(DefinitionType.TypeProperty, propId, propId.Id.ToString(), declarationRange)
    {
        // public override void GetHoverText(List<MarkedString> markedStrings)
        // {
        //     markedStrings.Add(new MarkedString("moirai", $"{propId.TypeId}.{Database.Instance.GetPropertyName(propId)}: {(Database.Instance.GetPropertyType(propId, out var valueType) ? valueType.ToString() : "")}"));
        // }
    }

    public class EnumMemberDefinition(DefinitionType Type, PropertyValue t, string Name, Range? FullDefinition)
        : Definition<PropertyValue>(Type, t, Name, FullDefinition)
    {
    }

    public class EnumDefinition(EnumDefinitionId propId, Range? declarationRange)
        : Definition<EnumDefinitionId>(DefinitionType.Enum, propId, propId.Id.ToString(), declarationRange)
    {
        public List<EnumMemberDefinition> Members = Enumerable.Repeat((EnumMemberDefinition)null, 1).Concat(Database
                .Instance.Enums[propId.Id].Values.Select((v, i) =>
                    new EnumMemberDefinition(DefinitionType.EnumMember,
                        new PropertyValue(Database.Instance.Enums[propId.Id].ValueType, i), v, declarationRange)))
            .ToList();

        public Definition MemberDefinition(PropertyValue enumValue) => Members[enumValue.IntValue];
    }

    public class VariableDefinition(
        AstVisitor.VariableDeclaration decl,
        FileRange declarationRange)
        : Definition<AstVisitor.VariableDeclaration>(DefinitionType.Variable, decl, decl.Name,
            declarationRange.ToLspRange())
    {
        public override void GetHoverText(List<MarkedString> markedStrings)
        {
            markedStrings.Add(new MarkedString(Database.Instance.Printer.Print(Data.Type)));
        }
    }
    public class VariableScopeDefinition(
        AstVisitor.VariableDeclaration decl,
        FileRange declarationRange)
        : Definition<AstVisitor.VariableDeclaration>(DefinitionType.VariableScope, decl, decl.Name,
            declarationRange.ToLspRange())
    {
        public override void GetHoverText(List<MarkedString> markedStrings)
        {
            markedStrings.Add(new MarkedString(Database.Instance.Printer.Print(Data.Type)));
        }
    }

    public class FunctionDefinition : Definition<IFunctionDescriptor>
    {
        public FunctionDefinition(IFunctionDescriptor functionDescriptor, Range? fullDefinition = null)
            : base(DefinitionType.Function,
                functionDescriptor,
                functionDescriptor.FuncName,
                fullDefinition)
        {
        }

        public override void GetHoverText(List<MarkedString> markedStrings)
        {
            // TODO params
            markedStrings.Add(new MarkedString($"{Data.FuncName}()"));
            if (Data.Documentation != null)
                markedStrings.Add(new MarkedString(Data.Documentation));
        }
    }

    class ScopedDeclarations(VariableDeclarationScope rootScope)
    {
        public bool FindDeclaration(IToken token, out VariableDefinition decl)
        {
            decl = default;
            string text = token.Text;
            Range tokenRange = GetRange(token);
            VariableDeclarationScope? smallerContaining =
                FindSmallerContainingScope(rootScope, tokenRange.Start.ToParserPosition());
            if (smallerContaining == null)
                return false;

            while (smallerContaining != null)
            {
                var idx = GetVariableIndex(smallerContaining, text, tokenRange.Start.ToParserPosition());
                if (idx != -1)
                {
                    if (smallerContaining.GetDeclarationAndRange(idx, out var varDecl, out var fileRange))
                    {
                        decl = new VariableDefinition(varDecl, fileRange);
                        return true;
                    }
                }

                smallerContaining = smallerContaining.Parent;
            }


            return false;
        }

        private static int GetVariableIndex(VariableDeclarationScope smallerContaining, string text,
            FilePosition pos)
        {
            return smallerContaining.Variables.FindLastIndex(v => v.Name == text && v.DeclarationRange.Start < pos);
        }

        private VariableDeclarationScope? FindSmallerContainingScope(
            VariableDeclarationScope cur, FilePosition pos)
        {
            if (cur.Parent != null && !cur.Range.Contains(pos))
                return null;
            foreach (var child in cur.Children)
            {
                var res = FindSmallerContainingScope(child, pos);
                if (res != null)
                    return res;
            }

            return cur;
        }
    }

    private readonly ILogger _logger;
    private readonly DocumentUri _documentUri;
    public readonly List<(Range range, SemanticTokenType type, string[] modifiers)> SemanticTokens = new();
    public readonly List<SymbolInformationOrDocumentSymbol> Symbols = new();
    public List<StoryParser.Error> Errors { get; } = new();
    public MoiraiParser Parser { get; set; }
    public (int offsetLine, int offsetColumn) Offset { get; set; }


    public TokenVisitor(ILogger logger, DocumentUri documentUri,
        List<(Range range, SemanticTokenType type, string[] modifiers)> semanticTokens,
        List<SymbolInformationOrDocumentSymbol> symbols)
    {
        _logger = logger;
        _documentUri = documentUri;
        SemanticTokens = semanticTokens;
        Symbols = symbols;
    }

    private void PushSymbol(IToken symbol, SymbolKind symbolKind)
    {
        var range = GetRange(symbol);
        Symbols.Add(new SymbolInformationOrDocumentSymbol(new SymbolInformation
        {
            Location = new Location { Uri = _documentUri, Range = range },

            Name = symbol.Text,
            Kind = symbolKind,
        }));
    }

    private object? VisitTerminals(ParserRuleContext context)
    {
        foreach (var child in context.children)
        {
            if (child is ITerminalNode terminalNode)
                terminalNode.Accept(this);
        }

        return null;
    }

    private void PushSemanticToken(IToken symbol, SemanticTokenType tokenType, params string[] keyword)
    {
        SemanticTokens.Add((
            GetRange(symbol),
            tokenType,
            keyword));
    }

    public static Range GetRange(ParserRuleContext symbol) => new Range(symbol.Start.Line - 1, symbol.Start.Column,
        symbol.Stop.Line - 1, symbol.Stop.Column);

    public static Range GetRange(IToken symbol)
    {
        return new Range(symbol.Line - 1, symbol.Column, symbol.Line - 1, symbol.Column + symbol.Text.Length);
    }

    private void PushSemanticToken(ParserRuleContext symbol, SemanticTokenType tokenType, params string[] keyword)
    {
        SemanticTokens.Add((
            new Range(
                symbol.Start.Line - 1,
                symbol.Start.Column,
                symbol.Stop.Line - 1,
                symbol.Stop.Column),
            tokenType,
            keyword));
    }

    public override object? VisitEvent(MoiraiParser.EventContext context)
    {
        var id = context.ID();
        PushSemanticToken(context.EVENT().Symbol, SemanticTokenType.Keyword);
        PushSemanticToken(id.Symbol, SemanticTokenType.Class);
        PushSymbol(id.Symbol, SymbolKind.Function);

        context.scope().Accept(this);
        return VisitTerminals(context);
    }

    public override object? VisitFunction_definition(MoiraiParser.Function_definitionContext context)
    {
        PushSemanticToken(context.FUNCTION().Symbol, SemanticTokenType.Keyword);
        PushSemanticToken(context.fun_id().ID().Symbol, SemanticTokenType.Function, SemanticTokenModifier.Definition);
        if(context.type() != null)
            PushSemanticToken(context.type().ID().Symbol, SemanticTokenType.Type);

        context.type()?.Accept(this);
        context.scope().Accept(this);
        return VisitTerminals(context);
    }

    public override object? VisitTrigger(MoiraiParser.TriggerContext context)
    {
        var id = context.ID();
        PushSemanticToken(context.TRIGGER().Symbol, SemanticTokenType.Keyword);
        PushSemanticToken(id.Symbol, SemanticTokenType.Class, SemanticTokenModifier.Definition);
        PushSymbol(id.Symbol, SymbolKind.Event);

        if (context.scope() != null)
            context.scope().Accept(this);
        return VisitTerminals(context);
    }

    public override object? VisitScope(MoiraiParser.ScopeContext context)
    {
        foreach (var child in context.children)
        {
            if (child is MoiraiParser.WhenContext w)
                w.Accept(this);
            else if (child is MoiraiParser.When_createdContext wc)
                wc.Accept(this);
            else if (child is MoiraiParser.EffectContext e)
                e.Accept(this);
            else if (child is ITerminalNode terminalNode)
                terminalNode.Accept(this);
        }

        return null;
    }

    public override object? VisitR(MoiraiParser.RContext context)
    {
        foreach (var c in context.def())
        {
            foreach (var attribute in c.attribute())
                attribute.Accept(this);
            if (c.enum_definition() is { } e)
            {
                e.Accept(this);
            }
            else if (c.@event() is { } ev)
            {
                ev.Accept(this);
            }
            else if (c.trigger() is {} tr)
            {
                tr.Accept(this);
            }
            else if (c.type_definition() is {} typeDefinitionContext)
            {
                PushSemanticToken(typeDefinitionContext.ENTITY().Symbol, SemanticTokenType.Keyword);
                PushSemanticToken(typeDefinitionContext.TYPE_ID().Symbol, SemanticTokenType.Type);
                foreach (var propDefinitionContext in typeDefinitionContext.prop_definition())
                {
                    propDefinitionContext.Accept(this);
                }

                foreach (var functionDefinitionContext in typeDefinitionContext.function_definition())
                {
                    functionDefinitionContext.Accept(this);
                }
            }
            else
            {
                continue;
            }
        }

        return null;
    }

    public override object? VisitProp_definition(MoiraiParser.Prop_definitionContext context)
    {
        PushSemanticToken(context.PROP().Symbol, SemanticTokenType.Keyword);
        PushSemanticToken(context.property_id().ID().Symbol, SemanticTokenType.Property);
        PushSymbol(context.property_id().ID().Symbol, SymbolKind.Property);

        if (context.type() != null)
            PushSemanticToken(StoryParser.GetTypeTerminal(context.type()).Symbol, SemanticTokenType.Type);

        return base.VisitProp_definition(context);
    }

    public override object? VisitType_definition(MoiraiParser.Type_definitionContext context)
    {
        throw new NotImplementedException();
    }

    public override object? VisitAttribute(MoiraiParser.AttributeContext context)
    {
        PushSemanticToken(context.AT().Symbol, SemanticTokenType.Decorator);
        PushSemanticToken(context.ID().Symbol, SemanticTokenType.Decorator);
        foreach (var expr in context.expr())
            expr.Accept(this);

        return null;
    }

    public override object? VisitEnum_definition(MoiraiParser.Enum_definitionContext context)
    {
        PushSymbol(context.TYPE_ID(0).Symbol, SymbolKind.Enum);

        PushSemanticToken(context.ENUM().Symbol, SemanticTokenType.Keyword);

        PushSemanticToken(context.TYPE_ID(0).Symbol, SemanticTokenType.Enum);

        foreach (var member in context.TYPE_ID().Skip(1))
        {
            PushSemanticToken(member.Symbol, SemanticTokenType.EnumMember);
        }

        return base.VisitEnum_definition(context);
    }

    public override object? VisitEnum_value(MoiraiParser.Enum_valueContext context)
    {
        PushSemanticToken(context.TYPE_ID(0).Symbol, SemanticTokenType.Enum);
        PushSemanticToken(context.TYPE_ID(1).Symbol, SemanticTokenType.EnumMember);
        return base.VisitEnum_value(context);
    }

    public override object? VisitMatch(MoiraiParser.MatchContext context)
    {
        PushSemanticToken(context.MATCH()?.Symbol ?? context.MATCH_WEIGHT()?.Symbol, SemanticTokenType.Keyword);
        return base.VisitMatch(context);
    }

    public override object? VisitIf(MoiraiParser.IfContext context)
    {
        PushSemanticToken(context.IF().Symbol, SemanticTokenType.Keyword);
        context.cond.Accept(this);
        context.then.Accept(this);
        if (context.ELSE() != null)
        {
            PushSemanticToken(context.ELSE().Symbol, SemanticTokenType.Keyword);
            context.@else.Accept(this);
        }

        return null;
    }

    public override object? VisitVar(MoiraiParser.VarContext context)
    {
        PushSemanticToken(context.VAR().Symbol, SemanticTokenType.Keyword);
        PushSemanticToken(context.VAR_ID().Symbol, SemanticTokenType.Variable);

        return context.expr().Accept(this);
    }

    public override object? VisitRaw_call(MoiraiParser.Raw_callContext context)
    {
        PushSemanticToken(context.fun_id().ID().Symbol, SemanticTokenType.Function);
        if (context.type() != null)
            PushSemanticToken(StoryParser.GetTypeTerminal(context.type()).Symbol, SemanticTokenType.Type);

        if (context.VAR_ID() is { } varId) 
            PushSemanticToken(varId.Symbol, SemanticTokenType.Variable);

        return base.VisitRaw_call(context);
    }

    public override object? VisitCall(MoiraiParser.CallContext context)
    {
        PushSemanticToken(context.fun_id().ID().Symbol, SemanticTokenType.Function);

        if (context.type() != null)
        {
            PushSemanticToken(StoryParser.GetTypeTerminal(context.type()).Symbol, SemanticTokenType.Type);
        }

        if (context.VAR_ID() is not null)
        {
            PushSemanticToken(context.VAR_ID().Symbol, SemanticTokenType.Variable);
        }

        return base.VisitCall(context);
    }

    public override object? VisitWhen(MoiraiParser.WhenContext context)
    {
        PushSemanticToken(context.WHEN().Symbol, SemanticTokenType.Keyword);
        PushSemanticToken(context.type_id().TYPE_ID().Symbol, SemanticTokenType.Type);

        return base.VisitWhen(context);
    }

    public override object? VisitWhen_created(MoiraiParser.When_createdContext context)
    {
        PushSemanticToken(context.WHEN_CREATED().Symbol, SemanticTokenType.Keyword);
        PushSemanticToken(context.type_id().TYPE_ID().Symbol, SemanticTokenType.Type);

        return base.VisitWhen_created(context);
    }


    public override object? VisitTerminal(ITerminalNode node)
    {
        IList<IToken> hidden;
        // {
        //     _logger.LogCritical($"T: '{(node.Symbol.Type == moirai_lexer.LINE_BREAK ? "LINE_BREAK" : node.GetText())}'");
        //     hidden = ((CommonTokenStream)Parser.TokenStream).GetHiddenTokensToLeft(node.Symbol.TokenIndex, moirai_lexer.COMMENTS) ?? ReadOnlyCollection<IToken>.Empty;
        //     _logger.LogCritical("  L: " + string.Join("|",hidden.Select(t => $"'{t.Text}'")));
        //     hidden = ((CommonTokenStream)Parser.TokenStream).GetHiddenTokensToRight(node.Symbol.TokenIndex,
        //         moirai_lexer.COMMENTS) ?? ReadOnlyCollection<IToken>.Empty;
        //     _logger.LogCritical("  R: " + string.Join("|",hidden.Select(t => $"'{t.Text}'")));
        // }


        hidden = ((CommonTokenStream)Parser.TokenStream).GetHiddenTokensToLeft(node.Symbol.TokenIndex,
            moirai_lexer.COMMENTS);
        if (hidden != null)
            foreach (var t in hidden)
                PushSemanticToken(t, SemanticTokenType.Comment);

        if (node.Parent is MoiraiParser.SetContext && node.Symbol.Text == "set")
        {
            PushSemanticToken(node.Symbol, SemanticTokenType.Keyword);
        }

        if (node.Parent is MoiraiParser.WhenContext && node.Symbol.Text == "when")
        {
            PushSemanticToken(node.Symbol, SemanticTokenType.Keyword);
        }

        hidden = ((CommonTokenStream)Parser.TokenStream).GetHiddenTokensToRight(node.Symbol.TokenIndex,
            moirai_lexer.COMMENTS);
        if (hidden != null)
            foreach (var t in hidden)
            {
                PushSemanticToken(t, SemanticTokenType.Comment);
            }

        return base.VisitTerminal(node);
    }

    public override object? VisitPath(MoiraiParser.PathContext context)
    {
        if (context.var_id_read()?.VAR_ID() != null)
        {
            PushSemanticToken(context.var_id_read().VAR_ID().Symbol, SemanticTokenType.Variable);
        }

        if (context.var_id_read()?.SINGLETON_ID() != null)
        {
            PushSemanticToken(context.var_id_read().SINGLETON_ID().Symbol, SemanticTokenType.Type);
        }

        if (context.property_id() != null)
        {
            PushSemanticToken(context.property_id().ID().Symbol, SemanticTokenType.Property);
        }
        
        if(context.dot_property() != null)
            foreach (MoiraiParser.Dot_propertyContext dotPropertyContext in context.dot_property())
            {
                PushSemanticToken(dotPropertyContext.DOT().Symbol, SemanticTokenType.Operator);
                if(dotPropertyContext.property_id() != null)
                    PushSemanticToken(dotPropertyContext.property_id().ID().Symbol, SemanticTokenType.Property);
                else if (dotPropertyContext.call() != null)
                    PushSemanticToken(dotPropertyContext.call().fun_id().ID().Symbol, SemanticTokenType.Function);
                    
            }

        return base.VisitPath(context);
    }

    public override object? VisitExpr(MoiraiParser.ExprContext context)
    {
        if (context.@if() != null)
            return context.@if().Accept(this);
        if (context.match() != null)
            return context.match().Accept(this);
        if (context.value() != null)
            return context.value().Accept(this);
        if (context.paren_expr != null)
            return context.paren_expr.Accept(this);

        context.left?.Accept(this);
        if (context.op != null)
        {
            PushSemanticToken(context.op, SemanticTokenType.Operator);
            context.right.Accept(this);
        }

        return null;
    }

    public override object? VisitValue(MoiraiParser.ValueContext context)
    {
        if (context.type_id()?.TYPE_ID() != null)
        {
            PushSemanticToken(context.Start, SemanticTokenType.Type);
        }
        else if (context.@string() is MoiraiParser.StringContext s)
        {
            PushSemanticToken(s.QUOTE(0).Symbol, SemanticTokenType.String);
            foreach (var content in s.stringContent())
            {
                if (content.TEXT() != null)
                    PushSemanticToken(content.TEXT().Symbol, SemanticTokenType.String);
                else if (content.expr() is MoiraiParser.ExprContext e)
                    e.Accept(this);
            }

            PushSemanticToken(s.QUOTE(1).Symbol, SemanticTokenType.String);
        }
        else if (context.@bool() != null || context.number() != null || context.NULL() != null)
            PushSemanticToken(context.Start, SemanticTokenType.Number);
        else
            return base.VisitValue(context);

        return null;
    }
}
