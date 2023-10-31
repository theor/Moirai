using Pcg;

static class NameEntity
{
    public static bool AssignName(PredicateContext ctx, EntityId entityId)
    {
        if (!ctx.Database.TryGetEntity(entityId.Id, out var e))
            return false;

        var t = (EntityType)e.GetProperty(Database.PropType).IntValue;

        string name = GenerateName(ctx, t, in e);
        return ctx.Database.SetProperty(ctx.EntityId, Database.PropName, name);
    }

    private static readonly string[] Items =
    {
        "Ring", "Sword", "Spear", "Breastplate", "Greatsword", "Pendant",
    };

    private static readonly string[] Names =
    {
        // ReSharper disable StringLiteralTypo
        "Abraxas", "Adara", "Adrienne", "Aeron", "Aeronwen", "Aeronwy", "Ailbhe", "Aileen", "Aislinn", "Aithne", "Alanna", "Alastair",
        "Alastriona", "Albion", "Altalune", "Amaris", "Amina", "Amira", "Anastasia", "Andreas", "Aneira", "Angeline", "Aodh", "Aoife",
        "Arella", "Arianell", "Arianwen", "Artemis", "Artemisia", "Arthur", "Arwen", "Ascella", "Asteria", "Astoria", "Astra", "Astraea",
        "Astraia", "Astrid", "Astrophel", "Auberon", "Aud", "Audrienne", "Aurelius", "Aurora", "Autumn", "Avalon", "Azalea", "Bara",
        "Bedivere", "Belinda", "Belladonna", "Bianca", "Blanchefleur", "Branwen", "Briar", "Bronwyn", "Caelan", "Caitriona", "Calla",
        "Calliope", "Camellia", "Caradoc", "Cerelia", "Cerella", "Ceridwen", "Chandra", "Ciaran", "Clarimond", "Clarinda", "Clarine",
        "Corabel", "Corabella", "Corbin", "Cordelia", "Corinda", "Corisande", "Crescent", "Darius", "Dawn", "Dominic", "Edith", "Eilidh",
        "Elaine", "Elara", "Eleri", "Elora", "Emrys", "Endellion", "Ethelinda", "Evander", "Evangelia", "Evangelina", "Evangeline",
        "Evangelique", "Fae", "Faye", "Ferelith", "Fiona", "Galahad", "Gawain", "Ginevra", "Gloriana", "Griffin", "Guinevere", "Gwenllian",
        "Gwenore", "Hecate", "Hesperia", "Hestia", "Io", "Iona", "Isolde", "Izora", "Jocasta", "Khione", "Lancelot", "Lavinia", "Leander",
        "Lethia", "Liora", "Lorcan", "Lowenna", "Lowri", "Lucan", "Lucienne", "Lucina", "Lucine", "Luna", "Lunette", "Lysander", "Lysandra",
        "Melisande", "Melisende", "Merlin", "Mirian", "Moon", "Morgaine", "Morgana", "Morrigan", "Myrcella", "Niamh", "Nimue", "Oberon",
        "Oleander", "Olwyn", "Opal", "Orenda", "Oriana", "Owain", "Percival", "Persephone", "Reverie", "Rhian", "River", "Rosabel",
        "Rosabella", "Rosabelle", "Rosella", "Rosina", "Rowena", "Rune", "Sage", "Senara", "Silvana", "Sonora", "Sorcha", "Sybella",
        "Taliesin", "Tamora", "Tarian", "Titania", "Tristan", "Twyla", "Victoire", "Vivia", "Viviana", "Viviane", "Willow", "Yvaine",
        "Zella", "Áine"
        // ReSharper restore StringLiteralTypo
    };

    private static string GenerateName(PredicateContext predicateContext, EntityType t, in Entity entity)
    {
        var n = Names.RandomIn(predicateContext.Rnd);
        switch (t)
        {

            case EntityType.Person:
                return n;
            case EntityType.Item:
                return Items.RandomIn(predicateContext.Rnd) + " of " + n;
            case EntityType.Faction:
                return "Faction of " + n;
            default:
                throw new ArgumentOutOfRangeException(nameof(t), t, null);
        }
    }

    static T RandomIn<T>(this T[] array, Pcg32 rnd) => array[rnd.GenerateNext((uint)array.Length)];
}