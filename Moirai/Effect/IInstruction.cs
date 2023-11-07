using Moirai.Core;

public interface IInstruction
{
    bool Execute(PredicateContext ctx);
}