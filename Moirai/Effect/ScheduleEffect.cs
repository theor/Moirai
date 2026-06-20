using System.Collections.Generic;
using System.Text;
using Moirai.Core;

/// <summary>
/// Schedule-time instruction for <c>schedule(entity, year) { body }</c>. When executed it evaluates the
/// target <see cref="Entity"/> and fire <see cref="Year"/> in the enclosing scope and enqueues the deferred
/// body (a registered "schedule site", see <see cref="Database.RegisterScheduleSite"/>) to fire once the
/// simulation reaches that year. The body itself runs later, via <see cref="Database.DrainScheduled"/>.
/// </summary>
public class ScheduleEffect : IValueCall
{
    public readonly IValue Entity;
    public readonly IValue Year;
    public readonly int SiteIndex;
    // Same instruction array as the registered site's Effects; kept here only so the printer can round-trip
    // the body (the head `schedule(entity, year)` alone would not reparse without its `{ }` block).
    public readonly IInstruction[] Body;

    public ScheduleEffect(IValue entity, IValue year, int siteIndex, IInstruction[] body)
    {
        Entity = entity;
        Year = year;
        SiteIndex = siteIndex;
        Body = body;
    }

    public PropertyValue Compute(ExecuteContext ctx)
    {
        var e = Entity.Compute(ctx);
        if (e.Id.IsNull)
            return true;
        var year = Year.Compute(ctx).IntValue;
        ctx.Database.EnqueueScheduled(year, e.Id, SiteIndex);
        return true;
    }

    public IFunctionDescriptor? FunctionDescriptor { get; set; }

    public IEnumerable<IValue> GetArgs(StoryPrinter printer)
    {
        yield return Entity;
        yield return Year;
    }

    public string Print(StoryPrinter printer, int indent)
    {
        var b = new StringBuilder(FunctionDescriptor?.Print(printer, this));
        b.AppendLine(" {");
        foreach (var effect in Body)
            printer.PrintEffect(effect, b, indent + 1);
        b.Append(StoryPrinter.IndentStr(indent) + " }");
        return b.ToString();
    }
}
