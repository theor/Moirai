using Moirai.Core;

public struct PropertyPath : IValueSql
{
    public record struct PropertyOrCall(PropertyId Property, UserFunctionCall? Call)
    {
        public EntityTypeId TypeId => Call?.Definition.InstanceType ?? Property.TypeId;
    }

    public readonly int VariableIndex;
    public readonly EntityTypeId SingletonTypeId;
    public List<PropertyOrCall>? Segments;

    public enum PropertyPathMode
    {
        Variable,
        Singleton
    }

    public readonly PropertyPathMode Mode;

    public PropertyPath(int variableIndex, PropertyId? property = null)
    {
        VariableIndex = variableIndex;
        if (property != null)
            Segments = new() { new(property.Value, null) };
        Mode = PropertyPathMode.Variable;
    }

    public PropertyPath(EntityTypeId singletonTypeId)
    {
        SingletonTypeId = singletonTypeId;
        Segments = null;
        Mode = PropertyPathMode.Singleton;
        VariableIndex = -1;
    }

    public void AddProperty(PropertyId pid)
    {
        if (Segments == null)
        {
            Segments = new() { new(pid, null) };
            return;
        }

        // ???
        if (Segments.Count > 0 && Segments[^1].Property.Id == 0)
        {
            throw new InvalidDataException("wtf");
        }
        else
            Segments.Add(new(pid, null));
    }

    public void AddCall(UserFunctionCall call)
    {
        Segments ??= new();
        Segments.Add(new(default, call));
    }

    public bool Nested => (Segments?.Count ?? 0) > 1;

    public readonly PropertyValue Compute(ExecuteContext ctx)
    {
        if (Mode == PropertyPathMode.Singleton)
        {
            // TODO #Singleton.method()
            if (!ctx.GetSingleton(Segments[0].Property.TypeId, out var entity))
                return default;
            if (Segments[0].Property.Id == 0)
                return entity.Id;

            return entity.GetProperty(Segments[0].Property);
        }

        PropertyValue varValue = ctx.Argument(VariableIndex);
        if (varValue.Type != PropertyValue.TypeRef)
            return varValue;

        if (!ctx.Database.TryGetEntity(varValue.Id, out var e))
        {
            if (varValue.Id.Id == Database.ChangePrevEntityId.Id)
            {
                return ctx.GetPrevEntityProperty(Segments == null || Segments[0].Property == PropertyId.Null
                    ? Database.PropId
                    : Segments[0].Property);
            }

            return default;
        }

        if (Segments == null)
            return varValue;
        if (Segments[0].Call == null && Segments[0].Property == PropertyId.Null)
            return varValue;
        // return e.GetProperty(Property[0]);

        bool prevEntityProp = false;
        for (int i = 0; i < Segments.Count; i++)
        {
            if (Segments[i].Property.IsValid)
                varValue = prevEntityProp
                    ? ctx.GetPrevEntityProperty(Segments[i].Property)
                    : e.GetProperty(Segments[i].Property);
            else
            {
                if (prevEntityProp)
                    throw new NotImplementedException("$old entity method call");

                var userFunctionCall = Segments[i].Call;
                varValue = userFunctionCall.Compute(ctx,
                    userFunctionCall.Definition.IsInstanceMethod ? varValue : default);
            }

            prevEntityProp = false;
            if (i < Segments.Count - 1)
            {
                if (!ctx.Database.TryGetEntity(varValue.Id, out e))
                {
                    if (varValue.Id.Id == Database.ChangePrevEntityId.Id)
                    {
                        prevEntityProp = true;
                        continue;
                    }

                    return default;
                }
            }
        }

        return varValue;
    }

    public (string where, string? joins) ToSql(ExecuteContext ctx)
    {
        /* pick Person $r: ($r.birthplace.type = Place.City)
 SELECT entity.default__id FROM entity
     LEFT JOIN entity x
         ON entity.Person__birthplace = x.default__id
     WHERE entity.default__type = 2
         AND (entity.Person__birthplace != 0)
         AND x.type = 3
  */
        /* pick Person $r: ($r.birthplace.founder.birthdate = 1234)
        SELECT entity.default__id FROM entity
            LEFT JOIN entity x
                ON entity.Person__birthplace = x.default__id
            LEFT JOIN entity y
                ON x.founder = y.default__id
            WHERE entity.default__type = 2
                AND ((entity.Person__birthplace != 0)
                AND (x.founder != 0)
                AND (y.birthdate = 1234)
         */

        // TODO must be contextual - if var is the one assigned, should be prop name, otherwise computed
        // TODO ugly
        if (Mode != PropertyPathMode.Variable ||
            (VariableIndex != -1 && VariableIndex != ctx.ValueCount - ctx.ValueOffset))
            return (Compute(ctx).ToSql(), null);
        

        // if (Segments[0].Call != null)
        // throw new NotImplementedException("user function calls in sql " + Segments[0].Call.ToSql(ctx));

        var where = "";
        string? join = null;
        string? prevProp = null;

        InlineSql(ctx, ref where, ref join, prevProp, false);

        return (where, join);

        // return /*Property.IsValid ?*/ ctx.Database.GetPropertyName(Property);// : Compute(ctx).ToSql();
    }

    public void InlineSql(ExecuteContext ctx, ref string where, ref string? join)
    {
        InlineSql(ctx, ref where, ref join, null, true);
    }
    public void InlineSql(ExecuteContext ctx, ref string where, ref string? join, string? prevProp, bool skipThis)
    {
        if (Segments == null)
        {
            where = "default__id";
            join = null;
            return;
        }
        for (int i = 0; i < Segments.Count; i++)
        {
            PropertyOrCall p = Segments[i];
            string? thisVar = null;
            bool isJoin = i != 0;
            if (!isJoin)
            {
                if(!skipThis)
                    thisVar = "entity.";
            }
            else
            {
                where += " != 0 AND ";
                thisVar = $"j{i}.";
            }

            string thisProp;
            if (p.Call != null)
            {
                string callWhere = "";
                string? callJoin = null;
                p.Call.InlineSql(ctx, ref callWhere, ref callJoin);
                // var (callWhere, callJoin) = p.Call.ToSql(ctx);
                thisProp = $"{thisVar}{callWhere}";
            }
            else
            {
                thisProp =
                    $"{thisVar}{ctx.Database.GetEntityTypeName(p.TypeId)}__{ctx.Database.GetPropertyName(p.Property)}";
            }

            where += thisProp;

            if (isJoin)
            {
                string pj = $"LEFT JOIN entity j{i} ON {prevProp} = {thisVar}default__id";
                join = join == null ? pj : (join + "\n" + pj);
            }

            prevProp = thisProp;
        }

    }
}
