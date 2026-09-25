public class EntityType
{
    public readonly string Name;
    public readonly EntityTypeId Id;
    /// <summary>Declared with the <c>singleton</c> keyword: at most one instance, looked up O(1).</summary>
    public bool IsSingleton;
    public readonly List<PropertyDefinition> Properties = Database.DefaultProperties();
    public readonly List<FunctionDefinition> Functions = new(){default};

    public EntityType(string name, uint id)
    {
        Name = name;
        Id = new EntityTypeId(id);
    }
    public PropertyValue.ValueType RefType =>
        new PropertyValue.ValueType(PropertyValue.ValueBaseType.Ref, (ushort)Id.Id);

    public List<Display> Attributes { get; } = new();

    // ---- roles: what a property means to the engine and the viewer --------------------------------
    //
    // Kinship (related()), the family tree, the chronicle's population and ages all need to know which
    // property is a parent, a birth year, a death flag... A story says so with an attribute on the type
    // (@parents(mother, father), @dead(dead), @period(from, to), @population); what it does not say is
    // filled from the names w.sg uses (parent1/parent2, partner, birthdate, deathdate, alive,
    // start_year/end_year), so an unannotated story behaves exactly as before.

    private readonly Dictionary<EntityRole, PropertyId> _roles = new();
    private readonly HashSet<EntityRole> _declaredRoles = new();

    /// <summary>The property playing <paramref name="role"/>, or an invalid id if none does.</summary>
    public PropertyId Role(EntityRole role) => _roles.TryGetValue(role, out var p) ? p : default;

    /// <summary>Whether the story named the role itself rather than it being inferred from a name.</summary>
    public bool IsDeclared(EntityRole role) => _declaredRoles.Contains(role);

    public void DeclareRole(EntityRole role, PropertyId property)
    {
        _roles[role] = property;
        _declaredRoles.Add(role);
    }

    /// <summary>Declared with <c>@population</c>: the type whose living count is the world's population.</summary>
    public bool IsPopulation;

    public bool HasParents => Role(EntityRole.Parent1).IsValid && Role(EntityRole.Parent2).IsValid;

    public bool IsPeriod => Role(EntityRole.PeriodStart).IsValid && Role(EntityRole.PeriodEnd).IsValid;

    /// <summary>
    /// Fill every role the story did not declare from the conventional property name, where the type has
    /// one of the right kind. Parents count only in pairs; a declared @dead suppresses the alive default,
    /// since the two are one fact read two ways; and an age is only inferred on a type with no references,
    /// which is what keeps w.sg's ItemOwnership (who held what, from when to when) from being one.
    /// </summary>
    public void InferRoles(int builtinProperties)
    {
        bool Is(string name, Func<PropertyValue.ValueType, bool> kind, out PropertyId id)
        {
            var def = Properties.FirstOrDefault(p => p.Name == name);
            id = def.PropertyId;
            return def.Name != null && !def.IsCollection && kind(def.Type);
        }

        bool IsRef(PropertyValue.ValueType t) => t.IsRefType;
        bool IsNumber(PropertyValue.ValueType t) =>
            t.BaseType is PropertyValue.ValueBaseType.Number or PropertyValue.ValueBaseType.Float;
        bool IsBool(PropertyValue.ValueType t) => t.BaseType == PropertyValue.ValueBaseType.Bool;

        void Infer(EntityRole role, string name, Func<PropertyValue.ValueType, bool> kind)
        {
            if (!_declaredRoles.Contains(role) && Is(name, kind, out var id))
                _roles[role] = id;
        }

        if (!_declaredRoles.Contains(EntityRole.Parent1) && Is("parent1", IsRef, out var p1) && Is("parent2", IsRef, out var p2))
        {
            _roles[EntityRole.Parent1] = p1;
            _roles[EntityRole.Parent2] = p2;
        }
        Infer(EntityRole.Partner, "partner", IsRef);
        Infer(EntityRole.Birth, "birthdate", IsNumber);
        Infer(EntityRole.Death, "deathdate", IsNumber);
        if (!_declaredRoles.Contains(EntityRole.Dead))
            Infer(EntityRole.Alive, "alive", IsBool);

        var hasRefs = Properties.Skip(builtinProperties).Any(p => p.Type.IsRefType);
        if (!_declaredRoles.Contains(EntityRole.PeriodStart) && !hasRefs
            && Is("start_year", IsNumber, out var from) && Is("end_year", IsNumber, out var to))
        {
            _roles[EntityRole.PeriodStart] = from;
            _roles[EntityRole.PeriodEnd] = to;
        }
    }

    public EntityType DeclareProperty(PropertyDefinition propertyDefinition)
    {
        Properties.Add(propertyDefinition);
        return this;
    }

    public EntityType DeclareProperty(string propertyDefinition, uint propYearId, PropertyValue.ValueType valueType)
    {
        Properties.Add(new PropertyDefinition(propertyDefinition, Id, propYearId, valueType));
        return this;
    }

    public PropertyId GetPropertyId(string propName)
    {
        return Properties.FirstOrDefault(p => p.Name == propName).PropertyId;
    }

    public PropertyValue.ValueType GetPropertyType(string propName)
    {
        return Properties.FirstOrDefault(p => p.Name == propName).Type;
    }

    public bool GetFunctionDefinition(string funcName, out FunctionDefinition o)
    {
        o = Functions.Skip(1).FirstOrDefault(f => f.Name == funcName);
        return o.Id.IsValid;
    }
}

/// <summary>What a property means beyond its value. See <see cref="EntityType.Role"/>.</summary>
public enum EntityRole
{
    Parent1,
    Parent2,
    Partner,
    Birth,
    Death,
    Alive,
    Dead,
    PeriodStart,
    PeriodEnd,
}
