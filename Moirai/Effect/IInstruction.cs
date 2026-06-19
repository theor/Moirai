using Moirai.Core;

public interface IInstruction
{
    PropertyValue Execute(ExecuteContext ctx);

    /// <summary>
    /// Source location of the statement this instruction was compiled from, or
    /// <see cref="SourceSpan.None"/> when unknown. Populated by the parser; consumed
    /// by the debugger to map breakpoints/stepping to lines.
    /// </summary>
    SourceSpan Source { get; set; }
}

// public interface IInstructionCall : IInstruction
// {   
//     public IFunctionDescriptor FunctionDescriptor { get; set; }
// }
