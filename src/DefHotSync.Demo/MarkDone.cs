using WorkflowCore.Interface;
using WorkflowCore.Models;

namespace DefHotSync.Demo;

public class MarkDone : StepBody
{
    public string Message { get; set; } = "";

    public override ExecutionResult Run(IStepExecutionContext context)
    {
        return ExecutionResult.Next();
    }
}
