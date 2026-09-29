using Moirai.Core;
using Moirai.Parser.Ast;

namespace Moirai.Parser;

/// A built-in call bound to one of its forms: which form matched, each argument's value in the shape its
/// kind gives, and for a binding form its variable, predicate and blocks, already parsed in the scopes the
/// form declares.
public sealed class BoundCall
{
    public FunctionParseContext Context { get; }
    public int FormIndex { get; }
    public FunctionForm Form { get; }
    readonly IValue?[] _values;
    readonly PropertyValue.ValueType[] _types;
    readonly object?[] _extras;
    readonly Dictionary<string, IInstruction[]> _blocks = new();
    readonly Dictionary<string, DebugScope?> _blockScopes = new();

    internal BoundCall(FunctionParseContext context, int formIndex, FunctionForm form, int count)
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

    // ---- A binding form's parts ----

    /// The type T of `name T $v`.
    public EntityTypeId EntityType { get; internal set; }
    /// The slot of `$v`.
    public int Variable { get; internal set; }
    /// The predicate of a Predicate or PredicateAndValue head: `and` over its clauses, `and` of none
    /// (always true) when there are none.
    public IValueSql Predicate { get; internal set; } = null!;
    /// How many clauses the predicate has; 0 when the head has none.
    public int PredicateCount { get; internal set; }

    /// A block's statements: the main block for null, `else` for "else". Null when the call has none.
    public IInstruction[]? Block(string? keyword = null) => _blocks.GetValueOrDefault(keyword ?? "");

    /// The debug scope captured inside a block that runs later, with `$self` in it.
    public DebugScope? BlockScope(string? keyword = null) => _blockScopes.GetValueOrDefault(keyword ?? "");

    /// The slot `$self` was declared in, for a block that sees only `$self`.
    public int SelfVariable { get; internal set; }

    /// What the descriptor's prepare step returned, before any block was parsed.
    public object? Prepared { get; internal set; }

    internal void SetBlock(string? keyword, IInstruction[] statements, DebugScope? scope)
    {
        _blocks[keyword ?? ""] = statements;
        _blockScopes[keyword ?? ""] = scope;
    }

    PropertyValue.ValueType? _returnType;

    /// The call's type: what the form declares, unless the handler refined it (an aggregate's type follows
    /// its value's).
    public PropertyValue.ValueType ReturnType
    {
        get => _returnType ?? Form.Returns switch
        {
            FnReturn.Number => PropertyValue.TypeNumber,
            FnReturn.Bool => PropertyValue.TypeBool,
            FnReturn.String => PropertyValue.TypeString,
            FnReturn.EnumOfArg => PropertyValue.TypeEnum(Enum(0)),
            FnReturn.TableEntryOfArg => Table(0).ValueType,
            FnReturn.Variable => PropertyValue.TypeTypedRef(EntityType),
            _ => PropertyValue.ValueType.Null,
        };
        set => _returnType = value;
    }
}

/// Checks a built-in call against its forms (<see cref="FunctionDescriptor.Forms"/>) before its handler
/// runs. A form matches when the call has its shape -- a variable (`T $v`) or not, and exactly the blocks
/// it declares -- and a fitting number of arguments; among several, the arguments' syntactic kind decides
/// (`random(Job)` vs `random(10)`). The binder then parses and checks each argument, declares a binding
/// form's variable for as long as the form says it lives, and parses each block in the scope the form
/// declares for it. Every mistake is reported; the call is still bound when an argument has only the wrong
/// type, so the rest of the rule keeps parsing, and not bound when an argument cannot be read at all.
public static class FunctionBinder
{
    public static bool TryBind(FunctionDescriptor descriptor, FunctionParseContext ctx, out BoundCall bound)
    {
        var name = descriptor.FuncName;
        var forms = descriptor.Forms;
        var count = ctx.ArgCount;
        bool binding = (ctx.CallContext.Call?.DeclType ?? ctx.CallContext.RawCall?.DeclType) != null;
        bool main = ctx.GetScopeContext() != null, orElse = ctx.GetElseContext() != null;

        bool Kind(FunctionForm f) => f is BindingForm == binding;
        bool Blocks(FunctionForm f) => Has(f, null) == main && Has(f, "else") == orElse;
        bool Fits(FunctionForm f) => f switch
        {
            CallForm c => count >= c.MinArgs && (c.MaxArgs is not { } max || count <= max),
            BindingForm { Head: BindingHead.Variable } => count == 0,
            BindingForm { Head: BindingHead.Name } => count == 1,
            BindingForm { Head: BindingHead.PredicateAndValue } => count >= 1,
            _ => true,
        };

        var candidates = forms.Select((f, i) => (Form: f, Index: i)).Where(c => Kind(c.Form) && Blocks(c.Form) && Fits(c.Form))
            .ToList();
        if (candidates.Count == 0)
        {
            Diagnose(descriptor, ctx, binding, main, orElse, count, Kind, Blocks);
            bound = null!;
            return false;
        }

        var chosen = candidates.FirstOrDefault(c => c.Form is not CallForm cf || LooksLike(cf, ctx));
        if (chosen.Form == null)
            chosen = candidates[0];

        bound = new BoundCall(ctx, chosen.Index, chosen.Form, count);
        return chosen.Form switch
        {
            CallForm c => BindCall(descriptor, c, ctx, bound),
            BindingForm b => BindBinding(descriptor, b, ctx, bound),
            _ => throw new ArgumentOutOfRangeException(),
        };
    }

