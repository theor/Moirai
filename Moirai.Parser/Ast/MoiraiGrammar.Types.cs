using Superpower.Model;

namespace Moirai.Parser.Ast;

public static partial class MoiraiGrammar
{
    // type_definition: (ENTITY | SINGLETON) TYPE_ID SCOPE_OPEN LINE_BREAK* (prop_definition|function_definition)* SCOPE_CLOSE LINE_BREAK+ ;
    static TokenListParserResult<MoiraiTokenKind, TypeDefinitionNode> TypeDefinitionRule(
        TokenList<MoiraiTokenKind> input)
    {
        var kw = input.ConsumeToken();
        if (!kw.HasValue || kw.Value.Kind is not (MoiraiTokenKind.Entity or MoiraiTokenKind.Singleton))
            return TokenListParserResult.Empty<MoiraiTokenKind, TypeDefinitionNode>(input,
                "'entity' or 'singleton'");
        bool isSingleton = kw.Value.Kind == MoiraiTokenKind.Singleton;

        var nameTok = kw.Remainder.ConsumeToken();
        Ident? typeName;
        bool lowercase = false;
        TokenList<MoiraiTokenKind> afterName;
        if (nameTok.HasValue && nameTok.Value.Kind == MoiraiTokenKind.TypeId)
        {
            typeName = IdentOf(nameTok.Value);
            afterName = nameTok.Remainder;
        }
        else if (nameTok.HasValue && nameTok.Value.Kind == MoiraiTokenKind.Id)
        {
            // Recovery fallback preserving StoryParser.ErrorCode.TypenameMustStartWithUpperCase: a
            // lowercase name here is a real (if malformed) type name, not "no name at all" — the
            // semantic-analysis layer (Phase 3) is what turns IsLowercaseName into that diagnostic.
            typeName = IdentOf(nameTok.Value);
            lowercase = true;
            afterName = nameTok.Remainder;
        }
        else
        {
            return TokenListParserResult.Empty<MoiraiTokenKind, TypeDefinitionNode>(kw.Remainder, "a type name");
        }

        var open = afterName.ConsumeToken();
        if (!open.HasValue || open.Value.Kind != MoiraiTokenKind.ScopeOpen)
            return TokenListParserResult.Empty<MoiraiTokenKind, TypeDefinitionNode>(afterName, "'{'");
        var remainder = SkipLineBreaksStar(open.Remainder);

        var props = new List<PropDefinitionNode>();
        var funcs = new List<FunctionDefinitionNode>();
        while (true)
        {
            var lead = PeekKind(remainder);
            if (lead == MoiraiTokenKind.Prop)
            {
                var p = PropDefinitionRule(remainder);
                if (!p.HasValue)
                    return TokenListParserResult
                        .CastEmpty<MoiraiTokenKind, PropDefinitionNode, TypeDefinitionNode>(p);
                props.Add(p.Value);
                remainder = p.Remainder;
            }
            else if (lead == MoiraiTokenKind.Function)
            {
                var f = FunctionDefinitionRule(remainder);
                if (!f.HasValue)
                    return TokenListParserResult
                        .CastEmpty<MoiraiTokenKind, FunctionDefinitionNode, TypeDefinitionNode>(f);
                funcs.Add(f.Value);
                remainder = f.Remainder;
            }
            else break;
        }

        var close = remainder.ConsumeToken();
        if (!close.HasValue || close.Value.Kind != MoiraiTokenKind.ScopeClose)
            return TokenListParserResult.Empty<MoiraiTokenKind, TypeDefinitionNode>(remainder, "'}'");
        var lb = SkipLineBreaksPlus(close.Remainder);
        if (!lb.HasValue)
            return TokenListParserResult.CastEmpty<MoiraiTokenKind, Unit, TypeDefinitionNode>(lb);

        var span = Combine(kw.Value.Span, close.Value.Span);
        return TokenListParserResult.Value(
            new TypeDefinitionNode(isSingleton, typeName, lowercase, props.ToArray(), funcs.ToArray(), span),
            input, lb.Remainder);
    }

