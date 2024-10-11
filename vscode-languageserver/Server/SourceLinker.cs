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

    public void DeclareType(FileRange? range, EntityTypeId typeId, string? inlineDefinition = null)
    {
        var r = range?.ToLspRange();
        var typeDefinition = new TokenVisitor.TypeDefinition(typeId, r) { InlineDefinition = inlineDefinition};
        TypeDefinitions.Add(typeId, typeDefinition);
    }

    public void LinkType(FileRange range, EntityTypeId entityType)
    {
        var r = range.ToLspRange();
        if(TypeDefinitions.TryGetValue(entityType, out var definition))
            _tree.Add(r.Start, r.End, definition);
    }

    public void DeclareTypeProperty(FileRange? range, PropertyId propertyId, string? inlineDefinition = null)
    {
        var r = range?.ToLspRange();
        var typeDefinition = new TokenVisitor.PropertyDefinition(propertyId, r) { InlineDefinition = inlineDefinition};
        _propertyDefinitions.Add(propertyId, typeDefinition);
    }

    public void LinkProperty(FileRange range, PropertyId propertyId)
    {
        var r = range.ToLspRange();
        _tree.Add(r.Start, r.End, _propertyDefinitions[propertyId]);
        
    }

    public void DeclareEnum(FileRange range, EnumDefinitionId enumId)
    {
        var r = range.ToLspRange();
        var enumDefinition = new TokenVisitor.EnumDefinition(enumId, r);
        _enumDefinitions.Add(enumId, enumDefinition);
    }

    public void LinkEnum(FileRange range, EnumDefinitionId enumId)
    {
        var r = range.ToLspRange();
        _tree.Add(r.Start, r.End, _enumDefinitions[enumId]);
    }

    public void LinkEnumMember(FileRange range, PropertyValue enumValue)
    {
        var r = range.ToLspRange();
        var enumType = Database.Instance.Enums[enumValue.Type.Index];
        var enumDef = _enumDefinitions[enumType.Index];
        _tree.Add(r.Start, r.End, enumDef.MemberDefinition(enumValue));
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
        }
    }

    public void LinkFunction(FileRange range, IFunctionDescriptor descriptor)
    {
        var r = range.ToLspRange();
        _tree.Add(r.Start, r.End, _funDefinitions[descriptor.FuncName]);

    }
}
