using System.Reflection;
using Antlr4.Runtime;
using Antlr4.Runtime.Tree;
using Moirai.Parser;

// public class InstructionFunctionDescriptor : IFunctionDescriptor
// {
//     public string FuncName { get; }
//     private readonly Func<StoryParser.AstVisitor,CallContext, IInstructionCall> _parse;
//
//     public InstructionFunctionDescriptor(string funcName, Func<StoryParser.AstVisitor, CallContext, IInstructionCall> parse)
//     {
//         FuncName = funcName;
//         _parse = parse;
//     }
//
//     public IInstruction ParseInstruction(StoryParser.AstVisitor parser, CallContext args)
//     {
//         var c =  _parse(parser, args);
//         c.FunctionDescriptor = this;
//         return c;
//     }
//
//     public string Print(StoryPrinter printer, IValueCall call)
//     {
//         return $"{FuncName} {string.Join(", ", call.GetArgs().Select(a => printer.Print(a)))}";
//     }
// }
public class FunctionDescriptor : IFunctionDescriptor
{
    public record ParseContext(StoryParser.AstVisitor Visitor, ParserRuleContext CallContext)
    {
        public int ParseVariable(out EntityTypeId entityTypeId, out PropertyValue.ValueType type)
        {
            EntityType varType = ParseEntityType();

            int varIndex;
            if (CallContext is MoiraiParser.CallContext c)
            {
                 Visitor.DeclareVar(c.VAR_ID().GetText(), varType.RefType, c.VAR_ID().Symbol, out varIndex);
            } else
            {
                var rawCallContext = (MoiraiParser.Raw_callContext)CallContext;
                Visitor.DeclareVar(rawCallContext.VAR_ID().GetText(),varType.RefType, rawCallContext.VAR_ID().Symbol, out varIndex);
            }

            entityTypeId = varType.Id;
            type = varType.RefType;
            return varIndex;
        }

        public ParserRuleContext GetArgumentToken(int index)
        {
            if (CallContext is MoiraiParser.CallContext c)
            {
                return c.expr(index);
            } else if (CallContext is MoiraiParser.Raw_callContext r)
            {
                return r.value();
            }
            return CallContext;
        }
        public IValue ParseArgument(int index)
        {
            if (CallContext is MoiraiParser.CallContext c)
            {
                return Visitor.ParseExpr(c.expr(index))!;
            } else if (CallContext is MoiraiParser.Raw_callContext r)
            {
                if (index != 0)
                {
                    Visitor.AddError(StoryParser.ErrorCode.MissingArgument, CallContext,
                        "Expected more arguments, convert to () syntax");
                    return default!;
                }
                return Visitor.ParseValue(r.value(), out var _);
            }
            return default!;
        }

        public int ArgCount => CallContext is MoiraiParser.CallContext c ? c.expr().Length : (CallContext is MoiraiParser.Raw_callContext r && r.value() != null ? 1 : 0);

        public EntityType ParseEntityType()
        {
            ITerminalNode t;
            if (CallContext is MoiraiParser.CallContext c)
                t = c.TYPE_ID() ?? c.ID(1);
            else
                t = ((MoiraiParser.Raw_callContext)CallContext).TYPE_ID() ??
                    ((MoiraiParser.Raw_callContext)CallContext).ID(1);
            EntityTypeId type = Visitor.Database.GetEntityType(Visitor.ParseType(t))?.Id ?? default;
            if (type == EntityTypeId.Null)
            {
                Visitor.AddError(StoryParser.ErrorCode.UnknownEntityType, GetArgumentToken(0), $"'{type}'");
            }

            return this.Visitor.Database.Types[(int)type.Id];
        }

        public string GetText(RuleContext expr) => Visitor.Parser.TokenStream.GetText(expr);

        public IInstruction[]? ParseScope(bool autoCleanupVariableDeclarations)
        {
            if(CallContext is MoiraiParser.CallContext c)
                return Visitor.ParseScope(c.scope(), autoCleanupVariableDeclarations, out _);
            else
                return Visitor.ParseScope(((MoiraiParser.Raw_callContext)CallContext).scope(), autoCleanupVariableDeclarations, out _);
                
        }

        public void ExpectArgcount(int i, bool isMaxCount = false)
        {
            if (isMaxCount ? ArgCount > i : ArgCount != i)
                Visitor.AddError(StoryParser.ErrorCode.MissingArgument, CallContext,
                    $"Expected {i} arguments{(isMaxCount ? " max" : "")}, got {ArgCount}");
        }

        public IValue ParsePredicate(EntityTypeId entityTypeId)
        {
            if (ArgCount == 1)
                return ParseArgument(0);
            IValue[] preds = new IValue[ArgCount];
            for (int i = 0; i < ArgCount; i++)
            {
                preds[i] = ParseArgument(i);
            }
            return new And(preds);
        }
    }

    public delegate (IValueCall, PropertyValue.ValueType) ParseCallDelegate(ParseContext context);

    public string FuncName { get; }
    public bool ExpectVariable { get; }
    private readonly ParseCallDelegate _parse;

    public FunctionDescriptor(string funcName, bool expectVariable, ParseCallDelegate parse)
    {
        FuncName = funcName;
        ExpectVariable = expectVariable;
        _parse = parse;
    }

    public IValueCall Parse(StoryParser.AstVisitor parser, MoiraiParser.Raw_callContext call, out PropertyValue.ValueType returnType)
    {
        var c = _parse(new ParseContext(parser, call));
        returnType = c.Item2;
        if (c.Item1 != null)
            c.Item1.FunctionDescriptor = this;
        else
            throw new InvalidOperationException(parser.Parser.TokenStream.GetText(call));
        return c.Item1;
    }
    public IValueCall Parse(StoryParser.AstVisitor parser, MoiraiParser.CallContext call, out PropertyValue.ValueType returnType)
    {
        var c = _parse(new ParseContext(parser, call));
        returnType = c.Item2;
        if (c.Item1 != null)
            c.Item1.FunctionDescriptor = this;
        else
            throw new InvalidOperationException(parser.Parser.TokenStream.GetText(call));
        return c.Item1;
    }


