using Superpower;
using Superpower.Model;

namespace Moirai.Parser.Ast;

public static partial class MoiraiGrammar
{
    // property_id: ID ;   fun_id: ID ;  (same token shape, distinct grammar names/roles)
    static TokenListParserResult<MoiraiTokenKind, Ident> PropertyId(TokenList<MoiraiTokenKind> input) =>
        Kind(MoiraiTokenKind.Id).Select(IdentOf).Named("property name")(input);

    static TokenListParserResult<MoiraiTokenKind, Ident> FunId(TokenList<MoiraiTokenKind> input) =>
        Kind(MoiraiTokenKind.Id).Select(IdentOf).Named("function/builtin name")(input);

    // type_id: TYPE_ID ;
    static TokenListParserResult<MoiraiTokenKind, Ident> TypeId(TokenList<MoiraiTokenKind> input) =>
        Kind(MoiraiTokenKind.TypeId).Select(IdentOf).Named("type name")(input);

    // type: TYPE_ID | ID | LBRACK (TYPE_ID | ID) RBRACK ;
    static TokenListParserResult<MoiraiTokenKind, TypeNode> TypeRule(TokenList<MoiraiTokenKind> input)
    {
        var bracket = input.ConsumeToken();
        if (bracket.HasValue && bracket.Value.Kind == MoiraiTokenKind.LBrack)
        {
            var nameTok = bracket.Remainder.ConsumeToken();
            if (!nameTok.HasValue || nameTok.Value.Kind is not (MoiraiTokenKind.TypeId or MoiraiTokenKind.Id))
                return TokenListParserResult.Empty<MoiraiTokenKind, TypeNode>(bracket.Remainder, "a type name");
            var close = nameTok.Remainder.ConsumeToken();
            if (!close.HasValue || close.Value.Kind != MoiraiTokenKind.RBrack)
                return TokenListParserResult.Empty<MoiraiTokenKind, TypeNode>(nameTok.Remainder, "']'");
            var span = Combine(bracket.Value, close.Value);
            return TokenListParserResult.Value(
                new TypeNode(IdentOf(nameTok.Value), true, span), input, close.Remainder);
        }

        if (bracket.HasValue && bracket.Value.Kind is MoiraiTokenKind.TypeId or MoiraiTokenKind.Id)
        {
            var ident = IdentOf(bracket.Value);
            return TokenListParserResult.Value(
                new TypeNode(ident, false, bracket.Value.Span), input, bracket.Remainder);
        }

        return TokenListParserResult.Empty<MoiraiTokenKind, TypeNode>(input, "a type name");
    }

    // number: NUMBER_FLOAT | NUMBER | PERCENT ;
    static TokenListParserResult<MoiraiTokenKind, NumberNode> NumberRule(TokenList<MoiraiTokenKind> input)
    {
        var t = input.ConsumeToken();
        if (!t.HasValue)
            return TokenListParserResult.Empty<MoiraiTokenKind, NumberNode>(input, "a number");

        var kind = t.Value.Kind switch
        {
            MoiraiTokenKind.NumberFloat => NumberKind.Float,
            MoiraiTokenKind.Number => NumberKind.Int,
            MoiraiTokenKind.Percent => NumberKind.Percent,
            _ => (NumberKind?) null,
        };
        if (kind == null)
            return TokenListParserResult.Empty<MoiraiTokenKind, NumberNode>(input, "a number");

        return TokenListParserResult.Value(
            new NumberNode(kind.Value, t.Value.ToStringValue(), t.Value.Span), input, t.Remainder);
    }

    // bool: TRUE | FALSE ;
    static TokenListParserResult<MoiraiTokenKind, (bool Value, TextSpan Span)> BoolRule(
        TokenList<MoiraiTokenKind> input)
    {
        var t = input.ConsumeToken();
        if (t.HasValue && t.Value.Kind == MoiraiTokenKind.True)
            return TokenListParserResult.Value((true, t.Value.Span), input, t.Remainder);
        if (t.HasValue && t.Value.Kind == MoiraiTokenKind.False)
            return TokenListParserResult.Value((false, t.Value.Span), input, t.Remainder);
        return TokenListParserResult.Empty<MoiraiTokenKind, (bool, TextSpan)>(input, "'true' or 'false'");
    }

