using Moirai.Core;

public interface IInstruction
{
    bool Execute(PredicateContext ctx);
}

// public interface IInstructionCall : IInstruction
// {   
//     public IFunctionDescriptor FunctionDescriptor { get; set; }
// }
