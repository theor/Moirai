using Moirai.Core;

public interface IInstruction
{
    PropertyValue Execute(ExecuteContext ctx);
}

// public interface IInstructionCall : IInstruction
// {   
//     public IFunctionDescriptor FunctionDescriptor { get; set; }
// }
