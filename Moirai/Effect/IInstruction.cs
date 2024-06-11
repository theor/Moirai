using Moirai.Core;
using ExecutionContext = Moirai.Core.ExecutionContext;

public interface IInstruction
{
    PropertyValue Execute(ExecutionContext ctx);
}

// public interface IInstructionCall : IInstruction
// {   
//     public IFunctionDescriptor FunctionDescriptor { get; set; }
// }
