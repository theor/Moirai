using Pcg.Core;

public interface IInstruction
{
    bool Execute(PredicateContext ctx);
}