    // prop_definition: PROP property_id COLON type LINE_BREAK+ ;
    static TokenListParserResult<MoiraiTokenKind, PropDefinitionNode> PropDefinitionRule(
        TokenList<MoiraiTokenKind> input)
    {
        var kw = input.ConsumeToken();
        if (!kw.HasValue || kw.Value.Kind != MoiraiTokenKind.Prop)
            return TokenListParserResult.Empty<MoiraiTokenKind, PropDefinitionNode>(input, "'prop'");
        var propId = PropertyId(kw.Remainder);
        if (!propId.HasValue)
            return TokenListParserResult.CastEmpty<MoiraiTokenKind, Ident, PropDefinitionNode>(propId);
        var colon = propId.Remainder.ConsumeToken();
        if (!colon.HasValue || colon.Value.Kind != MoiraiTokenKind.Colon)
            return TokenListParserResult.Empty<MoiraiTokenKind, PropDefinitionNode>(propId.Remainder, "':'");
        var type = TypeRule(colon.Remainder);
        if (!type.HasValue)
            return TokenListParserResult.CastEmpty<MoiraiTokenKind, TypeNode, PropDefinitionNode>(type);
        var lb = SkipLineBreaksPlus(type.Remainder);
        if (!lb.HasValue)
            return TokenListParserResult.CastEmpty<MoiraiTokenKind, Unit, PropDefinitionNode>(lb);
        var span = Combine(kw.Value.Span, type.Value.Span);
        return TokenListParserResult.Value(new PropDefinitionNode(propId.Value, type.Value, span), input,
            lb.Remainder);
    }

    // enum_definition: ENUM TYPE_ID SCOPE_OPEN LINE_BREAK* TYPE_ID (COMMA LINE_BREAK* TYPE_ID)* COMMA? LINE_BREAK* SCOPE_CLOSE LINE_BREAK+ ;
    static TokenListParserResult<MoiraiTokenKind, EnumDefinitionNode> EnumDefinitionRule(
        TokenList<MoiraiTokenKind> input)
    {
        var kw = input.ConsumeToken();
        if (!kw.HasValue || kw.Value.Kind != MoiraiTokenKind.Enum)
            return TokenListParserResult.Empty<MoiraiTokenKind, EnumDefinitionNode>(input, "'enum'");
        var name = TypeId(kw.Remainder);
        if (!name.HasValue)
            return TokenListParserResult.CastEmpty<MoiraiTokenKind, Ident, EnumDefinitionNode>(name);
        var open = name.Remainder.ConsumeToken();
        if (!open.HasValue || open.Value.Kind != MoiraiTokenKind.ScopeOpen)
            return TokenListParserResult.Empty<MoiraiTokenKind, EnumDefinitionNode>(name.Remainder, "'{'");
        var remainder = SkipLineBreaksStar(open.Remainder);

        var members = new List<Ident>();
        var first = TypeId(remainder);
        if (!first.HasValue)
            return TokenListParserResult.CastEmpty<MoiraiTokenKind, Ident, EnumDefinitionNode>(first);
        members.Add(first.Value);
        remainder = first.Remainder;
        while (PeekKind(remainder) == MoiraiTokenKind.Comma)
        {
            var comma = remainder.ConsumeToken();
            var afterComma = SkipLineBreaksStar(comma.Remainder);
            if (PeekKind(afterComma) != MoiraiTokenKind.TypeId)
            {
                remainder = afterComma; // trailing comma before '}' (COMMA? in the grammar)
                break;
            }

            var m = TypeId(afterComma);
            if (!m.HasValue)
                return TokenListParserResult.CastEmpty<MoiraiTokenKind, Ident, EnumDefinitionNode>(m);
            members.Add(m.Value);
            remainder = m.Remainder;
        }

        remainder = SkipLineBreaksStar(remainder);
        var close = remainder.ConsumeToken();
        if (!close.HasValue || close.Value.Kind != MoiraiTokenKind.ScopeClose)
            return TokenListParserResult.Empty<MoiraiTokenKind, EnumDefinitionNode>(remainder, "'}'");
        var lb = SkipLineBreaksPlus(close.Remainder);
        if (!lb.HasValue)
            return TokenListParserResult.CastEmpty<MoiraiTokenKind, Unit, EnumDefinitionNode>(lb);

        var span = Combine(kw.Value.Span, close.Value.Span);
        return TokenListParserResult.Value(new EnumDefinitionNode(name.Value, members.ToArray(), span), input,
            lb.Remainder);
    }

