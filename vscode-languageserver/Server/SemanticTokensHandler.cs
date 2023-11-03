using Antlr4.Runtime.Tree;
using Microsoft.Extensions.Logging;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

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

    class TokenVisitor : MoiraiBaseVisitor<object?>, StoryParser.IVisitor
    {
        private readonly SemanticTokensBuilder _builder;
        private readonly ILogger _logger;
        public TokenVisitor(SemanticTokensBuilder builder, ILogger logger)
        {
            _builder = builder;
            _logger = logger;
        }
        public List<StoryParser.Error> Errors { get; } = new();

        public override object? VisitAction(Moirai.ActionContext context)
        {
            var id = context.ID();
            _builder.Push(context.RULE().Symbol.Line - 1, context.RULE().Symbol.Column, context.RULE().Symbol.Text.Length, SemanticTokenType.Keyword, Array.Empty<string>());
            _builder.Push(id.Symbol.Line - 1, id.Symbol.Column, id.Symbol.Text.Length, SemanticTokenType.Class, SemanticTokenModifier.Definition);
            return base.VisitAction(context);
        }
        public override object? VisitProp_definition(Moirai.Prop_definitionContext context)
        {
            _builder.Push(context.PROP().Symbol.Line - 1, context.PROP().Symbol.Column, context.PROP().Symbol.Text.Length, SemanticTokenType.Keyword, Array.Empty<string>());
            return base.VisitProp_definition(context);
        }
        public override object? VisitType_definition(Moirai.Type_definitionContext context)
        {
            _builder.Push(context.ENTITY().Symbol.Line - 1, context.ENTITY().Symbol.Column, context.ENTITY().Symbol.Text.Length, SemanticTokenType.Keyword, Array.Empty<string>());
            _builder.Push(context.TYPE_ID().Symbol.Line - 1, context.TYPE_ID().Symbol.Column, context.TYPE_ID().Symbol.Text.Length, SemanticTokenType.Type, Array.Empty<string>());
            return base.VisitType_definition(context);
        }
        public override object? VisitEnum_definition(Moirai.Enum_definitionContext context)
        {
            _builder.Push(context.ENUM().Symbol.Line - 1, context.ENUM().Symbol.Column, context.ENUM().Symbol.Text.Length, SemanticTokenType.Keyword, Array.Empty<string>());
            return base.VisitEnum_definition(context);
        }
        public override object? VisitCall(Moirai.CallContext context)
        {
            _builder.Push(context.ID().Symbol.Line - 1, context.ID().Symbol.Column, context.ID().Symbol.Text.Length, SemanticTokenType.Function,
                Array.Empty<string>());
            return base.VisitCall(context);
        }
        public override object? VisitCall_assign(Moirai.Call_assignContext context)
        {
            _builder.Push(context.ID().Symbol.Line - 1, context.ID().Symbol.Column, context.ID().Symbol.Text.Length, SemanticTokenType.Function,
                Array.Empty<string>());
            if(context.VAR_ID() != null)
                _builder.Push(context.VAR_ID().Symbol.Line - 1, context.VAR_ID().Symbol.Column, context.VAR_ID().Symbol.Text.Length, SemanticTokenType.Variable,
                    Array.Empty<string>());
            return base.VisitCall_assign(context);
        }
        public override object? VisitTerminal(ITerminalNode node)
        {
            
            if(node.Parent is Moirai.SetContext)
            {
                _builder.Push(node.Symbol.Line - 1, node.Symbol.Column, node.Symbol.Text.Length, SemanticTokenType.Keyword,
                Array.Empty<string>());
            }
            return base.VisitTerminal(node);
        }

        // public override object? VisitAssign(Moirai.AssignContext context)
        // {
        //     if (context.VAR_ID() != null)
        //     {
        //         _builder.Push(context.VAR_ID().Symbol.Line - 1, context.VAR_ID().Symbol.Column, context.VAR_ID().Symbol.Text.Length, SemanticTokenType.Variable,
        //             Array.Empty<string>());
        //     }
        //     return base.VisitAssign(context);
        // }
        public override object? VisitPath(Moirai.PathContext context)
        {
            if (context.VAR_ID() != null)
            {
                _builder.Push(context.VAR_ID().Symbol.Line - 1, context.VAR_ID().Symbol.Column, context.VAR_ID().Symbol.Text.Length, SemanticTokenType.Variable,
                    Array.Empty<string>());
            }
            return base.VisitPath(context);
        }
        public override object? VisitExpr(Moirai.ExprContext context)
        {
            if (context.value() != null)
                return context.value().Accept(this);
            if (context.paren_expr != null)
                return context.paren_expr.Accept(this);  
            if (context.op != null)
            {
               
                _builder.Push(context.op.Line - 1, context.op.Column, context.op.Text.Length,
                    SemanticTokenType.Operator, Array.Empty<string>());
            }
            return base.VisitExpr(context);
        }
        public override object? VisitValue(Moirai.ValueContext context)
        {
            if(context.TYPE_ID() != null)
                _builder.Push(context.Start.Line-1, context.Start.Column, context.GetText().Length, SemanticTokenType.Type, Array.Empty<string>());
            else if(context.@string() != null)
                _builder.Push(context.Start.Line-1, context.Start.Column, context.GetText().Length, SemanticTokenType.String, Array.Empty<string>());
            else if(context.@bool() != null || context.number() != null || context.NULL() != null)
                _builder.Push(context.Start.Line-1, context.Start.Column, context.GetText().Length, SemanticTokenType.Number, Array.Empty<string>());
            else
                return base.VisitValue(context);

            return null;
        }
    }

    protected override async Task Tokenize(
        SemanticTokensBuilder builder, ITextDocumentIdentifierParams identifier,
        CancellationToken cancellationToken
    )
    {
        _logger.LogCritical("Tokenize");
        // using var typesEnumerator = RotateEnum(SemanticTokenType.Defaults).GetEnumerator();
        // using var modifiersEnumerator = RotateEnum(SemanticTokenModifier.Defaults).GetEnumerator();
        // you would normally get this from a common source that is managed by current open editor, current active editor, etc.
        var content = await File.ReadAllTextAsync(DocumentUri.GetFileSystemPath(identifier), cancellationToken).ConfigureAwait(false);
        await Task.Yield();

        var visitor = new TokenVisitor(builder, _logger);
        StoryParser.SetupParser(content, out _moiraiCache.Errors, out var parser, visitor);
        var r = parser.r();
        r.Accept(visitor);
        _logger.LogCritical("Errors " + _moiraiCache.Errors.Count);
        // foreach (var error in errors)
        // {
        //     // builder.Push(error.Line, error.Col, 1, Seman);
        // }

        // foreach (var (line, text) in content.Split('\n').Select((text, line) => (line, text)))
        // {
        //     int index = -1;
        //     if ((index = text.IndexOf("@")) != -1)
        //         builder.Push(line, index, text.Length - index - 1, SemanticTokenType.Class, SemanticTokenModifier.Definition);
        //
        //     else
        //     {
        //         index = -1;
        //         while ((index = text.IndexOf('"', index+1)) != -1)
        //         {
        //             int closingIndex = text.IndexOf('"', index + 1);
        //             if (closingIndex != -1)
        //             {
        //                 builder.Push(new Range(
        //                     new Position(line, index),
        //                     new Position(line, closingIndex + 1)
        //                 ), SemanticTokenType.String, SemanticTokenModifier.DefaultLibrary);
        //             }
        //         }
        //         index = -1;
        //         while ((index = text.IndexOf('$', index + 1)) != -1)
        //         {
        //             int end = index + 1;
        //             while (end < text.Length && Char.IsLetterOrDigit(text[end])) end++;
        //             builder.Push(new Range(
        //                 new Position(line, index),
        //                 new Position(line, end)
        //             ), SemanticTokenType.Variable, SemanticTokenModifier.DefaultLibrary);
        //         }
        //     }
        //    
        // }
    }

    protected override Task<SemanticTokensDocument>
        GetSemanticTokensDocument(ITextDocumentIdentifierParams @params, CancellationToken cancellationToken)
    {
        return Task.FromResult(new SemanticTokensDocument(RegistrationOptions.Legend));
    }


    private IEnumerable<T> RotateEnum<T>(IEnumerable<T> values)
    {
        while (true)
        {
            foreach (var item in values)
                yield return item;
        }
    }

    protected override SemanticTokensRegistrationOptions CreateRegistrationOptions(
        SemanticTokensCapability capability, ClientCapabilities clientCapabilities
    )
    {
        return new SemanticTokensRegistrationOptions
        {
            DocumentSelector = TextDocumentSelector.ForLanguage( "moirai"),
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