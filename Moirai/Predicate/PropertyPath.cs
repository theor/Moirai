using System.Linq;
using Moirai.Core;

public struct  PropertyPath : IValueSql
{
    public record struct PropertyOrCall(PropertyId Property, UserFunctionCall? Call)
    {
        public EntityTypeId TypeId => Call?.Definition.InstanceType ?? Property.TypeId;
    }

    public readonly int VariableIndex;
    public readonly PropertyValue.ValueType TypeId;
    public List<PropertyOrCall>? Segments;

    public enum PropertyPathMode
    {
        Variable,
        Singleton
    }

    public readonly PropertyPathMode Mode;

    public PropertyPath(int variableIndex, PropertyValue.ValueType typeId, PropertyId? property = null)
    {
        VariableIndex = variableIndex;
        TypeId = typeId;
        if (property != null)
            Segments = new() { new(property.Value, null) };
        Mode = PropertyPathMode.Variable;
    }

    public PropertyPath(EntityTypeId typeId)
    {
        TypeId = PropertyValue.TypeTypedRef(typeId);
        Segments = null;
        Mode = PropertyPathMode.Singleton;
        VariableIndex = -1;
    }

    // Owner path = this path minus its last segment. Used to split a collection path like
    // `$c.parents` into (owner=`$c`, collProp=`parents`).
    private PropertyPath(in PropertyPath src, PropertyValue.ValueType ownerType)
    {
        VariableIndex = src.VariableIndex;
        Mode = src.Mode;
        TypeId = ownerType;
        Segments = src.Segments!.Count <= 1 ? null : src.Segments.Take(src.Segments.Count - 1).ToList();
    }

    // General constructor used when inlining a function call: arbitrary base + segments.
    private PropertyPath(int variableIndex, PropertyPathMode mode, PropertyValue.ValueType typeId,
        List<PropertyOrCall>? segments)
    {
        VariableIndex = variableIndex;
        Mode = mode;
        TypeId = typeId;
        Segments = segments;
    }

    /// <summary>
    /// Re-roots this path (which references a function parameter) onto the call argument
    /// <paramref name="arg"/>. A bare parameter (<c>$p</c>) becomes the argument as-is; a parameter
    /// with trailing access (<c>$p.prop</c>) appends those segments onto the argument's path. Used to
    /// inline a function body into the caller's scope so it compiles to SQL with multiple parameters.
    /// </summary>
    public readonly IValue RebaseOnto(IValue arg)
    {
        if (Segments == null || Segments.Count == 0)
            return arg;
        if (arg is PropertyPath ap)
        {
            var segs = new List<PropertyOrCall>();
            if (ap.Segments != null)
                segs.AddRange(ap.Segments);
            segs.AddRange(Segments);
            return new PropertyPath(ap.VariableIndex, ap.Mode, TypeId, segs);
        }

        throw new NotImplementedException("inlining a non-path argument with trailing property access");
    }

    /// <summary>
    /// Splits a path whose final segment is a collection property into the owner entity path and the
    /// collection <see cref="PropertyId"/>. Returns false if there is no trailing property segment.
    /// </summary>
    public bool TrySplitCollection(out PropertyPath owner, out PropertyId collProp)
    {
        owner = default;
        collProp = default;
        if (Segments == null || Segments.Count == 0 || Segments[^1].Call != null)
            return false;
        collProp = Segments[^1].Property;
        owner = new PropertyPath(in this, PropertyValue.TypeTypedRef(collProp.TypeId));
        return true;
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
        Segments = new List<PropertyOrCall>(1) {
                new (default, call),
                
            };
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

        PropertyValue varValue = VariableIndex != -1 ? ctx.Argument(VariableIndex) : default;
        Entity e = default;
        if (varValue.Type == PropertyValue.TypeRef)
            if (!ctx.Database.TryGetEntity(varValue.Id, out e))
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
}