    static bool Has(FunctionForm f, string? keyword) => f.Blocks.Any(b => b.Keyword == keyword);

    /// Says which part of the call no form accepts: the variable, a block, or the number of arguments.
    static void Diagnose(FunctionDescriptor d, FunctionParseContext ctx, bool binding, bool main, bool orElse, int count,
        Func<FunctionForm, bool> kind, Func<FunctionForm, bool> blocks)
    {
        var name = d.FuncName;
        var visitor = ctx.Visitor;
        var span = ctx.CallContext.Span;
        var signatures = string.Join(" or ", d.Forms.Select(f => f.Signature(name)));
        if (!d.Forms.Any(kind))
        {
            if (binding)
                visitor.AddError(StoryParser.ErrorCode.InvalidArgument, span, $"{name}() does not declare a variable: {signatures}");
            else
                visitor.AddError(StoryParser.ErrorCode.MissingVariable, span, $"{name} needs a variable: {signatures}");
            return;
        }

        var ofKind = d.Forms.Where(kind).ToList();
        if (!ofKind.Any(blocks))
        {
            if (orElse && !ofKind.Any(f => Has(f, "else")))
            {
                var failovers = StoryParser.Functions.Where(f => f.Forms.Any(x => Has(x, "else"))).Select(f => f.FuncName);
                visitor.AddError(StoryParser.ErrorCode.InvalidArgument, ctx.GetElseContext()!.Span,
                    $"only {string.Join(", ", failovers)} can have an else block; '{name}' cannot fail over to one");
            }
            else if (main && !ofKind.Any(f => Has(f, null)))
                visitor.AddError(StoryParser.ErrorCode.InvalidArgument, ctx.GetScopeContext()!.Span, $"{name}() takes no {{ }} block");
            else if (!main)
                visitor.AddError(StoryParser.ErrorCode.MissingEachScope, span, $"{name} needs a {{ }} block: {signatures}");
            else
                visitor.AddError(StoryParser.ErrorCode.InvalidArgument, span, $"no form of {name} takes these blocks: {signatures}");
            return;
        }

        visitor.AddError(StoryParser.ErrorCode.MissingArgument, span,
            $"{name}() takes {Arity(ofKind.Where(blocks).ToList())}, got {count}: {signatures}");
    }

    static string Arity(List<FunctionForm> forms)
    {
        var counts = forms.Select(f => f switch
        {
            CallForm c => c.MaxArgs is { } max ? c.MinArgs == max ? $"{max}" : $"{c.MinArgs} to {max}" : $"{c.MinArgs} or more",
            BindingForm { Head: BindingHead.Variable } => "0",
            BindingForm { Head: BindingHead.Name } => "1",
            BindingForm { Head: BindingHead.PredicateAndValue } => "1 or more",
            _ => "any number of",
        }).Distinct().ToList();
        return string.Join(" or ", counts) + " argument" + (counts is ["1"] ? "" : "s");
    }