    public string Print(StoryPrinter printer, IValueCall call)
    {
        // call (1,2)
        // call X $x: (12)
        // call X $x
        var args = call.GetArgs(printer);
        switch ((call.VariableIndex.HasValue, args.Count()))
        {
            case (false, 0):
                return ("not a call??");
            case(false, _):
                return $"{FuncName} ({string.Join(", ", call.GetArgs(printer).Select(a => printer.Print(a)))})";
            case (true, 0):
                return $"{FuncName} {printer.Print(call.VariableIndex.Value.Item2)} ${call.VariableIndex.Value.Item1}";
            case(true, _):
                return $"{FuncName} {printer.Print(call.VariableIndex.Value.Item2)} ${call.VariableIndex.Value.Item1}: ({string.Join(", ", call.GetArgs(printer).Select(a => printer.Print(a)))})";
                
        }
    }
}

public static class StoryParser
{
    private static readonly FunctionDescriptor[] Functions = new FunctionDescriptor[]
    {
        new("create", true, (FunctionDescriptor.ParseContext ctx) =>
        {
            return (new CreateEntity(ctx.ParseVariable(out var etid, out _), etid,ctx.ArgCount == 0 ? null : (InterpolatedString)ctx.ParseArgument(0)),
                    PropertyValue.TypeTypedRef(etid));
        }),
        new("each", true,
            ctx =>
            {
                using var vs = new AstVisitor.VariableDeclarationScope(ctx.Visitor, true);
                var variableIndex = ctx.ParseVariable(out var etid, out _);
                return (new AssignPick(etid, variableIndex, ctx.ParsePredicate(etid),
                    CallType.Each, ctx.ParseScope(false)),
                    PropertyValue.TypeTypedRef(etid));
            }),
        new("pick", true,
            ctx =>
            {
                var variableIndex = ctx.ParseVariable(out var etid, out _);
                return (new AssignPick(etid, variableIndex, ctx.ParsePredicate(etid),
                    CallType.Pick),
                PropertyValue.TypeTypedRef(etid));
            }),

        new("assert", false, ctx =>
            (new AssertInstr(ctx.ParseArgument(0)!, ctx.GetText(ctx.GetArgumentToken(0))), PropertyValue.ValueType.Null)!),
        new("assert_eq", false, ctx =>
            (new AssertInstr(
                ctx.ParseArgument(0)!,
                ctx.ParseArgument(1),
                $"{ctx.GetText(ctx.GetArgumentToken(0))} = {ctx.GetText(ctx.GetArgumentToken(1))}"), PropertyValue.ValueType.Null)),
        new("mark", false, ctx =>
        {
            ctx.ExpectArgcount(1);
            var e = ctx.ParseArgument(0);
            return (new Mark(e, ctx.Visitor.CurrentEventTrigger.Id), PropertyValue.ValueType.Null);
        }),
        new("since_last", false, ctx =>
        {
            ctx.ExpectArgcount(1);
            var e = ctx.ParseArgument(0);
            return (new SinceLast(e, ctx.Visitor.CurrentEventTrigger.Id), PropertyValue.TypeNumber);
        }),
        new("record", false, ctx =>
        {
            var interpolatedString = (InterpolatedString)ctx.ParseArgument(0);
            return (new Record(interpolatedString), PropertyValue.ValueType.Null);
        }),
        new("link", false, ctx =>
        {
            var linkValue = ctx.ParseArgument(0);
            var linkText = ctx.ParseArgument(1);
            return (new InterpolatedStringLink(linkValue, linkText), PropertyValue.TypeString);
        }),
        new("call", false, ctx =>
        {
            var arg = ctx.GetArgumentToken(0);
            string? eventName = arg is MoiraiParser.ExprContext e ? e.value()?.path()?.GetText() ?? e.value()?.@string()?.GetString()
                    : (arg is MoiraiParser.ValueContext v) ? v.path()?.GetText() ?? v.@string()?.GetText() : null;
            if (eventName == null)
            {
                ctx.Visitor.AddError(ErrorCode.MissingArgument, ctx.CallContext, "event name");
            }

            var eventIndex = ctx.Visitor.Database.Actions.FindIndex(r => r.Name == eventName);
            if (eventIndex == -1)
            {
                ctx.Visitor.AddError(ErrorCode.UnknownRule, arg, eventName);
            }

            int count = 1;
            if (ctx.ArgCount > 1)
            {
                var countValue = ctx.ParseArgument(1);
                if (countValue is Literal l && l is Literal
                    {
                        Value: { Type: { BaseType: PropertyValue.ValueBaseType.Number } }
                    })
                {
                    count = l.Value.IntValue;
                }
            }

            // TODO type calls ?
            return (new CallRule(eventIndex, count), PropertyValue.ValueType.Null);
        }),

        new("random", false, ctx =>
        {
            var argCount = ctx.ArgCount;
            if (argCount == 0)
            {
                ctx.Visitor.AddError(ErrorCode.MissingArgument, ctx.CallContext,
                    "'random' needs at least one argument");
                return (null!, PropertyValue.ValueType.Null);
            }

            var arg = ctx.ParseArgument(0);

            if (arg is Literal { Value.Type.BaseType: PropertyValue.ValueBaseType.EnumType } l)
            {
                ctx.ExpectArgcount(1);
                var edid = new EnumDefinitionId((ushort)l.Value.IntValue);
                return (new RandomEnum(edid), PropertyValue.TypeEnum(edid));
            }

            if (arg is Literal { Value.Type.BaseType: PropertyValue.ValueBaseType.Number })
            {
                ctx.ExpectArgcount(2, true);
                var min = argCount == 1 ? new Literal(0) : arg;
                var max = ctx.ParseArgument(argCount == 1 ? 0 : 1);
                return (new RandomRange(min, max), PropertyValue.TypeNumber);
            }

            ctx.Visitor.AddError(ErrorCode.MissingArgument, ctx.CallContext, ctx.GetText(ctx.CallContext));
            return (null!, PropertyValue.ValueType.Null);
        }),
        new("not", false,
            ctx => (new MathUnary(MathUnary.UnaryFunction.Not, ctx.ParseArgument(0)), PropertyValue.TypeBool)),
        new("floor", false,
            ctx => (new MathUnary(MathUnary.UnaryFunction.Floor, ctx.ParseArgument(0)), PropertyValue.TypeNumber)),
        new("round", false,
            ctx => (new MathUnary(MathUnary.UnaryFunction.Round, ctx.ParseArgument(0)), PropertyValue.TypeNumber)),
        new("ceiling", false,
            ctx => (new MathUnary(MathUnary.UnaryFunction.Ceiling, ctx.ParseArgument(0)), PropertyValue.TypeNumber)),
        new("clamp01", false,
            ctx => (new MathUnary(MathUnary.UnaryFunction.Clamp01, ctx.ParseArgument(0)), PropertyValue.TypeNumber)),
        new("debug", false, ctx=> (new DebugPrint(Enumerable.Repeat((object?)null, ctx.ArgCount).Select((_,i) => ctx.ParseArgument(i))), PropertyValue.ValueType.Null))
    };