    // enum_value: TYPE_ID DOT TYPE_ID ;
    static TokenListParserResult<MoiraiTokenKind, EnumValueNode> EnumValueRule(TokenList<MoiraiTokenKind> input)
    {
        var enumType = input.ConsumeToken();
        if (!enumType.HasValue || enumType.Value.Kind != MoiraiTokenKind.TypeId)
            return TokenListParserResult.Empty<MoiraiTokenKind, EnumValueNode>(input, "an enum type name");
        var dot = enumType.Remainder.ConsumeToken();
        if (!dot.HasValue || dot.Value.Kind != MoiraiTokenKind.Dot)
            return TokenListParserResult.Empty<MoiraiTokenKind, EnumValueNode>(enumType.Remainder, "'.'");
        var member = dot.Remainder.ConsumeToken();
        if (!member.HasValue || member.Value.Kind != MoiraiTokenKind.TypeId)
            return TokenListParserResult.Empty<MoiraiTokenKind, EnumValueNode>(dot.Remainder, "an enum member name");

        var span = Combine(enumType.Value, member.Value);
        return TokenListParserResult.Value(
            new EnumValueNode(IdentOf(enumType.Value), IdentOf(member.Value), span), input, member.Remainder);
    }

    // dot_property: DOT (property_id | call) ;
    static TokenListParserResult<MoiraiTokenKind, DotPropertyNode> DotProperty(TokenList<MoiraiTokenKind> input)
    {
        var dot = input.ConsumeToken();
        if (!dot.HasValue || dot.Value.Kind != MoiraiTokenKind.Dot)
            return TokenListParserResult.Empty<MoiraiTokenKind, DotPropertyNode>(input, "'.'");

        // property_id and call both start with ID — disambiguate by peeking one token further for '('.
        var afterId = PeekKindAt(dot.Remainder, 1);
        if (afterId == MoiraiTokenKind.ParenOpen)
        {
            var callResult = Call(dot.Remainder);
            if (!callResult.HasValue)
                return TokenListParserResult.CastEmpty<MoiraiTokenKind, CallNode, DotPropertyNode>(callResult);
            var span = Combine(dot.Value.Span, callResult.Value.Span);
            return TokenListParserResult.Value(
                new DotPropertyNode(null, callResult.Value, span), input, callResult.Remainder);
        }

        var propResult = PropertyId(dot.Remainder);
        if (!propResult.HasValue)
            return TokenListParserResult.CastEmpty<MoiraiTokenKind, Ident, DotPropertyNode>(propResult);
        var propSpan = Combine(dot.Value.Span, propResult.Value.Span);
        return TokenListParserResult.Value(
            new DotPropertyNode(propResult.Value, null, propSpan), input, propResult.Remainder);
    }

    // path : (var_id_read | property_id) dot_property* ;   var_id_read: SINGLETON_ID | VAR_ID ;
    static TokenListParserResult<MoiraiTokenKind, PathNode> Path(TokenList<MoiraiTokenKind> input)
    {
        var root = input.ConsumeToken();
        if (!root.HasValue)
            return TokenListParserResult.Empty<MoiraiTokenKind, PathNode>(input, "a path");

        Ident? singleton = null, varId = null, propertyId = null;
        TokenList<MoiraiTokenKind> remainder;
        switch (root.Value.Kind)
        {
            case MoiraiTokenKind.SingletonId:
                singleton = IdentOf(root.Value);
                remainder = root.Remainder;
                break;
            case MoiraiTokenKind.VarId:
                varId = IdentOf(root.Value);
                remainder = root.Remainder;
                break;
            case MoiraiTokenKind.Id:
                propertyId = IdentOf(root.Value);
                remainder = root.Remainder;
                break;
            default:
                return TokenListParserResult.Empty<MoiraiTokenKind, PathNode>(input,
                    "a variable, singleton, or property reference");
        }

        var dotProps = new List<DotPropertyNode>();
        var last = root.Value.Span;
        while (true)
        {
            var next = PeekKind(remainder);
            if (next != MoiraiTokenKind.Dot) break;
            var dp = DotProperty(remainder);
            if (!dp.HasValue)
                return TokenListParserResult.CastEmpty<MoiraiTokenKind, DotPropertyNode, PathNode>(dp);
            dotProps.Add(dp.Value);
            last = dp.Value.Span;
            remainder = dp.Remainder;
        }

        var span = Combine(root.Value.Span, last);
        return TokenListParserResult.Value(
            new PathNode(singleton, varId, propertyId, dotProps.ToArray(), span), input, remainder);
    }
}
