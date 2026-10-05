using System.Diagnostics.Metrics;

namespace IronForge.Application.Observablity.Metrics
{
    public static class IronForgeMetrics
    {
        public static readonly Counter<long> AgentExecutions =
            IronForgeMeter.Meter.CreateCounter<long>(
                "ironforge.agent.executions",
                description: "Number of agent executions.");

        public static readonly Counter<long> AgentFailures =
            IronForgeMeter.Meter.CreateCounter<long>(
                "ironforge.agent.failures",
                description: "Number of failed agent executions.");

        public static readonly Histogram<double> AgentDuration =
            IronForgeMeter.Meter.CreateHistogram<double>(
                "ironforge.agent.duration",
                unit: "ms",
                description: "Agent execution duration.");

        public static readonly Counter<long> ToolExecutions =
            IronForgeMeter.Meter.CreateCounter<long>(
                "ironforge.tool.executions",
                description: "Number of tool executions.");

        public static readonly Counter<long> ToolFailures =
            IronForgeMeter.Meter.CreateCounter<long>(
                "ironforge.tool.failures",
                description: "Number of failed tool executions.");

        public static readonly Histogram<double> ToolDuration =
            IronForgeMeter.Meter.CreateHistogram<double>(
                "ironforge.tool.duration",
                unit: "ms",
                description: "Tool execution duration.");

        public static readonly Counter<long> WorkflowCreated =
            IronForgeMeter.Meter.CreateCounter<long>(
                "ironforge.workflow.created",
                description: "Number of workflows created.");

        public static readonly Counter<long> WorkflowCompleted =
            IronForgeMeter.Meter.CreateCounter<long>(
                "ironforge.workflow.completed",
                description: "Number of completed workflows.");

        public static readonly Counter<long> WorkflowFailed =
            IronForgeMeter.Meter.CreateCounter<long>(
                "ironforge.workflow.failed",
                description: "Number of failed workflows.");

        public static readonly Histogram<double> WorkflowDuration =
            IronForgeMeter.Meter.CreateHistogram<double>(
                "ironforge.workflow.duration",
                unit: "ms",
                description: "Workflow execution duration.");

        public static readonly Counter<long> ApprovalRequested =
            IronForgeMeter.Meter.CreateCounter<long>(
                "ironforge.approval.requested",
                description: "Number of approval requests.");

        public static readonly Counter<long> ApprovalApproved =
            IronForgeMeter.Meter.CreateCounter<long>(
                "ironforge.approval.approved",
                description: "Number of approved requests.");

        public static readonly Counter<long> ApprovalRejected =
            IronForgeMeter.Meter.CreateCounter<long>(
                "ironforge.approval.rejected",
                description: "Number of rejected requests.");

        public static readonly Counter<long> ApprovalExpired =
            IronForgeMeter.Meter.CreateCounter<long>(
                "ironforge.approval.expired",
                description: "Number of expired requests.");

        public static KeyValuePair<string, object?> AgentTag(string agentId)
        {
            return new(
                "agent.id",
                agentId);
        }
        public static KeyValuePair<string, object?> ToolTag(
        string toolName)
        {
            return new(
                "tool.name",
                toolName);
        }
        public static KeyValuePair<string, object?> ToolPathTag(
        string path)
        {
            return new(
                "execution.path",
                path);
        }
        public static KeyValuePair<string, object?> WorkflowTypeTag(
            string workflowType)
            => new("workflow.type", workflowType);

        public static KeyValuePair<string, object?> StepTypeTag(
            string stepType)
            => new("step.type", stepType);

        public static KeyValuePair<string, object?> ExecutionResultTag(
            string result)
            => new("execution.result", result);


    }
}
