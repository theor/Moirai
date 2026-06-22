using System.Text;
using Antlr4.Runtime.Tree;
using IntervalTree;
using Moirai.Parser;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

public class SourceLinker : StoryParser.ILinker
{
    public readonly Dictionary<EntityTypeId, TokenVisitor.TypeDefinition> TypeDefinitions = new();
    private Dictionary<PropertyId, TokenVisitor.PropertyDefinition> _propertyDefinitions = new();
    private Dictionary<EnumDefinitionId, TokenVisitor.EnumDefinition> _enumDefinitions = new();
    private IntervalTree<Position, TokenVisitor.Definition> _tree = new();
    private Dictionary<string, TokenVisitor.FunctionDefinition> _funDefinitions = new();

    public SourceLinker()
    {
        for (int i = 1; i < Database.Instance.BuiltinTypes; i++)
        {
            var type = Database.Instance.Types[i];
            StringBuilder sb = new();
            Database.Instance.Printer.PrintType(sb, type);
            DeclareType(default!, type.Id, sb.ToString());
            foreach (var propertyDefinition in type.Properties)
            {
                sb.Clear();
                Database.Instance.Printer.PrintTypeProperty(sb, propertyDefinition);
                DeclareTypeProperty(default!, propertyDefinition.PropertyId, sb.ToString());
            }

            foreach (var functionDescriptor in StoryParser.Functions)
            {
                DeclareFunction(default!, functionDescriptor, "inline def of " + functionDescriptor.FuncName);
            }
        }
    }

    public IEnumerable<TokenVisitor.Definition> GetDefinitions(Position pos, TokenVisitor.DefinitionType type = TokenVisitor.DefinitionType.Unknown)
    {
        if(type == TokenVisitor.DefinitionType.Unknown)
            return _tree.Query(pos);
        return _tree.Query(pos).Where(d => d.Type == type);
    }

    public TokenVisitor.Definition? GetDefinitionAt(Position requestPosition)
    {
        return _tree.Query(requestPosition).FirstOrDefault(x => x.Type != TokenVisitor.DefinitionType.VariableScope);
    }

    /// <summary>
    /// All occurrences (declaration + usages) of the symbol under <paramref name="pos"/>. Every
    /// symbol kind links its declaration name-token into the tree, so references resolve whether the
    /// cursor sits on the declaration or a usage, and the declaration is dropped when
    /// <paramref name="includeDeclaration"/> is false.
    /// </summary>
    public IEnumerable<Range> GetReferences(Position pos, bool includeDeclaration)
    {
        var target = GetDefinitionAt(pos);
        if (target?.SymbolKey == null)
            return Enumerable.Empty<Range>();

        // Variables/functions store the declaration name-token as FullDefinition; types/properties/
        // enums store a whole-block FullDefinition but track the name-token in DeclarationNameRange.
        var declaration = target.DeclarationNameRange ?? target.FullDefinition;
        var seen = new HashSet<Range>();
        var results = new List<Range>();
        foreach (var entry in _tree)
        {
            var def = entry.Value;
            if (def.Type != target.Type || !Equals(def.SymbolKey, target.SymbolKey))
                continue;

            var range = new Range(entry.From, entry.To);
            // For variables/functions the declaration name-token is itself in the tree; drop it
            // when the caller doesn't want the declaration included.
            if (!includeDeclaration && declaration != null && range == declaration)
                continue;
            if (seen.Add(range))
                results.Add(range);
        }

        return results;
    }

    // Symbol kinds that get an inline "N usages" CodeLens above their declaration. Enum members and
    // variables are excluded: members share a line (lenses would collide) and locals are too noisy.
    private const TokenVisitor.DefinitionType LensKinds =
        TokenVisitor.DefinitionType.Type | TokenVisitor.DefinitionType.TypeProperty |
        TokenVisitor.DefinitionType.Enum | TokenVisitor.DefinitionType.Function;

    /// <summary>
    /// One entry per declaration that should carry a usage-count CodeLens: the declaration's
    /// name-token range and the number of usages (occurrences other than the declaration itself).
    /// </summary>
    public IEnumerable<(Range nameRange, int usageCount)> GetDeclarationUsages()
    {
        // Group every linked occurrence by the symbol it denotes, remembering the shared definition.
        var groups = new Dictionary<(TokenVisitor.DefinitionType, object), (TokenVisitor.Definition def, HashSet<Range> ranges)>();
        foreach (var entry in _tree)
        {
            var def = entry.Value;
            if ((def.Type & LensKinds) == 0 || def.SymbolKey == null)
                continue;
            var key = (def.Type, def.SymbolKey);
            if (!groups.TryGetValue(key, out var group))
                groups[key] = group = (def, new HashSet<Range>());
            group.ranges.Add(new Range(entry.From, entry.To));
        }

        foreach (var (def, ranges) in groups.Values)
        {
            if (def.DeclarationNameRange is not { } nameRange)
                continue; // builtin / no in-source declaration
            yield return (nameRange, ranges.Count(r => r != nameRange));
        }
    }

