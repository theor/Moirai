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
            return (
                Property != null && Property[0].IsValid
                    ? $"{ctx.Database.GetEntityTypeName(Property[0].TypeId)}__{ctx.Database.GetPropertyName(Property[0])}"
                    : "default__id", null);
        // return /*Property.IsValid ?*/ ctx.Database.GetPropertyName(Property);// : Compute(ctx).ToSql();
        return (Compute(ctx).ToSql(), null);
    }
}
