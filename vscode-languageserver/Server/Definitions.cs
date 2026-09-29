using System.Text;
using Moirai.Parser;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using Range = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;

/// The language server's symbol table entries: one Definition per declaration or usage of a named
/// thing, keyed into SourceLinker's interval tree by source range. Go-to-definition, hover,
/// find-references, the "N usages" CodeLens and semantic highlighting of identifiers all read this.
///
/// These are populated by StoryParser.ILinker callbacks during the AST-to-Database lowering, not by
/// a separate walk over the tree -- which is why they survived the move off ANTLR untouched. They
/// used to be nested inside the ANTLR-based TokenVisitor purely because that is where the walk
/// lived.
public static class MoiraiSymbol
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
        Table = 1 << 7,
        Event = 1 << 8,
    }

    public abstract class Definition(DefinitionType Type, string Name, Range? FullDefinition)
    {
        public DefinitionType Type { get; init; } = Type;
        public string Name { get; init; } = Name;
        public Range? FullDefinition { get; init; } = FullDefinition;
        public string? InlineDefinition { get; set; }

        /// <summary>
        /// Range of the name-token at the declaration site, when it has been linked into the tree
        /// (types/properties/enums declare a whole-block <see cref="FullDefinition"/>, so this
        /// pinpoints just the identifier). Used to honor <c>includeDeclaration:false</c> in find-references.
        /// </summary>
        public Range? DeclarationNameRange { get; set; }

        /// <summary>
        /// Identity of the symbol this definition refers to, used to group a declaration and all
        /// its usages together for "find references". Two definitions with the same <see cref="Type"/>
        /// and equal <see cref="SymbolKey"/> denote the same symbol.
        /// </summary>
        public virtual object? SymbolKey => null;

        public virtual void GetHoverText(List<MarkedString> markedStrings)
        {
        }
    }

    public abstract class Definition<T>(DefinitionType Type, T t, string Name, Range? FullDefinition)
        : Definition(Type, Name, FullDefinition)
    {
        public T Data = t;

        public override object? SymbolKey => Data;
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

    /// Keyed by the table's id rather than the engine object, so a key compares by value.
    public class TableDefinition(Moirai.Core.TableDefinition table, Range? declarationRange)
        : Definition<int>(DefinitionType.Table, table.Id, table.Name, declarationRange)
    {
        public override void GetHoverText(List<MarkedString> markedStrings)
        {
            markedStrings.Add(new MarkedString("moirai", $"table {Name}"));
        }
    }

    /// Keyed by the event's id, so a key compares by value.
    public class EventDefinition(EventTrigger rule, Range? declarationRange)
        : Definition<int>(DefinitionType.Event, rule.Id, rule.Name, declarationRange)
    {
        /// Run by the schedule (`@start`, `@frequency`) as well as by any call. Only an unscheduled
        /// event gets a usage lens: for it, calls are the only way it ever runs.
        public bool Scheduled { get; } = rule.Filter != null;

        public override void GetHoverText(List<MarkedString> markedStrings)
        {
            var printer = Database.Instance.Printer;
            var parameters = (rule.Parameters ?? []).Select(p => $"{p.ParamName}: {printer.Print(p.ParamType)}");
            markedStrings.Add(new MarkedString("moirai", $"event {rule.Name}({string.Join(", ", parameters)})"));
            markedStrings.Add(new MarkedString("Runs as a rule of its own when called by name."));
        }
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
            markedStrings.Add(new MarkedString("moirai", Signature(Data)));
            if (Data is FunctionDescriptor { Doc: { } doc })
                markedStrings.Add(new MarkedString(doc.Summary));
            else if (!string.IsNullOrEmpty(Data.Documentation))
                markedStrings.Add(new MarkedString(Data.Documentation));
        }

        /// `name($a: T, $b: U): R` for a story's own function -- a method leaves out the `$self` it is called
        /// on -- and a builtin's documented forms, one per line.
        public static string Signature(IFunctionDescriptor descriptor)
        {
            if (descriptor is FunctionDescriptor { Doc: { } doc })
                return string.Join('\n', doc.Signatures);
            if (descriptor is not UserFunctionDescriptor { Definition: var d })
                return $"{descriptor.FuncName}()";

            var printer = Database.Instance.Printer;
            var parameters = d.Parameters.Skip(d.IsInstanceMethod ? 1 : 0)
                .Select(p => $"{p.ParamName}: {printer.Print(p.ParamType)}");
            var returns = d.ReturnType == PropertyValue.ValueType.Null ? "" : $": {printer.Print(d.ReturnType)}";
            return $"{d.Name}({string.Join(", ", parameters)}){returns}";
        }
    }
}
