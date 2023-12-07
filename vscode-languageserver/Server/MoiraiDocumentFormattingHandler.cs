using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using Microsoft.Extensions.Logging;
using Moirai.Parser;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

internal class MoiraiDocumentFormattingHandler : DocumentFormattingHandlerBase
{
    private readonly ILogger<MyWorkspaceSymbolsHandler> _logger;
    private readonly MoiraiCache _moiraiCache;

    public MoiraiDocumentFormattingHandler(ILogger<MyWorkspaceSymbolsHandler> logger, MoiraiCache moiraiCache)
    {
        _logger = logger;
        _moiraiCache = moiraiCache;
    }

    protected override DocumentFormattingRegistrationOptions CreateRegistrationOptions(DocumentFormattingCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new DocumentFormattingRegistrationOptions { DocumentSelector = MoiraiLanguage.Selector };
    }

    public override async Task<TextEditContainer?> Handle(DocumentFormattingParams request, CancellationToken cancellationToken)
    {
        var content = _moiraiCache.GetContent(request.TextDocument.Uri);
        if (content == String.Empty)
            return null;
        var v = new FormattingVisitor();
        StoryParser.SetupParser(content, out var parser, v, mergeChannels:true);
        var r = v.Parser.r();
        var edits = r.Accept(v).ToList();
        edits.Sort((x,y) => x.Range.Start.CompareTo(y.Range.Start));
        _logger.LogCritical($"Format: {edits.Count} edits\n{string.Join("\n", edits.Select(e=> $"{e}: '{e.NewText}'"))}");
        return new TextEditContainer(edits);

    }

    internal class FormattingVisitor : MoiraiParserBaseVisitor<IEnumerable<TextEdit>>, StoryParser.IVisitor
    {
        private int _indent = 0;
        private string IndentString() => new string(' ', _indent * 4);
        private int IndentCount() => _indent * 4;
        public List<StoryParser.Error> Errors { get; } = new();
        public MoiraiParser Parser { get; set; }
        public (int offsetLine, int offsetColumn) offset { get; set; }
        protected override List<TextEdit> AggregateResult(IEnumerable<TextEdit> aggregate, IEnumerable<TextEdit> nextResult)
        {
            if (!(aggregate is List<TextEdit> l))
            {
                l = new(aggregate);
                aggregate = l;
            }
            l.AddRange(nextResult);
            return l;
        }

        protected override List<TextEdit> DefaultResult => new List<TextEdit>();
        public static Range GetRange(IToken symbol)
        {
            return new Range(symbol.Line - 1, symbol.Column, symbol.Line - 1, symbol.Column + symbol.Text.Length);
        }
        public static Range GetRange(IToken from, IToken to)
        {
            return new Range(from.Line - 1, from.Column, to.Line - 1, to.Column+1);
        }
        public static Range InsertBefore(IToken symbol)
        {
            return new Range(symbol.Line - 1, symbol.Column, symbol.Line - 1, symbol.Column);
        }
        public static Range InsertAfter(IToken symbol)
        {
            return new Range(symbol.Line - 1, symbol.Column + symbol.Text.Length, symbol.Line - 1, symbol.Column + symbol.Text.Length);
        }


        private IEnumerable<TextEdit> EnsureSpaces(ITerminalNode? node, int? rightSpaceCount, int? leftSpaceCount) =>
            EnsureSpaces(node?.Symbol, rightSpaceCount, leftSpaceCount);
        private IEnumerable<TextEdit> EnsureSpaces(IToken? node, int? rightSpaceCount, int? leftSpaceCount)
        {
            if(node == null)
                yield break;
            if(leftSpaceCount.HasValue)
            {
                var hidden = ((CommonTokenStream)Parser.TokenStream).GetHiddenTokensToLeft(node.TokenIndex);
                if ((hidden?.Count ?? 0) > 0)
                    foreach (var token in hidden)
                    {
                        if (token.Type == moirai_lexer.SPACE && (token.Text?.Length ?? 0) != leftSpaceCount.Value)
                            yield return new TextEdit
                                { NewText = new string(' ', leftSpaceCount.Value), Range = GetRange(token) };
                    }
                else if (leftSpaceCount.Value > 0) // no space but we want one
                {
                    yield return new TextEdit
                        { NewText = new string(' ', leftSpaceCount.Value), Range = InsertBefore(node) };
                    
                }
            }
            if(rightSpaceCount.HasValue)
            {
                var hidden = ((CommonTokenStream)Parser.TokenStream).GetHiddenTokensToRight(node.TokenIndex);
                if ((hidden?.Count ?? 0) > 0)
                    foreach (var token in hidden)
                    {
                        if (token.Type == moirai_lexer.SPACE && (token.Text?.Length ?? 0) != rightSpaceCount)
                            yield return new TextEdit
                                { NewText = new string(' ', rightSpaceCount.Value), Range = GetRange(token) };
                    }
                else if (rightSpaceCount.Value > 0) // no space but we want one
                {
                    yield return new TextEdit
                        { NewText = new string(' ', rightSpaceCount.Value), Range = InsertAfter(node) };
                    
                }
            }
        }
        private IEnumerable<TextEdit> EnsureSpaces(params (ITerminalNode node, int? rightSpaceCount, int? leftSpaceCount)[] arr)
        {
            foreach (var x in arr)
            {
                foreach (var e in EnsureSpaces(x.node?.Symbol, x.rightSpaceCount, x.leftSpaceCount))
                {
                    yield return e;
                }
            }
            
        }
        private IEnumerable<TextEdit> EnsureSpaces(params (IToken node, int? rightSpaceCount, int? leftSpaceCount)[] arr)
        {
            foreach (var x in arr)
            {
                foreach (var e in EnsureSpaces(x.node, x.rightSpaceCount, x.leftSpaceCount))
                {
                    yield return e;
                }
            }
            
        }

