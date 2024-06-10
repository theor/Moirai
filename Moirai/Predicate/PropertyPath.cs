public struct PropertyPath : IValue
{
    public record struct PropertyOrCall(PropertyId Property, UserFunctionCall? Call);
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
            Segments[^1] = new(pid, null);
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

    public readonly PropertyValue Compute(PredicateContext ctx)
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
                return ctx.GetPrevEntityProperty(Segments == null || Segments[0].Property == PropertyId.Null ? Database.PropId : Segments[0].Property);
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
            if(Segments[i].Property.IsValid)
            varValue = prevEntityProp
                ? ctx.GetPrevEntityProperty(Segments[i].Property)
                : e.GetProperty(Segments[i].Property);
            else
            {
                if (prevEntityProp)
                    throw new NotImplementedException("$old entity method call");
                
                
                using var s = ctx.RunScope(true);
                
                if(((UserFunctionDescriptor)Segments[i].Call.FunctionDescriptor).Definition.InstanceType.IsValid)
                    ctx.SetArgument(0, varValue);
                varValue = Segments[i].Call.Compute(ctx);
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

    public (string where, string? joins) ToSql(PredicateContext ctx)
    {
        // TODO must be contextual - if var is the one assigned, should be prop name, otherwise computed
        // TODO ugly
        if (Mode == PropertyPathMode.Variable &&
            (VariableIndex == -1 || VariableIndex == ctx.ValueCount - ctx.ValueOffset))
        {
            if (Segments == null)
                return ("default__id", null);
            if (Segments.Any(s => s.Call != null))
                throw new NotImplementedException("user function calls in sql");
            var s =
                $"entity.{ctx.Database.GetEntityTypeName(Segments[0].Property.TypeId)}__{ctx.Database.GetPropertyName(Segments[0].Property)}";
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
            for (int i = 1; i < Segments.Count; i++)
            {
                string thisVar = $"j{i}";
                string thisProp = $"{thisVar}.{ctx.Database.GetEntityTypeName(Segments[i].Property.TypeId)}__{ctx.Database.GetPropertyName(Segments[i].Property)}";
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