    public void DeclareType(FileRange? range, EntityTypeId typeId, string? inlineDefinition = null)
    {
        var r = range?.ToLspRange();
        var typeDefinition = new TokenVisitor.TypeDefinition(typeId, r) { InlineDefinition = inlineDefinition};
        TypeDefinitions.Add(typeId, typeDefinition);
    }

    public void LinkType(FileRange range, EntityTypeId entityType, bool isDeclaration = false)
    {
        var r = range.ToLspRange();
        if (TypeDefinitions.TryGetValue(entityType, out var definition))
        {
            _tree.Add(r.Start, r.End, definition);
            if (isDeclaration)
                definition.DeclarationNameRange = r;
        }
    }

    public void DeclareTypeProperty(FileRange? range, PropertyId propertyId, string? inlineDefinition = null)
    {
        var r = range?.ToLspRange();
        var typeDefinition = new TokenVisitor.PropertyDefinition(propertyId, r) { InlineDefinition = inlineDefinition};
        _propertyDefinitions.Add(propertyId, typeDefinition);
    }

    public void LinkProperty(FileRange range, PropertyId propertyId, bool isDeclaration = false)
    {
        var r = range.ToLspRange();
        var definition = _propertyDefinitions[propertyId];
        _tree.Add(r.Start, r.End, definition);
        if (isDeclaration)
            definition.DeclarationNameRange = r;
    }

    public void DeclareEnum(FileRange range, EnumDefinitionId enumId)
    {
        var r = range.ToLspRange();
        var enumDefinition = new TokenVisitor.EnumDefinition(enumId, r);
        _enumDefinitions.Add(enumId, enumDefinition);
    }

    public void LinkEnum(FileRange range, EnumDefinitionId enumId, bool isDeclaration = false)
    {
        var r = range.ToLspRange();
        var definition = _enumDefinitions[enumId];
        _tree.Add(r.Start, r.End, definition);
        if (isDeclaration)
            definition.DeclarationNameRange = r;
    }

    public void LinkEnumMember(FileRange range, PropertyValue enumValue, bool isDeclaration = false)
    {
        var r = range.ToLspRange();
        var enumType = Database.Instance.Enums[enumValue.Type.Index];
        var enumDef = _enumDefinitions[enumType.Index];
        var member = enumDef.MemberDefinition(enumValue);
        _tree.Add(r.Start, r.End, member);
        if (isDeclaration)
            member.DeclarationNameRange = r;
    }

    public void DeclareVariable(FileRange range,
        AstVisitor.VariableDeclaration variableDeclaration, FileRange variableScope)
    {
        var r = range.ToLspRange();
        _tree.Add(r.Start, r.End, new TokenVisitor.VariableDefinition(variableDeclaration, variableDeclaration.DeclarationRange));
        var scope = variableScope.ToLspRange();
        _tree.Add(scope.Start, scope.End, new TokenVisitor.VariableScopeDefinition(variableDeclaration, variableDeclaration.DeclarationRange));
    }

    public void LinkVariable(FileRange range, AstVisitor.VariableDeclaration decl)
    {
        var r = range.ToLspRange();
        _tree.Add(r.Start, r.End, new TokenVisitor.VariableDefinition(decl, decl.DeclarationRange));
    }

    public void DeclareFunction(FileRange fileRange, IFunctionDescriptor descriptor, string? inlineDef = null)
    {
        var r = fileRange.ToLspRange();
        var functionDefinition = new TokenVisitor.FunctionDefinition(descriptor, r){InlineDefinition = inlineDef};
        _funDefinitions.Add(descriptor.FuncName, functionDefinition);
        if (!r.IsEmpty())
        {
            _tree.Add(r.Start, r.End, functionDefinition);
            functionDefinition.DeclarationNameRange = r;
        }
    }

    public void LinkFunction(FileRange range, IFunctionDescriptor descriptor)
    {
        var r = range.ToLspRange();
        _tree.Add(r.Start, r.End, _funDefinitions[descriptor.FuncName]);

    }
}
