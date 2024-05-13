using System.Collections.ObjectModel;
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using IntervalTree;
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

class TokenVisitor : MoiraiParserBaseVisitor<object?>, StoryParser.IVisitor
{
    public record Definition(string Name, Range Symbol, Range FullDefinition)
    {
        public Definition(IToken token, ParserRuleContext fullDefinition) : this(token.Text, GetRange(token), GetRange(fullDefinition))
        {
        }
    }
    private readonly ILogger _logger;
    private readonly DocumentUri _documentUri;
    public readonly List<(Range range, SemanticTokenType type, string[] modifiers)> SemanticTokens = new();
    public readonly Dictionary<string, Definition> Definitions = new();
    public readonly IntervalTree<Position, Definition> Locations = new();
    public List<SymbolInformationOrDocumentSymbol> Symbols = new();
    private string? _implicitTypeName;
    private Dictionary<string, string> _variablesToTypenames = new();
    public List<StoryParser.Error> Errors { get; } = new();
    public MoiraiParser Parser { get; set; }
    public (int offsetLine, int offsetColumn) Offset { get; set; }
    
    private ImplicitTypeScope ImplicitType(string typeName) => new ImplicitTypeScope(this, typeName);
    struct ImplicitTypeScope : IDisposable
    {
        private readonly TokenVisitor _tokenVisitor;
        private readonly string? _prevTypeName;

        public ImplicitTypeScope(TokenVisitor tokenVisitor, string typeName)
        {
            _tokenVisitor = tokenVisitor;
            _prevTypeName = tokenVisitor._implicitTypeName;
            tokenVisitor._implicitTypeName = typeName;
        }

        public void Dispose()
        {
            _tokenVisitor._implicitTypeName = _prevTypeName;
        }
    }

    public TokenVisitor(ILogger logger, DocumentUri documentUri)
    {
        _logger = logger;
        _documentUri = documentUri;
    }

