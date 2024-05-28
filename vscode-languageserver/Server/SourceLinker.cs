using System.Text;
using IntervalTree;
using Moirai.Parser;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;

public class SourceLinker : StoryParser.ILinker
{
    private Dictionary<EntityTypeId, TokenVisitor.TypeDefinition> _typeDefinitions = new();
    private Dictionary<PropertyId, TokenVisitor.PropertyDefinition> _propertyDefinitions = new();
    private IntervalTree<Position, TokenVisitor.Definition> _tree = new();

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
                DeclareTypeProperty(default!, propertyDefinition.PropertyId);
            }
        }
    }

    public TokenVisitor.Definition? GetDefinitionAt(Position requestPosition)
    {
        return _tree.Query(requestPosition).FirstOrDefault();
    }

    public void DeclareType(StoryParser.AstVisitor.FileRange? range, EntityTypeId typeId, string? inlineDefinition = null)
    {
        var r = range?.ToLspRange();
        var typeDefinition = new TokenVisitor.TypeDefinition(typeId, r) { InlineDefinition = inlineDefinition};
        _typeDefinitions.Add(typeId, typeDefinition);
    }

    public void LinkType(StoryParser.AstVisitor.FileRange range, EntityTypeId entityType)
    {
        var r = range.ToLspRange();
        _tree.Add(r.Start, r.End, _typeDefinitions[entityType]);
    }

    public void DeclareTypeProperty(StoryParser.AstVisitor.FileRange range, PropertyId propertyId)
    {
        var r = range.ToLspRange();
        var typeDefinition = new TokenVisitor.PropertyDefinition(propertyId, r);
        _propertyDefinitions.Add(propertyId, typeDefinition);
    }

    public void LinkProperty(StoryParser.AstVisitor.FileRange range, PropertyId propertyId)
    {
        var r = range.ToLspRange();
        _tree.Add(r.Start, r.End, _propertyDefinitions[propertyId]);
        
    }
}