    public interface IVisitor
    {
        List<Error> Errors { get; }
        MoiraiParser Parser { get; set; }
        (int offsetLine, int offsetColumn) offset { get; set; }
    }

    public enum ErrorCode
    {
        Lexer,
        Parser,
        UnknownCall,
        UnknownExpressionOperator,
        UnknownProperty,
        DuplicatePropertyDefinition,
        UnknownPropertyType,
        UnknownEnumValue,
        DuplicateVariableDefinition,
        MissingEachScope,
        UnknownEnum,
        TypeNameMustStartWithUpperCase,
        VariableNotDeclared,
        NullEffect,
        UnknownInstruction,
        MissingArgument,
        UnknownRule,
        UnknownEntityType,
        Exception,
        UnknownTag,
        DuplicateTagDefinition,
        WeightMatchTakesOnlyOneValue,
        MatchNullWeight,
        MatchAnyValueMustBeLast,
        MissingVariable,
        UnknownAttribute,
        MismatchedAssignmentTypes
    }

    public struct Error
    {
        public readonly ErrorCode Code;
        public int Line, Col;
        public int LineEnd, ColEnd;
        public string Message;

        public Error(ErrorCode code, int line, int col, string message)
        {
            Code = code;
            Line = line;
            Col = col;
            Message = message;
            LineEnd = line;
            ColEnd = col + 1;
        }

        public Error(ErrorCode code, ITerminalNode loc, string message, (int, int) offset)
        {
            Code = code;
            Line = loc.Symbol.Line + offset.Item1;
            Col = loc.Symbol.Column + offset.Item2;
            Message = message;
            LineEnd = loc.Symbol.Line;
            ColEnd = loc.Symbol.Column + loc.Symbol.Text.Length;
        }

        public Error(ErrorCode code, ParserRuleContext loc, string message, (int, int) offset)
        {
            Code = code;
            Line = loc.Start.Line + offset.Item1;
            Col = loc.Start.Column + offset.Item2;
            Message = message;
            LineEnd = loc.Stop.Line + offset.Item1;
            ColEnd = loc.Stop.Column + offset.Item2;
        }

        public override string ToString() => $"M{(int)Code}: {Code} {Line}:{Col}: {Message}";
    }

    class Listener : IAntlrErrorListener<int>, IAntlrErrorListener<IToken>
    {
        private readonly List<Error> _errors;
        private readonly (int offsetLine, int offsetColumn) _offset;

        public Listener(List<Error> errors, (int offsetLine, int offsetColumn)? offset)
        {
            _errors = errors;
            _offset = offset ?? (0, 0);
        }

        public void SyntaxError(TextWriter output, IRecognizer recognizer, int offendingSymbol, int line,
            int charPositionInLine,
            string msg,
            RecognitionException e)
        {
            _errors.Add(new Error(ErrorCode.Lexer, line + _offset.offsetLine, charPositionInLine + _offset.offsetColumn,
                "Lexer:" + msg));
        }

