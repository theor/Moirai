public struct PropertyPath : IValue
{
    public readonly int VariableIndex;
    public readonly PropertyId Property;
    public readonly EntityTypeId SingletonType;

    public enum PropertyPathMode
    {
        Variable,
        Singleton
    }

    public readonly PropertyPathMode Mode;
    public PropertyPath(int variableIndex, PropertyId? property = null)
    {
        VariableIndex = variableIndex;
        Property = property ?? PropertyId.Null;
        Mode = PropertyPathMode.Variable;
        SingletonType = default;
    }
    public PropertyPath(EntityTypeId singletonTypeId, PropertyId propertyId)
    {
        SingletonType = singletonTypeId;
        Property = propertyId;
        Mode = PropertyPathMode.Singleton;
        VariableIndex = -1;
    }
    public bool Nested => false;

    public readonly PropertyValue Compute(PredicateContext ctx)
    {
        if (Mode == PropertyPathMode.Singleton)
        {
            if (!ctx.GetSingleton(SingletonType, out var entity))
                return default;
            if (Property == PropertyId.Null)
                return entity.Id;

            return entity.GetProperty(Property);
        }

        PropertyValue varValue = ctx.Argument(VariableIndex);
        if (varValue.Type != PropertyValue.TypeRef)
            return varValue;
        if (Property == PropertyId.Null)
            return varValue;
        if (!ctx.Database.TryGetEntity(varValue.Id, out var e))
        {
            if (varValue.Id.Id == Database.ChangePrevEntityId.Id)
            {
                return ctx.GetPrevEntityProperty(Property);
            }
            return default;
        }

        return e.GetProperty(Property);
    }

    public (string where, string? joins) ToSql(PredicateContext ctx)
    {
        // TODO must be contextual - if var is the one assigned, should be prop name, otherwise computed
        // TODO ugly
        if (Mode == PropertyPathMode.Variable && (VariableIndex == -1 || VariableIndex == ctx.ValueCount - ctx.ValueOffset))
            return (Property.IsValid ? $"{ctx.Database.GetEntityTypeName(Property.TypeId)}__{ctx.Database.GetPropertyName(Property)}" : "default__id", null);
        // return /*Property.IsValid ?*/ ctx.Database.GetPropertyName(Property);// : Compute(ctx).ToSql();
        return (Compute(ctx).ToSql(), null);
    }
}
