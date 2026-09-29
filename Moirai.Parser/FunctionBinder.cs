using Moirai.Core;
using Moirai.Parser.Ast;

namespace Moirai.Parser;

/// A built-in call whose arguments were checked against one of its <see cref="CallForm"/>s: which form
/// matched, and each argument's value in the shape its kind gives.
public sealed class BoundCall
{
    public FunctionParseContext Context { get; }
    public int FormIndex { get; }
    public CallForm Form { get; }
    readonly IValue?[] _values;
    readonly PropertyValue.ValueType[] _types;
    readonly object?[] _extras;

    internal BoundCall(FunctionParseContext context, int formIndex, CallForm form, int count)
    {
        Context = context;
        FormIndex = formIndex;
        Form = form;
        _values = new IValue?[count];
        _types = new PropertyValue.ValueType[count];
        _extras = new object?[count];
    }

    public int Count => _values.Length;
    public IValue Value(int i) => _values[i]!;
    public PropertyValue.ValueType Type(int i) => _types[i];
    public int Int(int i) => (int) _extras[i]!;
    public EnumDefinitionId Enum(int i) => (EnumDefinitionId) _extras[i]!;
    public TableDefinition Table(int i) => (TableDefinition) _extras[i]!;
    public InterpolatedString Text(int i) => (InterpolatedString) _values[i]!;
    public ExprNode? Node(int i) => Context.GetArgumentToken(i);

    public (PropertyPath Full, PropertyPath Owner, PropertyId Collection) Collection(int i) =>
        ((PropertyPath Full, PropertyPath Owner, PropertyId Collection)) _extras[i]!;

    internal void Set(int i, IValue? value, PropertyValue.ValueType type, object? extra = null)
    {
        _values[i] = value;
        _types[i] = type;
        _extras[i] = extra;
    }

    /// The call's return type, as the form declares it.
    public PropertyValue.ValueType ReturnType => Form.Returns switch
    {
        FnReturn.Number => PropertyValue.TypeNumber,
        FnReturn.Bool => PropertyValue.TypeBool,
        FnReturn.String => PropertyValue.TypeString,
        FnReturn.EnumOfArg => PropertyValue.TypeEnum(Enum(0)),
        FnReturn.TableEntryOfArg => Table(0).ValueType,
        _ => PropertyValue.ValueType.Null,
    };
}

/// Checks a built-in call against its forms (<see cref="FunctionDescriptor.Forms"/>) before its handler
/// runs: picks the form by argument count and, where two forms take the same count, by the syntactic kind
/// of their arguments (`random(Job)` vs `random(10)`); then parses each argument and checks it. Every
/// mistake is reported; the call is still bound when an argument has only the wrong type, so the rest of
/// the rule keeps parsing, and not bound when an argument cannot be read at all.
public static class FunctionBinder
{
    public static bool TryBind(string name, CallForm[] forms, FunctionParseContext ctx, out BoundCall bound)
    {
        var count = ctx.ArgCount;
        var candidates = forms.Select((f, i) => (Form: f, Index: i))
            .Where(c => count >= c.Form.MinArgs && (c.Form.MaxArgs is not { } max || count <= max))
            .ToList();

        if (candidates.Count == 0)
        {
            var expected = string.Join(" or ", forms.Select(f => f.Signature(name)));
            ctx.Visitor.AddError(StoryParser.ErrorCode.MissingArgument, ctx.CallContext.Span,
                $"{name}() takes {Arity(forms)}, got {count}: {expected}");
            bound = null!;
            return false;
        }

        var chosen = candidates.FirstOrDefault(c => LooksLike(c.Form, ctx));
        if (chosen.Form == null)
            chosen = candidates[0];

        bound = new BoundCall(ctx, chosen.Index, chosen.Form, count);
        bool readable = true;
        for (int i = 0; i < count; i++)
            readable &= BindArgument(name, chosen.Form, chosen.Form.ParamAt(i)!, i, ctx, bound);
        return readable;
    }

    static string Arity(CallForm[] forms)
    {
        var counts = forms.Select(f => f.MaxArgs is { } max
            ? f.MinArgs == max ? $"{max}" : $"{f.MinArgs} to {max}"
            : $"{f.MinArgs} or more").Distinct().ToList();
        return string.Join(" or ", counts) + " argument" + (counts is ["1"] ? "" : "s");
    }

    /// Whether the call's arguments have the syntactic shape of the form's parameters: an enum name where
    /// it wants one, a number literal where it wants one. Value parameters accept any shape.
    static bool LooksLike(CallForm form, FunctionParseContext ctx)
    {
        for (int i = 0; i < ctx.ArgCount; i++)
        {
            var value = ctx.GetArgumentToken(i)?.Value;
            var ok = form.ParamAt(i)!.Kind switch
            {
                FnArgKind.EnumType => value?.TypeId is { } t && ctx.Visitor.Database.GetEnumDefinition(t.Text, out _),
                FnArgKind.Table => value?.TypeId is { } t && ctx.Visitor.Database.GetTableDefinition(t.Text, out _),
                FnArgKind.IntLiteral => value?.Number is { Kind: NumberKind.Int },
                FnArgKind.Text => value?.StringLit != null,
                _ => true,
            };
            if (!ok)
                return false;
        }

        return true;
    }