    // table_entry: (NUMBER ARROW)? value ;
    static TokenListParserResult<MoiraiTokenKind, TableEntryNode> TableEntryRule(TokenList<MoiraiTokenKind> input)
    {
        int? weight = null;
        TextSpan? startSpan = null;
        var remainder = input;
        if (PeekKind(input) == MoiraiTokenKind.Number && PeekKindAt(input, 1) == MoiraiTokenKind.Arrow)
        {
            var num = input.ConsumeToken();
            weight = int.Parse(num.Value.ToStringValue());
            startSpan = num.Value.Span;
            var arrow = num.Remainder.ConsumeToken();
            remainder = arrow.Remainder;
        }

        var value = Value(remainder);
        if (!value.HasValue)
            return TokenListParserResult.CastEmpty<MoiraiTokenKind, ValueNode, TableEntryNode>(value);
        var span = startSpan.HasValue ? Combine(startSpan.Value, value.Value.Span) : value.Value.Span;
        return TokenListParserResult.Value(new TableEntryNode(weight, value.Value, span), input, value.Remainder);
    }

    // table_definition: TABLE TYPE_ID SCOPE_OPEN LINE_BREAK* table_entry (COMMA LINE_BREAK* table_entry)* COMMA? LINE_BREAK* SCOPE_CLOSE LINE_BREAK+ ;
    static TokenListParserResult<MoiraiTokenKind, TableDefinitionNode> TableDefinitionRule(
        TokenList<MoiraiTokenKind> input)
    {
        var kw = input.ConsumeToken();
        if (!kw.HasValue || kw.Value.Kind != MoiraiTokenKind.Table)
            return TokenListParserResult.Empty<MoiraiTokenKind, TableDefinitionNode>(input, "'table'");
        var name = TypeId(kw.Remainder);
        if (!name.HasValue)
            return TokenListParserResult.CastEmpty<MoiraiTokenKind, Ident, TableDefinitionNode>(name);
        var open = name.Remainder.ConsumeToken();
        if (!open.HasValue || open.Value.Kind != MoiraiTokenKind.ScopeOpen)
            return TokenListParserResult.Empty<MoiraiTokenKind, TableDefinitionNode>(name.Remainder, "'{'");
        var remainder = SkipLineBreaksStar(open.Remainder);

        var entries = new List<TableEntryNode>();
        var first = TableEntryRule(remainder);
        if (!first.HasValue)
            return TokenListParserResult.CastEmpty<MoiraiTokenKind, TableEntryNode, TableDefinitionNode>(first);
        entries.Add(first.Value);
        remainder = first.Remainder;
        while (PeekKind(remainder) == MoiraiTokenKind.Comma)
        {
            var comma = remainder.ConsumeToken();
            var afterComma = SkipLineBreaksStar(comma.Remainder);
            if (PeekKind(afterComma) == MoiraiTokenKind.ScopeClose)
            {
                remainder = afterComma; // trailing comma before '}'
                break;
            }

            var e = TableEntryRule(afterComma);
            if (!e.HasValue)
                return TokenListParserResult.CastEmpty<MoiraiTokenKind, TableEntryNode, TableDefinitionNode>(e);
            entries.Add(e.Value);
            remainder = e.Remainder;
        }

        remainder = SkipLineBreaksStar(remainder);
        var close = remainder.ConsumeToken();
        if (!close.HasValue || close.Value.Kind != MoiraiTokenKind.ScopeClose)
            return TokenListParserResult.Empty<MoiraiTokenKind, TableDefinitionNode>(remainder, "'}'");
        var lb = SkipLineBreaksPlus(close.Remainder);
        if (!lb.HasValue)
            return TokenListParserResult.CastEmpty<MoiraiTokenKind, Unit, TableDefinitionNode>(lb);

        var span = Combine(kw.Value.Span, close.Value.Span);
        return TokenListParserResult.Value(new TableDefinitionNode(name.Value, entries.ToArray(), span), input,
            lb.Remainder);
    }