    static bool BindCall(FunctionDescriptor d, CallForm form, FunctionParseContext ctx, BoundCall bound)
    {
        bool readable = true;
        for (int i = 0; i < bound.Count; i++)
            readable &= BindArgument(d.FuncName, form.ParamAt(i)!, i, ctx, bound);

        // Blocks are parsed even when an argument was unreadable, so their own mistakes are reported too.
        bound.Prepared = d.Prepare?.Invoke(ctx);
        foreach (var spec in form.Blocks)
            ParseBlock(spec, ctx, bound, pushScope: true);
        return readable;
    }

    /// `name T $v ...`. Whatever cannot see `$v` is parsed before it exists; then `$v` is declared for as
    /// long as the form says it lives, its head parsed, and the blocks that see it.
    static bool BindBinding(FunctionDescriptor d, BindingForm form, FunctionParseContext ctx, BoundCall bound)
    {
        bound.Prepared = d.Prepare?.Invoke(ctx);
        foreach (var spec in form.Blocks.Where(b => b.Sees == BlockSees.EnclosingNotVariable))
            ParseBlock(spec, ctx, bound, pushScope: true);

        var rest = form.Blocks.Where(b => b.Sees != BlockSees.EnclosingNotVariable).ToList();
        bool readable;
        switch (form.Lives)
        {
            case VariableLives.Rest:
                readable = DeclareAndParseHead(d, form, ctx, bound);
                foreach (var spec in rest)
                    ParseBlock(spec, ctx, bound, pushScope: true);
                break;

            case VariableLives.Block:
            {
                // The variable, the predicate and the block share one scope: the block's.
                var block = ctx.GetScopeContext()!;
                using var scope = new AstVisitor.VariableDeclarationScopeDisposable(ctx.Visitor, block.Span);
                readable = DeclareAndParseHead(d, form, ctx, bound);
                foreach (var spec in rest)
                    ParseBlock(spec, ctx, bound, pushScope: false);
                break;
            }

            default:
            {
                using var scope = new AstVisitor.VariableDeclarationScopeDisposable(ctx.Visitor, ctx.CallContext.Span);
                readable = DeclareAndParseHead(d, form, ctx, bound);
                foreach (var spec in rest)
                    ParseBlock(spec, ctx, bound, pushScope: true);
                break;
            }
        }

        return readable;
    }

    static bool DeclareAndParseHead(FunctionDescriptor d, BindingForm form, FunctionParseContext ctx, BoundCall bound)
    {
        bound.Variable = ctx.ParseVariable(out var etid, out _);
        bound.EntityType = etid;
        switch (form.Head)
        {
            case BindingHead.Name:
                return BindArgument(d.FuncName, P.Text("name"), 0, ctx, bound);

            case BindingHead.Predicate:
                bound.PredicateCount = bound.Count;
                bound.Predicate = ctx.ParsePredicateSql(etid, bound.Count);
                return true;

            case BindingHead.PredicateAndValue:
                bound.PredicateCount = bound.Count - 1;
                bound.Predicate = ctx.ParsePredicateSql(etid, bound.Count - 1);
                return BindArgument(d.FuncName, form.Value ?? P.Any("value"), bound.Count - 1, ctx, bound);

            default:
                return true;
        }
    }

    /// Parses one block in the scope its spec declares: a nested scope of the rule's for most; for a block
    /// that runs later, a nested scope holding `$self`, captured for the debugger while it is open.
    static void ParseBlock(BlockSpec spec, FunctionParseContext ctx, BoundCall bound, bool pushScope)
    {
        var visitor = ctx.Visitor;
        var node = spec.Keyword == "else" ? ctx.GetElseContext()! : ctx.GetScopeContext()!;
        using var scope = new AstVisitor.VariableDeclarationScopeDisposable(visitor, pushScope ? node.Span : null);
        if (spec.Sees == BlockSees.OnlySelf)
        {
            // $self takes the static type of argument 0, so `$self.prop` resolves.
            visitor.DeclareVar("$self", bound.Type(0), ctx.GetArgumentToken(0)!.Span, out var self);
            bound.SelfVariable = self;
        }

        var statements = visitor.ParseRawScope(node, out _);
        var debugScope = spec.Runs == BlockRuns.Later ? visitor.CaptureCurrentDebugScope() : null;
        bound.SetBlock(spec.Keyword, statements, debugScope);
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

    static bool BindArgument(string name, FnParam param, int i, FunctionParseContext ctx, BoundCall bound)
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