    private void PushSymbol(IToken symbol, SymbolKind symbolKind)
    {
        var range = GetRange(symbol);
        Symbols.Add(new SymbolInformationOrDocumentSymbol(new SymbolInformation
        {
            Location = new Location{Uri = _documentUri, Range =  range},
            
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
        // base.VisitEvent(context);
        context.filter()?.Accept(this);
        PushSemanticToken(context.EVENT().Symbol, SemanticTokenType.Keyword);
        PushSemanticToken(id.Symbol, SemanticTokenType.Class);
        PushSymbol(id.Symbol, SymbolKind.Function);
        foreach (var cat in context.categories().ID())
        {
            PushSemanticToken(cat.Symbol, SemanticTokenType.Decorator);
            
        }

        context.scope().Accept(this);
        return VisitTerminals(context);
    }

    public override object? VisitTrigger(MoiraiParser.TriggerContext context)
    {
        var id = context.ID();
        PushSemanticToken(context.TRIGGER().Symbol, SemanticTokenType.Keyword);
        PushSemanticToken(id.Symbol, SemanticTokenType.Class, SemanticTokenModifier.Definition);
        PushSymbol(id.Symbol, SymbolKind.Event);
        foreach (var cat in context.categories().ID())
        {
            PushSemanticToken(cat.Symbol, SemanticTokenType.Decorator, SemanticTokenModifier.Modification);
            
        }

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
            // else if (child is MoiraiParser.CommentContext c)
                // c.Accept(this);
        }
        return null;
    }

    public override object? VisitR(MoiraiParser.RContext context)
    {
        foreach (var enumDefinitionContext in context.enum_definition())
        {
            Definitions.Add( enumDefinitionContext.TYPE_ID(0).GetText(),  new Definition(enumDefinitionContext.TYPE_ID(0).Symbol, enumDefinitionContext));
            foreach (var member in enumDefinitionContext.TYPE_ID().Skip(1))
            {
                Definitions.Add($"{enumDefinitionContext.TYPE_ID(0).GetText()}.{member.GetText()}", new Definition(member.Symbol, enumDefinitionContext));
            } 
        }
        foreach (var typeDefinitionContext in context.type_definition())
        {
            PushSymbol(typeDefinitionContext.TYPE_ID().Symbol, SymbolKind.Class);
            var typeName = typeDefinitionContext.TYPE_ID().GetText();
            Definitions.Add( typeName, new Definition(typeDefinitionContext.TYPE_ID().Symbol, typeDefinitionContext));
            foreach (var propDefinitionContext in typeDefinitionContext.prop_definition())
            {
                Definitions.Add(typeName + "__" + propDefinitionContext.ID(0).GetText(), new Definition(propDefinitionContext.ID(0).Symbol, propDefinitionContext));
            }
        }
        // TODO visit props

        foreach (var c in context.children)
        {
            switch (c)
            {
                case TerminalNodeImpl t: break;
                case MoiraiParser.Enum_definitionContext e:
                {
                    e.Accept(this);
                    break;
                }
                case MoiraiParser.EventContext ev:
                {
                    _variablesToTypenames.Clear();
                    ev.Accept(this);
                    break;
                }
                case MoiraiParser.TriggerContext tr:
                {
                    _variablesToTypenames.Clear();
                    tr.Accept(this);
                    break;
                }
                case MoiraiParser.Type_definitionContext typeDefinitionContext:
                {
                    foreach (var attributeContext in typeDefinitionContext.attribute())
                    {
                        attributeContext.Accept(this);
                    }
                    PushSemanticToken(typeDefinitionContext.ENTITY().Symbol, SemanticTokenType.Keyword);
                    PushSemanticToken(typeDefinitionContext.TYPE_ID().Symbol, SemanticTokenType.Type);
                    foreach (var propDefinitionContext in typeDefinitionContext.prop_definition())
                    {
                        propDefinitionContext.Accept(this);
                    }
                    break;
                }
                default: throw new InvalidOperationException($"Not handled: {c} {c.GetType()}");
            }
        }

        return null;// base.VisitR(context);
    }

    public override object? VisitProp_definition(MoiraiParser.Prop_definitionContext context)
    {
        PushSemanticToken(context.PROP().Symbol, SemanticTokenType.Keyword);
        PushSemanticToken(context.ID(0).Symbol, SemanticTokenType.Property);
        PushSymbol(context.ID(0).Symbol, SymbolKind.Property);
        
        if (context.TYPE_ID() != null)
        {
            PushSemanticToken(context.TYPE_ID().Symbol, SemanticTokenType.Type);
            var text = context.TYPE_ID().GetText();
            LinkLocation(context.TYPE_ID());
        }
        else
            PushSemanticToken(context.ID(1).Symbol, SemanticTokenType.Type);
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
        PushSemanticToken(context.TYPE_ID().Symbol, SemanticTokenType.Type);
        using(ImplicitType(context.TYPE_ID().GetText()))
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

    void LinkLocationFromDefinition(ITerminalNode s, Definition? def)
    {
        if (def != null)
        {
            var range = GetRange(s.Symbol);
            Locations.Add(range.Start,range.End, def);
        }
    }

    void LinkLocation(ITerminalNode s) => LinkLocation(s, out _);
    void LinkLocation(ITerminalNode s, out Definition? def)
    {
        if (Definitions.TryGetValue(s.GetText(), out def))
        {
            var range = GetRange(s.Symbol);
            Locations.Add(range.Start,range.End, def);
        }
    }
    public override object? VisitEnum_value(MoiraiParser.Enum_valueContext context)
    {
        PushSemanticToken(context.TYPE_ID(0).Symbol, SemanticTokenType.Enum);
        PushSemanticToken(context.TYPE_ID(1).Symbol, SemanticTokenType.EnumMember);
        LinkLocation(context.TYPE_ID(0), out var enumDef);
        LinkLocationFromDefinition(context.TYPE_ID(1), enumDef);
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
        // TODO var untyped for now
        // _variablesToTypenames[context.VAR_ID().GetText()] = context.TYPE_ID().GetText();
       
        return context.expr().Accept(this);
    }

    public override object? VisitRaw_call(MoiraiParser.Raw_callContext context)
    {
        PushSemanticToken(context.ID(0).Symbol, SemanticTokenType.Function);
        if (context.TYPE_ID() != null)
        {
            PushSemanticToken(context.TYPE_ID().Symbol, SemanticTokenType.Type);
            
            LinkLocation(context.TYPE_ID());
            
            _variablesToTypenames[context.VAR_ID().GetText()] = context.TYPE_ID().GetText();
        }
        if (context.ID(1) != null)
            PushSemanticToken(context.ID(1).Symbol, SemanticTokenType.Type);
        if (context.VAR_ID() is { } varId)
        {
            PushSemanticToken(varId.Symbol, SemanticTokenType.Variable);
            LinkLocationFromDefinition(varId, new Definition(varId.Symbol, context));
        }
        return base.VisitRaw_call(context);
    }

    public override object? VisitCall(MoiraiParser.CallContext context)
    {
        PushSemanticToken(context.ID(0).Symbol, SemanticTokenType.Function);
        if (context.TYPE_ID() != null)
        {
            PushSemanticToken(context.TYPE_ID().Symbol, SemanticTokenType.Type);
            LinkLocation(context.TYPE_ID());

            _variablesToTypenames[context.VAR_ID().GetText()] = context.TYPE_ID().GetText();

        }
        if (context.ID(1) != null)
            PushSemanticToken(context.ID(1).Symbol, SemanticTokenType.Type);
        if (context.VAR_ID() != null)
            PushSemanticToken(context.VAR_ID().Symbol, SemanticTokenType.Variable);
        return base.VisitCall(context);
    }
    
    public override object? VisitWhen(MoiraiParser.WhenContext context)
    {
        PushSemanticToken(context.WHEN().Symbol, SemanticTokenType.Keyword);
        PushSemanticToken(context.TYPE_ID().Symbol, SemanticTokenType.Type);
        _variablesToTypenames["$new"] = context.TYPE_ID().GetText();
        _variablesToTypenames["$old"] = context.TYPE_ID().GetText();
        LinkLocation(context.TYPE_ID());

        using var x = ImplicitType(context.TYPE_ID().GetText());
        return base.VisitWhen(context);
    }
    
    public override object? VisitWhen_created(MoiraiParser.When_createdContext context)
    {
        PushSemanticToken(context.WHEN_CREATED().Symbol, SemanticTokenType.Keyword);
        PushSemanticToken(context.TYPE_ID().Symbol, SemanticTokenType.Type);
        _variablesToTypenames["$new"] = context.TYPE_ID().GetText();
        LinkLocation(context.TYPE_ID());

        using var x = ImplicitType(context.TYPE_ID().GetText());
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
        
        
         hidden = ((CommonTokenStream)Parser.TokenStream).GetHiddenTokensToLeft(node.Symbol.TokenIndex, moirai_lexer.COMMENTS);
        if(hidden != null)
            foreach (var t in hidden)
            {
                // if(t.Type == moirai_lexer.COMMENT)
                PushSemanticToken(t, SemanticTokenType.Comment);
            }

        if (node.Parent is MoiraiParser.SetContext && node.Symbol.Text == "set")
        {
            PushSemanticToken(node.Symbol, SemanticTokenType.Keyword);
        }
        if (node.Parent is MoiraiParser.WhenContext && node.Symbol.Text == "when")
        {
            PushSemanticToken(node.Symbol, SemanticTokenType.Keyword);
        }
        
        hidden = ((CommonTokenStream)Parser.TokenStream).GetHiddenTokensToRight(node.Symbol.TokenIndex, moirai_lexer.COMMENTS);
        if(hidden != null)
            foreach (var t in hidden)
            {
                // if(t.Type == moirai_lexer.COMMENT)
                    PushSemanticToken(t, SemanticTokenType.Comment);
            }
        return base.VisitTerminal(node);
    }
    // public override object? VisitComment(MoiraiParser.CommentContext context)
    // {
    //     PushSemanticToken(context, SemanticTokenType.Comment);
    //     
    //     return null;
    // }
    public override object? VisitFilter(MoiraiParser.FilterContext context)
    {
        PushSemanticToken(context, SemanticTokenType.Decorator);
        return null;// base.VisitFilter(context);
    }
    public override object? VisitPath(MoiraiParser.PathContext context)
    {
        if (context.VAR_ID() != null)
        {
            PushSemanticToken(context.VAR_ID().Symbol, SemanticTokenType.Variable);
            LinkLocation(context.VAR_ID());
        }
        if (context.SINGLETON_ID() != null)
        {
            PushSemanticToken(context.SINGLETON_ID().Symbol, SemanticTokenType.Type);
        }
        if (context.ID(0) != null)
        {
            PushSemanticToken(context.ID(0).Symbol, SemanticTokenType.Property);
            var prop = context.ID(0).GetText();
            if (context.VAR_ID() != null)
                prop = _variablesToTypenames.TryGetValue(context.VAR_ID().GetText(), out string type)
                    ? $"{type}__{prop}"
                    : prop;
            else if (context.VAR_ID() == null && context.SINGLETON_ID() == null && _implicitTypeName != null)
                prop = $"{_implicitTypeName}__{prop}";
            else if (context.VAR_ID() == null && context.SINGLETON_ID() != null)
                prop = $"{context.SINGLETON_ID().GetText()}__{prop}";
            // LinkLocation(context.ID(0));

            // if (Definitions.TryGetValue(prop, out var loc))
                // Locations.Add((GetRange(context.ID(0).Symbol), loc.Symbol, loc.FullDefinition));
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

        return null;//base.VisitExpr(context);
    }
    public override object? VisitValue(MoiraiParser.ValueContext context)
    {
        if (context.TYPE_ID() != null)
        {
            PushSemanticToken(context.Start, SemanticTokenType.Type);
            LinkLocation(context.TYPE_ID());
        }
        else if (context.@string() is MoiraiParser.StringContext s)
        {
            PushSemanticToken(s.QUOTE(0).Symbol, SemanticTokenType.String);
            foreach (var content in s.stringContent())
            {
             if(content.TEXT() != null)   
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