    // param: VAR_ID COLON type ;
    static TokenListParserResult<MoiraiTokenKind, ParamNode> ParamRule(TokenList<MoiraiTokenKind> input)
    {
        var varId = input.ConsumeToken();
        if (!varId.HasValue || varId.Value.Kind != MoiraiTokenKind.VarId)
            return TokenListParserResult.Empty<MoiraiTokenKind, ParamNode>(input, "a parameter name");
        var colon = varId.Remainder.ConsumeToken();
        if (!colon.HasValue || colon.Value.Kind != MoiraiTokenKind.Colon)
            return TokenListParserResult.Empty<MoiraiTokenKind, ParamNode>(varId.Remainder, "':'");
        var type = TypeRule(colon.Remainder);
        if (!type.HasValue)
            return TokenListParserResult.CastEmpty<MoiraiTokenKind, TypeNode, ParamNode>(type);
        var span = Combine(varId.Value.Span, type.Value.Span);
        return TokenListParserResult.Value(new ParamNode(IdentOf(varId.Value), type.Value, span), input,
            type.Remainder);
    }

    // function_definition: FUNCTION fun_id PAREN_OPEN (param (COMMA param)*)? PAREN_CLOSE (COLON type)? scope ;
    static TokenListParserResult<MoiraiTokenKind, FunctionDefinitionNode> FunctionDefinitionRule(
        TokenList<MoiraiTokenKind> input)
    {
        var kw = input.ConsumeToken();
        if (!kw.HasValue || kw.Value.Kind != MoiraiTokenKind.Function)
            return TokenListParserResult.Empty<MoiraiTokenKind, FunctionDefinitionNode>(input, "'function'");
        var name = FunId(kw.Remainder);
        if (!name.HasValue)
            return TokenListParserResult.CastEmpty<MoiraiTokenKind, Ident, FunctionDefinitionNode>(name);
        var open = name.Remainder.ConsumeToken();
        if (!open.HasValue || open.Value.Kind != MoiraiTokenKind.ParenOpen)
            return TokenListParserResult.Empty<MoiraiTokenKind, FunctionDefinitionNode>(name.Remainder, "'('");

        var remainder = open.Remainder;
        var parameters = new List<ParamNode>();
        if (PeekKind(remainder) != MoiraiTokenKind.ParenClose)
        {
            var p = ParamRule(remainder);
            if (!p.HasValue)
                return TokenListParserResult.CastEmpty<MoiraiTokenKind, ParamNode, FunctionDefinitionNode>(p);
            parameters.Add(p.Value);
            remainder = p.Remainder;
            while (PeekKind(remainder) == MoiraiTokenKind.Comma)
            {
                var comma = remainder.ConsumeToken();
                var p2 = ParamRule(comma.Remainder);
                if (!p2.HasValue)
                    return TokenListParserResult.CastEmpty<MoiraiTokenKind, ParamNode, FunctionDefinitionNode>(p2);
                parameters.Add(p2.Value);
                remainder = p2.Remainder;
            }
        }

        var close = remainder.ConsumeToken();
        if (!close.HasValue || close.Value.Kind != MoiraiTokenKind.ParenClose)
            return TokenListParserResult.Empty<MoiraiTokenKind, FunctionDefinitionNode>(remainder, "')'");
        remainder = close.Remainder;

        TypeNode? returnType = null;
        if (PeekKind(remainder) == MoiraiTokenKind.Colon)
        {
            var colon = remainder.ConsumeToken();
            var rt = TypeRule(colon.Remainder);
            if (!rt.HasValue)
                return TokenListParserResult.CastEmpty<MoiraiTokenKind, TypeNode, FunctionDefinitionNode>(rt);
            returnType = rt.Value;
            remainder = rt.Remainder;
        }

        var scope = Scope(remainder);
        if (!scope.HasValue)
            return TokenListParserResult.CastEmpty<MoiraiTokenKind, ScopeNode, FunctionDefinitionNode>(scope);
        var span = Combine(kw.Value.Span, scope.Value.Span);
        return TokenListParserResult.Value(
            new FunctionDefinitionNode(name.Value, parameters.ToArray(), returnType, scope.Value, span), input,
            scope.Remainder);
    }
}
