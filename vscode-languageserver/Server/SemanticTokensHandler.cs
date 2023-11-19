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
    public record struct Definition(Range Symbol, Range FullDefinition)
    {
        public Definition(IToken token, ParserRuleContext fullDefinition) : this(GetRange(token), GetRange(fullDefinition))
        {
        }
    }
    private readonly ILogger _logger;
    private readonly DocumentUri _documentUri;
    public readonly List<(Range range, SemanticTokenType type, string[] modifiers)> SemanticTokens = new();
    public readonly Dictionary<string, Definition> Definitions = new();
    public readonly List<(Range, Range)> Locations = new();
    public List<SymbolInformationOrDocumentSymbol> Symbols = new();
    public List<StoryParser.Error> Errors { get; } = new();
    public MoiraiParser Parser { get; set; }
    public (int offsetLine, int offsetColumn) offset { get; set; }

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
    public override object? VisitAction(MoiraiParser.ActionContext context)
    {
        var id = context.ID();
        // base.VisitAction(context);
        context.filter()?.Accept(this);
        PushSemanticToken(context.RULE().Symbol, SemanticTokenType.Keyword);
        PushSemanticToken(id.Symbol, SemanticTokenType.Class);
        PushSymbol(id.Symbol, SymbolKind.Function);
        foreach (var cat in context.categories().ID())
        {
            PushSemanticToken(cat.Symbol, SemanticTokenType.Decorator);
            
        }

        foreach (var child in context.children)
        {
            if (child is MoiraiParser.EffectContext e)
                e.Accept(this);
            else if (child is MoiraiParser.CommentContext c)
                c.Accept(this);
        }
        return null ;
    }

    public override object? VisitEvent(MoiraiParser.EventContext context)
    {
        var id = context.ID();
        PushSemanticToken(context.EVENT().Symbol, SemanticTokenType.Keyword);
        PushSemanticToken(id.Symbol, SemanticTokenType.Class, SemanticTokenModifier.Definition);
        PushSymbol(id.Symbol, SymbolKind.Event);
        foreach (var cat in context.categories().ID())
        {
            PushSemanticToken(cat.Symbol, SemanticTokenType.Decorator, SemanticTokenModifier.Modification);
            
        }
        
        foreach (var comment in context.comment())
            comment.Accept(this);
        foreach (var effect in context.effect())
            effect.Accept(this);
        return null;
    }

    public override object? VisitProp_definition(MoiraiParser.Prop_definitionContext context)
    {
        PushSemanticToken(context.PROP().Symbol, SemanticTokenType.Keyword);
        PushSemanticToken(context.ID(0).Symbol, SemanticTokenType.Property);
        PushSymbol(context.ID(0).Symbol, SymbolKind.Property);
        Definitions.Add( context.ID(0).GetText(), new Definition(context.ID(0).Symbol, context));
        if(context.TYPE_ID() != null)
            PushSemanticToken(context.TYPE_ID().Symbol, SemanticTokenType.Type);
        else
            PushSemanticToken(context.ID(1).Symbol, SemanticTokenType.Type);
        return base.VisitProp_definition(context);
    }
    public override object? VisitType_definition(MoiraiParser.Type_definitionContext context)
    {
       
        PushSymbol(context.TYPE_ID().Symbol, SymbolKind.Class);
        Definitions.Add( context.TYPE_ID().GetText(), new Definition(context.TYPE_ID().Symbol, context));
        PushSemanticToken(context.ENTITY().Symbol, SemanticTokenType.Keyword);
        PushSemanticToken(context.TYPE_ID().Symbol, SemanticTokenType.Type);
        return base.VisitType_definition(context);
    }

    public override object? VisitEnum_definition(MoiraiParser.Enum_definitionContext context)
    {
        PushSymbol(context.TYPE_ID(0).Symbol, SymbolKind.Enum);
        Definitions.Add( context.TYPE_ID(0).GetText(),  new Definition(context.TYPE_ID(0).Symbol, context));
        PushSemanticToken(context.ENUM().Symbol, SemanticTokenType.Keyword);

        PushSemanticToken(context.TYPE_ID(0).Symbol, SemanticTokenType.Enum);
        foreach (var member in context.TYPE_ID().Skip(1))
        {
            PushSemanticToken(member.Symbol, SemanticTokenType.EnumMember);
            Definitions.Add($"{context.TYPE_ID(0).GetText()}.{member.GetText()}", new Definition(member.Symbol, context));
        }        
        return base.VisitEnum_definition(context);
    }

    public override object? VisitEnum_value(MoiraiParser.Enum_valueContext context)
    {
        PushSemanticToken(context.TYPE_ID(0).Symbol, SemanticTokenType.Enum);
        PushSemanticToken(context.TYPE_ID(1).Symbol, SemanticTokenType.EnumMember);
        if (Definitions.TryGetValue(context.TYPE_ID(0).GetText(), out var loc))
            Locations.Add((GetRange(context.TYPE_ID(0).Symbol), loc.Symbol));
        if (Definitions.TryGetValue(context.GetText(), out var loc2))
            Locations.Add((GetRange(context.TYPE_ID(1).Symbol), loc2.Symbol));
        return base.VisitEnum_value(context);
    }

    public override object? VisitVar(MoiraiParser.VarContext context)
    {
        PushSemanticToken(context.VAR().Symbol, SemanticTokenType.Keyword);
        PushSemanticToken(context.VAR_ID().Symbol, SemanticTokenType.Variable);
            
        return context.expr().Accept(this);
    }

    public override object? VisitCall(MoiraiParser.CallContext context)
    {
        PushSemanticToken(context.ID().Symbol, SemanticTokenType.Function);
        if (context.VAR_ID() != null)
            PushSemanticToken(context.VAR_ID().Symbol, SemanticTokenType.Variable);
        return base.VisitCall(context);
    }
    
    public override object? VisitWhen(MoiraiParser.WhenContext context)
    {
        PushSemanticToken(context.WHEN().Symbol, SemanticTokenType.Keyword);
        return base.VisitWhen(context);
    }

    

    public override object? VisitTerminal(ITerminalNode node)
    {

        if (node.Parent is MoiraiParser.SetContext && node.Symbol.Text == "set")
        {
            PushSemanticToken(node.Symbol, SemanticTokenType.Keyword);
        }
        if (node.Parent is MoiraiParser.WhenContext && node.Symbol.Text == "when")
        {
            PushSemanticToken(node.Symbol, SemanticTokenType.Keyword);
        }
        return base.VisitTerminal(node);
    }
    public override object? VisitComment(MoiraiParser.CommentContext context)
    {
        PushSemanticToken(context, SemanticTokenType.Comment);
        
        return null;
    }
    public override object? VisitFilter(MoiraiParser.FilterContext context)
    {
        PushSemanticToken(context, SemanticTokenType.Decorator);
        return null;// base.VisitFilter(context);
    }
    public override object? VisitPath(MoiraiParser.PathContext context)
    {
        if (context.ID(0) != null)
        {
            PushSemanticToken(context.ID(0).Symbol, SemanticTokenType.Property);
            if (Definitions.TryGetValue(context.ID(0).GetText(), out var loc))
                Locations.Add((GetRange(context.ID(0).Symbol), loc.Symbol));
        }
        if (context.VAR_ID() != null)
        {
            PushSemanticToken(context.VAR_ID().Symbol, SemanticTokenType.Variable);
        }
        return base.VisitPath(context);
    }
    public override object? VisitExpr(MoiraiParser.ExprContext context)
    {
        if (context.value() != null)
            return context.value().Accept(this);
        if (context.paren_expr != null)
            return context.paren_expr.Accept(this);

        if (context.op != null)
        {
            PushSemanticToken(context.op, SemanticTokenType.Operator);
        }
        return base.VisitExpr(context);
    }
    public override object? VisitValue(MoiraiParser.ValueContext context)
    {
        if (context.TYPE_ID() != null)
        {
            PushSemanticToken(context.Start, SemanticTokenType.Type);
            if (Definitions.TryGetValue(context.TYPE_ID().GetText(), out var loc))
                Locations.Add((GetRange(context.TYPE_ID().Symbol), loc.Symbol));
        }
        else if (context.@string() != null)
            PushSemanticToken(context.Start, SemanticTokenType.String);
        else if (context.@bool() != null || context.number() != null || context.NULL() != null)
            PushSemanticToken(context.Start, SemanticTokenType.Number);
        else
            return base.VisitValue(context);

        return null;
    }
}
