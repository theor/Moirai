public struct PropertyPath : IValue
{
    public readonly int VariableIndex;
    public List<PropertyId>? Property;

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
            Property = new() { property.Value };
        Mode = PropertyPathMode.Variable;
    }

    public PropertyPath(PropertyId propertyId)
    {
        Property = new List<PropertyId> { propertyId };
        Mode = PropertyPathMode.Singleton;
        VariableIndex = -1;
    }

    public void AddProperty(PropertyId pid)
    {
        if (Property == null)
        {
            Property = new() { pid };
            return;
        }

        if (Property.Count > 0 && Property[^1].Id == 0)
            Property[^1] = pid;
        else
            Property.Add(pid);
    }

    public bool Nested => (Property?.Count ?? 0) > 1;

    public readonly PropertyValue Compute(PredicateContext ctx)
    {
        if (Mode == PropertyPathMode.Singleton)
        {
            if (!ctx.GetSingleton(Property[0].TypeId, out var entity))
                return default;
            if (Property[0].Id == 0)
                return entity.Id;

            return entity.GetProperty(Property[0]);
        }

        PropertyValue varValue = ctx.Argument(VariableIndex);
        if (varValue.Type != PropertyValue.TypeRef)
            return varValue;
        if (Property == null || Property[0] == PropertyId.Null)
            return varValue;
        if (!ctx.Database.TryGetEntity(varValue.Id, out var e))
        {
            if (varValue.Id.Id == Database.ChangePrevEntityId.Id)
            {
                return ctx.GetPrevEntityProperty(Property[0]);
            }

            return default;
        }

        // return e.GetProperty(Property[0]);

        PropertyValue val = default;
        bool prevEntityProp = false;
        for (int i = 0; i < Property.Count; i++)
        {
            val = prevEntityProp
                ? ctx.GetPrevEntityProperty(Property[i])
                : e.GetProperty(Property[i]);
            prevEntityProp = false;
            if (i < Property.Count - 1)
            {
                if (!ctx.Database.TryGetEntity(val.Id, out e))
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

        return val;
    }

    public (string where, string? joins) ToSql(PredicateContext ctx)
    {
        // TODO must be contextual - if var is the one assigned, should be prop name, otherwise computed
        // TODO ugly
        if (Mode == PropertyPathMode.Variable &&
            (VariableIndex == -1 || VariableIndex == ctx.ValueCount - ctx.ValueOffset))
        {
            if (Property == null || !Property[0].IsValid)
                return ("default__id", null);
            var s =
                $"entity.{ctx.Database.GetEntityTypeName(Property[0].TypeId)}__{ctx.Database.GetPropertyName(Property[0])}";
            var prevProp = s;
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
            string? join = null;
            for (int i = 1; i < Property.Count; i++)
            {
                string thisVar = $"j{i}";
                string thisProp = $"{thisVar}.{ctx.Database.GetEntityTypeName(Property[i].TypeId)}__{ctx.Database.GetPropertyName(Property[i])}";
                s += $" != 0 AND " + thisProp;
                string pj = $"LEFT JOIN entity j{i} ON {prevProp} = {thisVar}.default__id";
                prevProp = thisProp;
                join = join == null ? pj : (join + "\n" + pj);
            }
            return (s, join);
        }
        // return /*Property.IsValid ?*/ ctx.Database.GetPropertyName(Property);// : Compute(ctx).ToSql();
        return (Compute(ctx).ToSql(), null);
    }
}