        public void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol, int line,
            int charPositionInLine,
            string msg,
            RecognitionException e)
        {
            _errors.Add(new Error(ErrorCode.Parser, line + _offset.offsetLine,
                charPositionInLine + _offset.offsetColumn, "Parser:" + msg));
        }
    }

    public static IValue? ParseExpr(AstVisitor visitor, string s, int offsetLine, int offsetColumn,
        out List<Error> errors)
    {
        var prevOffset = visitor.offset;
        SetupParser(s, out var parser, visitor, (offsetLine, offsetColumn));
        var r = parser.expr();
        var propertyPath = visitor.ParseExpr(r);
        errors = visitor.Errors;
        visitor.offset = prevOffset;
        return propertyPath;
    }

    public static Database Parse(string s, out List<Error> errors)
    {
        var db = new Database();
        var visitor = new AstVisitor(db);
        SetupParser(s, out var parser, visitor);
        var r = parser.r();
        r.Accept(visitor);
        errors = visitor.Errors;
        return db;
    }

    public static void SetupParser(string s, out MoiraiParser parser, IVisitor visitor,
        (int offsetLine, int offsetColumn)? offset = null, bool mergeChannels = false)
    {
        var fromString = new CodePointCharStream(s /*.TrimStart('\r', '\n', ' ')*/);
        var lexer = new moirai_lexer(fromString);
        var tokens = /*mergeChannels ? new BufferedTokenStream(lexer) :*/ new CommonTokenStream(lexer);
        parser = new MoiraiParser(tokens);
        visitor.Parser = parser;
        visitor.offset = offset ?? (0, 0);
        var listener = new Listener(visitor.Errors, offset);
        lexer.AddErrorListener(listener);
        parser.AddErrorListener(listener);
    }

    public class AstVisitor : MoiraiParserBaseVisitor<object?>, IVisitor
    {
        public record struct VariableDeclaration(string Name, PropertyValue.ValueType Type)
        {
            // private sealed class NameEqualityComparer : IEqualityComparer<VariableDeclaration>
            // {
            //     public bool Equals(VariableDeclaration x, VariableDeclaration y) => x.Name == y.Name;
            //
            //     public int GetHashCode(VariableDeclaration obj) => obj.Name.GetHashCode();
            // }
            //
            // public static IEqualityComparer<VariableDeclaration> NameComparer { get; } = new NameEqualityComparer();
        }
        public (int offsetLine, int offsetColumn) offset { get; set; }

        private readonly List<VariableDeclaration> _variables = new();
        public List<Error> Errors { get; } = new();

        public MoiraiParser Parser { get; set; }
        protected override object? DefaultResult => null;

        public readonly Database Database;

        // private int _implicitVariableIndex = -1;
        public AstVisitor(Database database)
        {
            Database = database;
        }

        public override object? VisitR(MoiraiParser.RContext context)
        {
            foreach (var enumDefinitionContext in context.enum_definition())
            {
                enumDefinitionContext.Accept(this);
            }
            List<(EntityType Id, MoiraiParser.Type_definitionContext attr)> typesContexts = new();
            List<(EntityType Id, MoiraiParser.AttributeContext attr)> deferredTypeAttributes = new();
      
            foreach (var typeDefinitionContext in context.type_definition())
            {
                if (typeDefinitionContext.TYPE_ID() == null)
                    return AddError(ErrorCode.TypeNameMustStartWithUpperCase,
                        typeDefinitionContext,
                        typeDefinitionContext.GetText());

                string? typeName = typeDefinitionContext.TYPE_ID().GetText();
                EntityType type = DeclareEntityType(typeName);
                
                typesContexts.Add((type, typeDefinitionContext));
                foreach(var attr in typeDefinitionContext.attribute())
                    deferredTypeAttributes.Add((type,  attr));
            }
            foreach (var (type, typeDefinitionContext) in typesContexts)
            {
                foreach (var propDefinitionContext in typeDefinitionContext.prop_definition())
                {
                    var propName = propDefinitionContext.ID(0).GetText();
                    if (type.GetPropertyId(propName).Id != 0)
                        return AddError(ErrorCode.DuplicatePropertyDefinition, typeDefinitionContext, propName);

                    PropertyValue.ValueType proptype = ParseType(propDefinitionContext.ID(1) ?? propDefinitionContext.TYPE_ID());
                    type.Properties.Add(new PropertyDefinition(propName, type.Id, (uint)type.Properties.Count, proptype));
                }
            }
            foreach (var (tid, attr) in deferredTypeAttributes)
            {
                var id = attr.ID();
                if (id.GetText() != "display")
                {
                    AddError(ErrorCode.UnknownAttribute, id, id?.GetText() ?? "??");
                    continue;
                }
                if (attr.expr().Length < 2)
                {
                    AddError(ErrorCode.MissingArgument, attr, "display expects two arguments, a string and and expression");
                    continue;
                }

                var refReferencedType = ParseType(attr.TYPE_ID());
                if(!refReferencedType.IsRefType)
                    AddError(ErrorCode.UnknownEntityType, attr, "expected an Entity type");

                using (new VariableDeclarationScope(this, true))
                {
                    DeclareVar("$self", tid.RefType, null, out var varIndex);
                    DeclareVar("$other", refReferencedType, null, out var otherVarIndex);
                    var expr = ParseExpr(attr.expr(1));
                    InterpolatedString itemDisplay = null;
                    if (attr.expr(2)?.value()?.@string() != null)
                        itemDisplay = ParseInterpolatedString(attr.expr(2).value().@string());
                    Display d = new Display(Database.GetEntityType(refReferencedType), varIndex, otherVarIndex, attr.expr(0).GetText(), expr, itemDisplay);
                    var t = Database.Types[(int)(tid.Id.Id)];

                    t.Attributes.Add(d);
                }
            }

            foreach (var child in context.children)
            {
                if (child is MoiraiParser.EventContext e)
                    e.Accept(this);
                else if (child is MoiraiParser.TriggerContext t)
                    t.Accept(this);
            }

            return null;
        }

        
        public override object? VisitType_definition(MoiraiParser.Type_definitionContext context)
        {
            throw new NotImplementedException();
        }

        public EntityType DeclareEntityType(string typeName)
        {
            var id = (uint)Database.Types.Count;
            var entityType = new EntityType(typeName, id);
            Database.Types.Add(entityType);
            return entityType;
        }

        public override object? VisitProp_definition(MoiraiParser.Prop_definitionContext context)
        {
         throw new System.NotImplementedException();
        }

        public PropertyValue.ValueType ParseType(ITerminalNode id)
        {
            switch (id.GetText())
            {
                case "bool": return PropertyValue.TypeBool;
                // case "ref": return PropertyValue.TypeRef;
                case "number": return PropertyValue.TypeNumber;
                case "float": return PropertyValue.TypeFloat;
                case "string": return PropertyValue.TypeString;
                case "percentage": return PropertyValue.TypePercent;
                default:
                    if (Database.GetEnumDefinition(id.GetText(), out EnumDefinition enumDefinition))
                        return PropertyValue.TypeEnum(enumDefinition.Index);
                    var entityType = Database.GetEntityType(id.GetText());
                    if (entityType.Id.IsValid)
                        return entityType.RefType;
                    AddError(ErrorCode.UnknownPropertyType, id, id.GetText());
                    return default;
            }
        }

        public override object? VisitEnum_definition(MoiraiParser.Enum_definitionContext context)
        {
            EnumDefinition en = new(new EnumDefinitionId((ushort)Database.Enums.Count), context.TYPE_ID(0).GetText(),
                context.TYPE_ID().Skip(1).Select(v => v.GetText()).ToList());
            Database.Enums.Add(en);
            return null;
        }

        public override object? VisitEvent(MoiraiParser.EventContext context)
        {
            string actionId = context.ID().GetText();
            // bool isStartAction = context.AT() != null;
            //Console.WriteLine("@ " + actionId);
            _variables.Clear();
            IFilter? f = null;
            if (context.filter() != null)
            {
                var p = context.filter();
                switch (p.ID(0).GetText())
                {
                    case "start":
                        f = new FilterAtStart();
                        break;
                    case "every":
                    {
                        var x = int.Parse(p.occurence.Text);
                        var y = int.Parse(p.years.Text);
                        f = new FilterExactlyXEveryYYears(x, y, Database.Actions.Count + 1);
                        break;
                    }
                    case "per":
                    {
                        var x = int.Parse(p.occurence.Text);
                        var y = int.Parse(p.years.Text);
                        f = new FilterProbabilityXPerYears(x, y);
                        break;
                    }
                }
            }


            var cats = ParseCategories(context.categories());

            CurrentEventTrigger = new EventTrigger(Database.Actions.Count + 1, actionId, false, f, cats);
            foreach (MoiraiParser.EffectContext effectContext in context.scope().effect())
            {
                // if (effectContext.comment() != null)
                //     continue;
                var effect = ParseEffect(effectContext, out _);
                if (effect == null)
                {
                    AddError(ErrorCode.NullEffect, effectContext, effectContext.GetText());
                    continue;
                }

                CurrentEventTrigger.Effects.Add(effect);
            }

            Database.Actions.Add(CurrentEventTrigger);
            CurrentEventTrigger = null;
            return null;
        }

        private CategoryId[] ParseCategories(MoiraiParser.CategoriesContext tagIds)
        {
            CategoryId[] tags = new CategoryId[tagIds.ID().Length];
            var nodes = tagIds;
            for (var index = 0; index < nodes.ID().Length; index++)
            {
                var cat = tagIds.ID(index);
                tags[index] = Database.GetCategoryId(cat.GetText());
            }

            return tags;
        }


        public EventTrigger? CurrentEventTrigger;
        public override object? VisitTrigger(MoiraiParser.TriggerContext context)
        {
            string actionId = context.ID().GetText();
            //Console.WriteLine("@ " + actionId);
            var categories = ParseCategories(context.categories());
            CurrentEventTrigger = new EventTrigger(Database.Triggers.Count + 1, actionId, true, null, categories);
            _variables.Clear();

            using var _ = new VariableDeclarationScope(this, true);
            if (context.scope().when_created() is { } createdContext)
            {
                EntityType type = Database.GetEntityType(createdContext.TYPE_ID().GetText());
                if (!type.Id.IsValid)
                    AddError(ErrorCode.UnknownPropertyType, createdContext, createdContext.TYPE_ID()?.GetText() ?? createdContext.GetText());
               
                DeclareVar("$new", type.RefType,null, out var _);
                CurrentEventTrigger.When = (EventTrigger.WhenType.Created, type.Id, ParsePredicate(createdContext.expr()));
            }
            else if (context.scope().when() is { } whenContext)
            {
                EntityType type = Database.GetEntityType(whenContext.TYPE_ID().GetText());
                if (!type.Id.IsValid)
                    AddError(ErrorCode.UnknownPropertyType, whenContext, whenContext.TYPE_ID().GetText());
                
                if (context.scope().when() != null)
                    DeclareVar("$old", type.RefType, null, out var _);
                DeclareVar("$new", type.RefType,null, out var _);
                CurrentEventTrigger.When = (EventTrigger.WhenType.Changed, type.Id, ParsePredicate(whenContext.expr()));
            }

            Database.Triggers.Add(CurrentEventTrigger);
            foreach (var effectContext in context.scope().effect())
            {
                // if (effectContext.comment() != null)
                // continue;
                var effect = ParseEffect(effectContext, out var _);
                if (effect != null)
                    CurrentEventTrigger.Effects.Add(effect);
            }

            CurrentEventTrigger = null;
            return null;
        }

        private IInstruction ParseEffect(MoiraiParser.EffectContext effectContext, out PropertyValue.ValueType type)
        {
            if (effectContext.expr() != null)
            {
                var value = ParseExpr(effectContext.expr(), out type);
                if (value != null)
                    return new CallInstruction(value);
            }

            type = PropertyValue.ValueType.Null;
            if (effectContext.var() != null)
                return ParseLocalVar(effectContext.var());
            if (effectContext.set() != null)
                return ParseSet(effectContext.set());


            AddError(ErrorCode.Exception, effectContext, "NULL");
            return new SetProperty(default, null, false);
        }

        private bool _parsingMatchCase = false;

        private IValue ParseMatch(MoiraiParser.MatchContext match, out PropertyValue.ValueType valueType)
        {
            bool weight = match.MATCH_WEIGHT() != null;
            var values = match.expr().Select(ParseExpr).ToArray();
            (int, IInstruction[])[] weights = default;
            (IValue?[], IInstruction[])[] cases = default;
            if (weight)
            {
                if (values.Length > 1)
                    AddError(ErrorCode.WeightMatchTakesOnlyOneValue, match.expr(1), values.Length.ToString());
                weights = new (int, IInstruction[])[match.match_case().Length];
            }
            else
            {
                cases = new (IValue?[], IInstruction[])[match.match_case().Length];
            }

            int accWeight = 0;
            valueType = PropertyValue.ValueType.Null;
            for (int i = 0; i < match.match_case().Length; i++)
            {
                var caseCtx = match.match_case(i);
                _parsingMatchCase = true;
                IValue[] caseValues;
                try
                {
                    // TODO type ?
                    caseValues = caseCtx.value().Select(x => ParseValue(x, out var _)).ToArray();
                }
                finally
                {
                    _parsingMatchCase = false;
                }

                using var _ = new VariableDeclarationScope(this, true);
                var instrs = caseCtx.scope() == null
                    ? new IInstruction[] { ParseEffect(caseCtx.effect(), out valueType) }
                    : ParseScope(caseCtx.scope(), false, out valueType);
                if (weight)
                {
                    int w;
                    if (caseValues[0] is MatchAnyValue)
                    {
                        if (i != match.match_case().Length - 1)
                            AddError(ErrorCode.MatchAnyValueMustBeLast, caseCtx.value(0), caseCtx.value(0).GetText());
                        weights[i] = (-1, instrs);
                    }
                    else
                    {
                        w = (int)((Literal)caseValues[0]).Value.IntValue;
                        if (w <= 0)
                            AddError(ErrorCode.MatchNullWeight, caseCtx.value(0), caseCtx.value(0).GetText());
                        accWeight += w;
                        weights[i] = (accWeight, instrs);
                    }
                }
                else
                    cases[i] = (caseValues, instrs);
            }

            if (weight)
                return new MatchWeight(values[0], weights);

            return new Match(values, cases);
        }

        private If ParseIf(MoiraiParser.IfContext @if, out PropertyValue.ValueType valueType)
        {
            var elseType = PropertyValue.ValueType.Null;
            var iff = new If(ParseExpr(@if.cond), ParseScope(@if.then, true, out var ifType),
                @if.@else == null ? Array.Empty<IInstruction>() : ParseScope(@if.@else, true, out elseType));
            valueType = @if.@else == null ? ifType : Cast(ifType, elseType);
            return iff;
        }

        public IValue ParsePredicate(MoiraiParser.ExprContext[] exprContexts)
        {
            var exprs = exprContexts;
            if (exprContexts.Length == 0) return null;

            var predicate = exprs.Length == 1
                ? ParseExpr(exprs[0])!
                : new And(exprs.Select(x => ParseExpr(x)).Where(e => e != null).Cast<IValue>().ToList());
            return predicate;
        }

        public override object? VisitWhen(MoiraiParser.WhenContext context)
        {
            throw new System.NotImplementedException();
        }

        public override object? VisitSet(MoiraiParser.SetContext context)
        {
            throw new System.NotImplementedException();

            return null;
        }

        private SetProperty ParseLocalVar(MoiraiParser.VarContext context)
        {
            var name = context.VAR_ID();
            var expr = ParseExpr(context.expr(), out var type);
            DeclareVar(name.GetText(), type, name.Symbol, out var varIndex);
            return new SetProperty(new PropertyPath(varIndex), expr, true);
        }

        private SetProperty ParseSet(MoiraiParser.SetContext context)
        {
            var left = ParsePath(context.path(), out var assignedType);
            var right = ParseExpr(context.expr(), out var rightType); //, left.Property);
            if (assignedType != Cast(assignedType, rightType))
                AddError(ErrorCode.MismatchedAssignmentTypes, context, $"{assignedType} != {rightType}");
            return new SetProperty(left, right, false);
        }

        static PropertyValue.ValueType Cast(PropertyValue.ValueType to, PropertyValue.ValueType from)
        {
            if (to == from)
                return to;

            if (to == PropertyValue.TypeFloat)
            {
                if (from == PropertyValue.TypeNumber || from == PropertyValue.TypePercent)
                    return PropertyValue.TypeFloat;
            }
            if (to == PropertyValue.TypePercent)
            {
                if (from == PropertyValue.TypeNumber || from == PropertyValue.TypeFloat)
                    return PropertyValue.TypePercent;
            }

            if (to.BaseType == PropertyValue.ValueBaseType.Enum && from == PropertyValue.TypeNumber)
                return to;
            if (from.BaseType == PropertyValue.ValueBaseType.Enum && to == PropertyValue.TypeNumber)
                return to;
            if (to.IsRefType && from.IsRefType && from.Index == 0) // null or (shaky) untyped ref
                return to;

            return from;
        }

        public IValue ParseValue(MoiraiParser.ValueContext value, out PropertyValue.ValueType type)
        {
            if (_parsingMatchCase && value.path()?.GetText() == "_")
            {
                // TODO ?
                type = default;
                return MatchAnyValue.Instance;
            }
            if (value.TYPE_ID() != null)
            {
                var etype = Database.GetEntityType(value.TYPE_ID().GetText());
                if (!etype.Id.IsValid)
                {
                    if (Database.GetEnumDefinition(value.TYPE_ID().GetText(), out var ed))
                    {
                        // TODO really ?
                        type = ed.ValueType;
                        return new Literal(ed.EnumType);
                    }
                    AddError(ErrorCode.UnknownPropertyType, value, value.TYPE_ID().GetText());
                }

                type = etype.RefType;
                return new Literal(etype.Id);
            }

            if (value.call() != null)
            {
                return ParseCall(value.call(), out type);
            }

            if (value.raw_call() != null)
            {
                return ParseRawCall(value.raw_call(), out type);
            }

            if (value.path() != null)
            {
                PropertyPath path = ParsePath(value.path(), out type);
                return path;
            }

            if (value.@string() != null)
            {
                type = PropertyValue.TypeString;
                return ParseInterpolatedString(value.@string());
            }

            if (value.NULL() != null)
            {
                type = PropertyValue.TypeRef;
                return new Literal(EntityId.Null);
            }
            if (value.number() is { } number)
            {
                
                if (number.NUMBER_FLOAT() != null)
                {
                    type = PropertyValue.TypeFloat;
                    return new Literal(float.Parse(number.NUMBER_FLOAT().GetText()));
                }
                if (number.PERCENT() != null)
                {
                    type = PropertyValue.TypePercent;
                    return new Literal(PropertyValue.Percent(int.Parse(number.PERCENT().GetText()
                        .Substring(0, number.PERCENT().GetText().Length - 1))));}
                type = PropertyValue.TypeNumber;
                return new Literal(int.Parse(number.GetText()));
            }

            if (value.@bool() != null)
            {
                type = PropertyValue.TypeBool;
                return new Literal(value.@bool().TRUE() != null);
            }

            if (value.enum_value() != null)
            {
                var enumType = value.enum_value().TYPE_ID(0);
                if (!Database.GetEnumDefinition(enumType.GetText(), out var enumDef))
                {
                    type = default;
                    return (AddError(ErrorCode.UnknownEnum, enumType, enumType.GetText()) as IValue)!;
                }

                var enumValue = value.enum_value().TYPE_ID(1);
                if (!enumDef.GetValueFromName(enumValue.GetText(), out var val))
                {
                    type = default;
                    return (AddError(ErrorCode.UnknownEnumValue, enumValue,
                        enumValue.GetText() + " in enum " + enumDef.Name) as IValue)!;}
                type = enumDef.ValueType;
                return new Literal(val);
            }

            throw new ArgumentOutOfRangeException();
        }

        private int GetVariableIndexByName(string name)
        {
            return _variables.FindLastIndex(v => v.Name == name);
        }
        
        public bool DeclareVar(string variable,PropertyValue.ValueType type, IToken? contextStart, out int varIndex)
        {
            // if ((varIndex = GetVariableIndexByName(variable)) != -1)
            // {
            //     // AddError(ErrorCode.DuplicateVariableDefinition,  contextStart, " Duplicate variable " + variable);
            //     // varIndex = 0;
            //     return true;
            // }

            _variables.Add(new VariableDeclaration(variable, type));
            varIndex = _variables.Count - 1;
            return true;
        }

        public struct VariableDeclarationScope : IDisposable
        {
            private readonly AstVisitor _astVisitor;
            private readonly bool _autoCleanup;
            private readonly int _count;

            public VariableDeclarationScope(AstVisitor astVisitor, bool autoCleanup)
            {
                _astVisitor = astVisitor;
                _autoCleanup = autoCleanup;
                _count = astVisitor._variables.Count;
            }

            public void Dispose()
            {
                if (_autoCleanup)
                    Cleanup();
            }

            public void Cleanup()
            {
                _astVisitor._variables.RemoveRange(_count, _astVisitor._variables.Count - _count);
            }
        }

        private IValue ParseRawCall(MoiraiParser.Raw_callContext context, out PropertyValue.ValueType returnType)
        {
            var funcName = context.ID(0).GetText();
            var f = Functions.FirstOrDefault(f => f.FuncName == funcName);
            if (f != null)
            {
                return f.Parse(this, context, out returnType);
            }

            returnType = default!;
            return (AddError(ErrorCode.UnknownInstruction, context, funcName) as IValue)!;
        }
        private IValue ParseCall(MoiraiParser.CallContext context, out PropertyValue.ValueType returnType)
        {
            var funcName = context.ID(0).GetText();
            var f = Functions.FirstOrDefault(f => f.FuncName == funcName);
            if (f != null)
            {
                return f.Parse(this, context, out returnType);
            }

            returnType = default!;
            return (AddError(ErrorCode.UnknownInstruction, context, funcName) as IValue)!;
        }

        public IInstruction[] ParseScope(MoiraiParser.ScopeContext scopeContext, bool autoCleanupVariableDeclarations, out PropertyValue.ValueType type)
        {
            using var vs = new VariableDeclarationScope(this, autoCleanupVariableDeclarations);
            // TODO 
            type = PropertyValue.ValueType.Null;
            if (scopeContext == null)
                return Array.Empty<IInstruction>();
            var ttype = PropertyValue.ValueType.Null;
            var instructions = scopeContext.effect().Select(x =>
            {
                return ParseEffect(x, out ttype);
            }).Where(e => e != null).ToArray();
            type = ttype;
            return instructions;
        }

        public InterpolatedString ParseInterpolatedString(MoiraiParser.StringContext? stringContext)
        {
            if (stringContext == null || stringContext.stringContent().Length == 0)
                return new InterpolatedString("", Array.Empty<IValue>());

            List<IValue> paths = new();
            string result = "";
            foreach (var part in stringContext.stringContent())
            {
                if (part.TEXT() != null)
                    result += part.TEXT().GetText();
                else
                {
                    result += $"{{{paths.Count}}}";
                    paths.Add(ParseExpr(part.expr())!);
                }
            }
            
            // var str = stringContext.GetText().TrimQuotes();
            // List<IValue> paths = new();
            // string result = "";
            // int i = -1;
            // var prev = i + 1;
            //
            // while (i < str.Length)
            // {
            //     i = str.IndexOf('{', i + 1);
            //     if (i == -1)
            //         break;
            //
            //     int j = str.IndexOf('}', i + 1);
            //     if (j == -1)
            //         throw new System.NotImplementedException(
            //             $"Missing curly brace in string: {str}, opening brace at {i}");
            //
            //     var pathStr = str.Substring(i + 1, j - i - 1);
            //     var path = StoryParser.ParseExpr(this, pathStr,
            //         stringContext.Start.Line - 1 /* +1 somewhere in the pipeline */,
            //         stringContext.Start.Column + i + 1 + /*quote*/ 1, out _);
            //     paths.Add(path!);
            //     // Console.WriteLine($"'{pathStr}'");
            //     if (i > prev)
            //         result += str.Substring(prev, i - prev);
            //     result += $"{{{paths.Count - 1}}}";
            //     i = j;
            //     prev = i + 1;
            // }
            //
            // if (prev < str.Length)
            //     result += (str.Substring(prev));
            // // Console.WriteLine($"res:'{result}'");
            var interpolatedString = new InterpolatedString(result, paths.ToArray());
            return interpolatedString;
        }

        public IValue? ParseExpr(MoiraiParser.ExprContext context)
        {
            return ParseExpr(context, out var _);
        }
        public IValue? ParseExpr(MoiraiParser.ExprContext context, out PropertyValue.ValueType type)
        {
            
            if (context.@if() != null)
                return ParseIf(context.@if(), out type);
            if (context.match() != null)
                return ParseMatch(context.match(), out type);
            if (context.value() != null)
            {
                return ParseValue(context.value(), out type);
                // ComputedValue v = ParseValue(context.value(0), PropertyValue.TypeBool);
            }

            if (context.paren_expr != null)
                return ParseExpr(context.paren_expr, out type);

            string op = context.op.Text;
            // left, alive
            IValue leftPath = ParseExpr(context.left, out var leftType)!;

            // right, true or $x -  not alive or $x.alive
            IValue rightValue = ParseExpr(context.right, out var rightType)!;

            BinaryOperator.Operator pop;
            switch (op)
            {
                case "and":
                    pop = BinaryOperator.Operator.And;
                    type = PropertyValue.TypeBool;
                    break;
                case "or":
                    pop = BinaryOperator.Operator.Or;
                    type = PropertyValue.TypeBool;
                    break;
                case "??":
                    pop = BinaryOperator.Operator.Coalesce;
                    type = rightType;
                    break;
                case "=":
                    type = PropertyValue.TypeBool;
                    pop = BinaryOperator.Operator.Equals;

                    if (leftPath is PropertyPath { Nested: false } p &&
                        (p.Property == null || p.Property[0] == Database.PropType) &&
                        rightValue is Literal l &&
                        l.Value.Type == PropertyValue.TypeEntityType)
                    {
                        type = l.Value.Type;
                        return new IsOfType(leftPath, l.Value.TypeId);
                    }

                    break;
                case "!=":
                    type = PropertyValue.TypeBool;
                    pop = BinaryOperator.Operator.NotEquals;
                    break;
                case "+":
                    type = rightType;
                    pop = BinaryOperator.Operator.Add;
                    break;
                case "-":
                    type = rightType;
                    pop = BinaryOperator.Operator.Sub;
                    break;
                case "/":
                    type = rightType;
                    pop = BinaryOperator.Operator.Div;
                    break;
                case "*":
                    type = rightType;
                    pop = BinaryOperator.Operator.Mul;
                    break;
                case ">":
                    type = PropertyValue.TypeBool;
                    pop = BinaryOperator.Operator.Gt;
                    break;
                case "<":
                    type = PropertyValue.TypeBool;
                    pop = BinaryOperator.Operator.Lt;
                    break;
                case ">=":
                    type = PropertyValue.TypeBool;
                    pop = BinaryOperator.Operator.Ge;
                    break;
                case "<=":
                    type = PropertyValue.TypeBool;
                    pop = BinaryOperator.Operator.Le;
                    break;
                default:
                    type = default;
                    return (IValue?)AddError(ErrorCode.UnknownExpressionOperator, context, op);
            }

            return new BinaryOperator(pop, leftPath, rightValue);
        }

        public object AddError(ErrorCode code, ParserRuleContext loc, string msg)
        {
            Errors.Add(new Error(code, loc, Parser.TokenStream.GetText(loc) + ": " + msg, offset));
            // to avoid warnings in a case where the parsing is already compromised
            return null!;
        }

        public object? AddError(ErrorCode code, ITerminalNode loc, string msg)
        {
            Errors.Add(new Error(code, loc, msg, offset));
            return null;
        }

        public override object? VisitCall(MoiraiParser.CallContext context)
        {
            throw new System.NotImplementedException();
        }

        public override object? VisitExpr(MoiraiParser.ExprContext context)
        {
            throw new System.NotImplementedException();
        }

        public override object? VisitPath(MoiraiParser.PathContext context)
        {
            throw new System.NotImplementedException();
        }


        private void ParseProperty(ref PropertyPath path, MoiraiParser.PathContext context, int idIndex, EntityType owningType, out PropertyValue.ValueType type)
        {
            string propertyName = context.ID(idIndex).GetText();
            var propertyId = owningType.GetPropertyId(propertyName);
            if (!propertyId.IsValid)
            {
                type = default;
                AddError(ErrorCode.UnknownProperty, context.ID(0), propertyName);
                return;
            }

            type = owningType.GetPropertyType(propertyName);
            path.AddProperty(propertyId);
            if(context.ID(idIndex+1) != null)
                ParseProperty(ref path, context, idIndex+1, Database.GetEntityType(type), out type);
        }
        public PropertyPath ParsePath(MoiraiParser.PathContext context, out PropertyValue.ValueType type)
        {
            // if (context.ID().Length > 1)
                // throw new Exception("expected two parts, got " + (context.ID().Length + 1));

            ITerminalNode? singletonId = context.SINGLETON_ID();
            if (singletonId != null)
            {
                string typeName = singletonId.GetText().Substring(1);
                EntityType singletonType = Database.GetEntityType(typeName);
                if (!singletonType.Id.IsValid)
                {
                    AddError(ErrorCode.UnknownEntityType, singletonId, typeName);
                    type = default;

                    return default;
                }
                // TODO chained singleton #Time.x.y
                var path = new PropertyPath(PropertyId.Null);
                ParseProperty(ref path, context, 0, singletonType, out type);
                return path;
            }

            int variableIndex;
            ITerminalNode? varId = context.VAR_ID();
            if (varId != null)
            {
                if (!int.TryParse(varId.GetText().Substring(1), out variableIndex))
                {
                    variableIndex = GetVariableIndexByName(varId.GetText());
                    if (variableIndex == -1)
                    {
                        AddError(ErrorCode.VariableNotDeclared, context, varId.GetText());
                        type = default;
                        return new PropertyPath();
                    }
                }

                type = _variables[variableIndex].Type;
                if (context.ID().Length == 0)
                {
                    return new PropertyPath(variableIndex);
                }
            }
            else
                variableIndex = _variables.Count - 1;

            {
                EntityType? etype = Database.GetEntityType(_variables[variableIndex].Type);
                var path = new PropertyPath(variableIndex);
                ParseProperty(ref path, context, 0, etype, out type);
                return path;
            }
        }
    }
}

internal static class ParsingExtensions
{
    public static string TrimQuotes(this string s) => s.Trim('"', '\'');
    public static string GetString(this MoiraiParser.StringContext context) => context.GetText().TrimQuotes();
}