    static bool BindArgument(string name, CallForm form, FnParam param, int i, FunctionParseContext ctx, BoundCall bound)
    {
        var visitor = ctx.Visitor;
        var node = ctx.GetArgumentToken(i);
        var span = node?.Span ?? ctx.CallContext.Span;
        switch (param.Kind)
        {
            case FnArgKind.Collection:
            {
                if (!ctx.ParseCollectionPath(i, out var full, out var owner, out var coll))
                    return false;
                var element = visitor.Database.GetEntityType(coll.TypeId).Properties[(int) coll.Id].Type;
                bound.Set(i, full, element, (full, owner, coll));
                return true;
            }

            case FnArgKind.EnumType:
            {
                var value = ctx.ParseArgument(i, out var type);
                if (value is not Literal { Value.Type.BaseType: PropertyValue.ValueBaseType.EnumType } literal)
                {
                    visitor.AddError(StoryParser.ErrorCode.InvalidArgument, span, $"{name}(): {param.Name} must name an enum");
                    return false;
                }

                bound.Set(i, value, type, new EnumDefinitionId((ushort) literal.Value.IntValue));
                return true;
            }

            case FnArgKind.Table:
            {
                // A table is not a value: look its name up rather than parsing it as one.
                var text = node == null ? "" : ctx.GetText(node.Span).Trim();
                if (!visitor.Database.GetTableDefinition(text, out var table))
                {
                    visitor.AddError(StoryParser.ErrorCode.UnknownTable, span, text);
                    return false;
                }

                visitor.Linker?.LinkTable(new FileRange(node!.Span), table);
                bound.Set(i, null, table.ValueType, table);
                return true;
            }

            case FnArgKind.Text:
            {
                if (node?.Value?.StringLit == null)
                {
                    visitor.AddError(StoryParser.ErrorCode.InvalidArgument, span, $"{name}(): {param.Name} must be a string literal");
                    return false;
                }

                bound.Set(i, ctx.ParseArgument(i, out var type), type);
                return true;
            }

            case FnArgKind.IntLiteral:
            {
                var value = ctx.ParseArgument(i, out var type);
                if (value is not Literal { Value.Type.BaseType: PropertyValue.ValueBaseType.Number } literal
                    || node?.Value?.Number is not { Kind: NumberKind.Int })
                {
                    visitor.AddError(StoryParser.ErrorCode.InvalidArgument, span,
                        $"{name}(): {param.Name} must be a whole-number literal");
                    return false;
                }

                var n = literal.Value.IntValue;
                if ((param.Min is { } min && n < min) || (param.Max is { } max && n > max))
                {
                    visitor.AddError(StoryParser.ErrorCode.InvalidArgument, span,
                        $"{name}(): {param.Name} must be from {param.Min} to {param.Max}");
                    return false;
                }

                bound.Set(i, value, type, n);
                return true;
            }

            default:
            {
                var value = ctx.ParseArgument(i, out var type);
                if (value == null)
                    return false;
                bound.Set(i, value, type);
                if (!Fits(param, type, bound, visitor.Database, out var want))
                    visitor.AddError(StoryParser.ErrorCode.InvalidArgument, span,
                        $"{name}(): {param.Name} must be {want}, got {visitor.Database.Printer.Print(type)}");
                return true;
            }
        }
    }

    /// Whether a value of <paramref name="type"/> fits the parameter. An unknown type (an expression the
    /// visitor could not type) is let through: it has already been reported, or cannot be judged here.
    static bool Fits(FnParam param, PropertyValue.ValueType type, BoundCall bound, Database db, out string want)
    {
        want = "";
        if (type.BaseType == PropertyValue.ValueBaseType.None)
            return true;

        switch (param.Constraint)
        {
            case ValueConstraint.Number:
                want = "a number";
                return type.BaseType is PropertyValue.ValueBaseType.Number or PropertyValue.ValueBaseType.Float
                    or PropertyValue.ValueBaseType.Percentage;
            case ValueConstraint.Bool:
                want = "a bool";
                return type.BaseType == PropertyValue.ValueBaseType.Bool;
            case ValueConstraint.Condition:
                want = "a bool or an entity";
                return type.BaseType == PropertyValue.ValueBaseType.Bool || type.IsRefType;
            case ValueConstraint.Entity:
                want = "an entity";
                return type.IsRefType;
            case ValueConstraint.String:
                want = "a string";
                return type.BaseType == PropertyValue.ValueBaseType.String;
            case ValueConstraint.ElementOf:
            {
                var element = bound.Type(param.Of);
                want = $"a {db.Printer.Print(element)}";
                return element.BaseType == PropertyValue.ValueBaseType.None || AstVisitor.Accepts(element, type);
            }
            default:
                return true;
        }
    }
}
