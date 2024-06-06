using Moirai.Core;

public interface IInstruction
{
    PropertyValue Execute(PredicateContext ctx);
}

// public interface IInstructionCall : IInstruction
// {   
//     public IFunctionDescriptor FunctionDescriptor { get; set; }
// }
