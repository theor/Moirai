using Newtonsoft.Json.Linq;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

/// <summary>
/// Emits the inline "N usages" overlay (CodeLens) above each declaration. The count is computed
/// server-side from the same occurrence index that powers find-references; clicking the lens runs
/// the client-side <c>moirai.showReferences</c> command, which re-queries references and opens the
/// peek view at the declaration.
/// </summary>
public class MoiraiCodeLensHandler : CodeLensHandlerBase
{
    private readonly MoiraiCache _moiraiCache;

    public MoiraiCodeLensHandler(MoiraiCache moiraiCache)
    {
        _moiraiCache = moiraiCache;
    }

    public override Task<CodeLensContainer?> Handle(CodeLensParams request, CancellationToken cancellationToken)
    {
        var uri = request.TextDocument.Uri;
        var usageLenses = _moiraiCache.GetDeclarationUsages(request.TextDocument)
            .Select(d => new CodeLens
            {
                Range = d.nameRange,
                Command = new Command
                {
                    Title = d.usageCount == 1 ? "1 usage" : $"{d.usageCount} usages",
                    // Client-registered command; see extension.ts. Args reach it as plain JSON values.
                    Name = "moirai.showReferences",
                    Arguments = new JArray(uri.ToString(), d.nameRange.Start.Line, d.nameRange.Start.Character),
                },
            });

        // Informational lens over each Changed-trigger showing the properties whose changes it reacts
        // to (the engine's property-gated dispatch set). Empty command name => rendered as plain text.
        var triggerLenses = _moiraiCache.GetTriggerReadPropLenses(request.TextDocument)
            .Select(t => new CodeLens
            {
                Range = t.range,
                Command = new Command { Title = t.title, Name = "" },
            });

        var lenses = usageLenses.Concat(triggerLenses).ToArray();

        return Task.FromResult<CodeLensContainer?>(new CodeLensContainer(lenses));
    }

    public override Task<CodeLens> Handle(CodeLens request, CancellationToken cancellationToken)
    {
        // Counts are resolved eagerly in the list handler, so there is nothing to fill in here.
        return Task.FromResult(request);
    }

    protected override CodeLensRegistrationOptions CreateRegistrationOptions(CodeLensCapability capability,
        ClientCapabilities clientCapabilities)
    {
        return new CodeLensRegistrationOptions
        {
            DocumentSelector = MoiraiLanguage.Selector,
            ResolveProvider = false,
        };
    }
}