        public override IEnumerable<TextEdit> VisitFilter(MoiraiParser.FilterContext context)
        {
            return EnsureSpaces(
                (context.AT(), 0, 0)    
            );
        }

        public override IEnumerable<TextEdit> VisitCategories(MoiraiParser.CategoriesContext context)
        {
            return context.ID()?.SelectMany(x => EnsureSpaces(x?.Symbol, 1, null));
        }

        private IEnumerable<TextEdit> Indent(IEnumerable<TextEdit> indented)
        {
            _indent++;
            foreach (var e in indented)
            {
                yield return e;
            }

            _indent--;
        }

        public override IEnumerable<TextEdit> VisitMatch_case(MoiraiParser.Match_caseContext context)
        {
            _indent++;
            foreach (var e in EnsureSpaces(context.Start, null, IndentCount()))
                yield return e;
            foreach (var e in EnsureSpaces(context.ARROW(), 1, 1))
                yield return e;
            if(context.scope() != null)
                foreach (var e in context.scope().Accept(this))
                    yield return e;
            _indent--;
        }

        public override IEnumerable<TextEdit> VisitEvent(MoiraiParser.EventContext context)
        {
            return EnsureSpaces(
                (context.EVENT(), 1, 0),
                (context.ID(), 1, null)
                ).Concat(base.VisitEvent(context));
        }

        public override IEnumerable<TextEdit> VisitEffect(MoiraiParser.EffectContext context)
        {
            foreach (var e in  EnsureSpaces(context.Start, null, IndentCount())
                         .Concat(base.VisitEffect(context)))
                yield return e;
        }

        public override IEnumerable<TextEdit> VisitScope(MoiraiParser.ScopeContext context)
        {
            _indent++;
            foreach (var e in  base.VisitScope(context))
                yield return e;
            _indent--;

            foreach (var e in EnsureSpaces(context.SCOPE_CLOSE(), null, IndentCount()))
                yield return e;
        }

        public override IEnumerable<TextEdit> VisitType_definition(MoiraiParser.Type_definitionContext context)
        {
            // if (context.SCOPE_OPEN() != null)
            //     yield return new TextEdit
            //         { NewText = "", Range = GetRange(context.SCOPE_OPEN().Symbol, context.SCOPE_CLOSE().Symbol) };
            foreach (var e in  base.VisitType_definition(context))
                yield return e;
        }

        public override IEnumerable<TextEdit> VisitExpr(MoiraiParser.ExprContext context)
        {
            return EnsureSpaces(context.op, 1, 1).Concat(base.VisitExpr(context));
        }

        public override IEnumerable<TextEdit> VisitSet(MoiraiParser.SetContext context)
        {
            return EnsureSpaces(context.EQ(), 1, 1).Concat(base.VisitSet(context));
        }

        public override IEnumerable<TextEdit> VisitCall(MoiraiParser.CallContext context)
        {
            if (context.VAR_ID() != null)
            {
                return EnsureSpaces(
                    (context.VAR_ID(), 0, 1),
                    (context.COLON(), 1, 0),
                    (context.PAREN_OPEN(), 0, null),
                    (context.PAREN_CLOSE(), null, 0)
                ).Concat(base.VisitCall(context));
            }
            return EnsureSpaces(
                (context.ID(), 0, null),
                (context.PAREN_OPEN(), 0, null),
                (context.PAREN_CLOSE(), null, 0)
            ).Concat(base.VisitCall(context));
        }

        public override IEnumerable<TextEdit> VisitIf(MoiraiParser.IfContext context)
        {
            
            return  EnsureSpaces(
                (context.ELSE(), 1, 1)
            ).Concat(base.VisitIf(context));
        }
        
        public override IEnumerable<TextEdit> VisitProp_definition(MoiraiParser.Prop_definitionContext context)
        {
            return EnsureSpaces(
                (context.PROP(), 1, 4),
                (context.ID(0), 0, null),
                (context.COLON(), 1, null),
                (context.TYPE_ID() ?? context.ID(1), 0, null)
                );
        }
    }
}
