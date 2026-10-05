using Microsoft.Extensions.Logging;

namespace IronForge.Application.Observablity.Logging
{
    public static partial class IronForgeLog
    {
        // ============================================================
        // Agent
        // Event IDs: 1000 - 1099
        // ============================================================

        [LoggerMessage(
            EventId = 1001,
            Level = LogLevel.Information,
            Message = "Agent execution started. RequestedSessionId={RequestedSessionId}, UserId={UserId}")]
        public static partial void AgentExecutionStarted(
            this ILogger logger,
            Guid? requestedSessionId,
            int? userId);

        [LoggerMessage(
            EventId = 1002,
            Level = LogLevel.Information,
            Message = "Agent session established. SessionId={SessionId}, AgentId={AgentId}, UserId={UserId}")]
        public static partial void AgentSessionEstablished(
            this ILogger logger,
            Guid sessionId,
            string agentId,
            int userId);

        [LoggerMessage(
            EventId = 1003,
            Level = LogLevel.Information,
            Message = "Agent execution completed. SessionId={SessionId}, AgentId={AgentId}")]
        public static partial void AgentExecutionCompleted(
            this ILogger logger,
            Guid sessionId,
            string agentId);

        // ============================================================
        // Agent approvals
        // Event IDs: 1100 - 1199
        // ============================================================

        [LoggerMessage(
            EventId = 1101,
            Level = LogLevel.Information,
            Message = "Agent approval created. ApprovalId={ApprovalId}, RequestId={RequestId}, ToolName={ToolName}, RiskLevel={RiskLevel}, UserId={UserId}, AgentId={AgentId}")]
        public static partial void AgentApprovalCreated(
            this ILogger logger,
            string approvalId,
            string requestId,
            string toolName,
            object riskLevel,
            int userId,
            string agentId);

        [LoggerMessage(
            EventId = 1102,
            Level = LogLevel.Information,
            Message = "Agent approval decision received. ApprovalId={ApprovalId}, Approved={Approved}")]
        public static partial void AgentApprovalDecisionReceived(
            this ILogger logger,
            string approvalId,
            bool approved);

        [LoggerMessage(
            EventId = 1103,
            Level = LogLevel.Information,
            Message = "Agent approval approved. ApprovalId={ApprovalId}, SessionId={SessionId}, ToolName={ToolName}")]
        public static partial void AgentApprovalApproved(
            this ILogger logger,
            string approvalId,
            Guid sessionId,
            string toolName);

        [LoggerMessage(
            EventId = 1104,
            Level = LogLevel.Information,
            Message = "Agent approval rejected. ApprovalId={ApprovalId}, SessionId={SessionId}, ToolName={ToolName}")]
        public static partial void AgentApprovalRejected(
            this ILogger logger,
            string approvalId,
            Guid sessionId,
            string toolName);

        [LoggerMessage(
            EventId = 1105,
            Level = LogLevel.Warning,
            Message = "Agent approval expired. ApprovalId={ApprovalId}, SessionId={SessionId}, ToolName={ToolName}")]
        public static partial void AgentApprovalExpired(
            this ILogger logger,
            string approvalId,
            Guid sessionId,
            string toolName);

        // ============================================================
        // Workflow
        // Event IDs: 1200 - 1299
        // ============================================================

        [LoggerMessage(
            EventId = 1201,
            Level = LogLevel.Information,
            Message = "Agent workflow created. WorkflowId={WorkflowId}, WorkflowType={WorkflowType}, SessionId={SessionId}, UserId={UserId}, TenantId={TenantId}, AgentId={AgentId}")]
        public static partial void AgentWorkflowCreated(
            this ILogger logger,
            Guid workflowId,
            string workflowType,
            Guid? sessionId,
            int userId,
            int tenantId,
            string agentId);

        [LoggerMessage(
            EventId = 1202,
            Level = LogLevel.Information,
            Message = "Agent workflow step started. WorkflowId={WorkflowId}, StepType={StepType}, Sequence={Sequence}")]
        public static partial void AgentWorkflowStepStarted(
            this ILogger logger,
            Guid workflowId,
            string stepType,
            int sequence);

        [LoggerMessage(
            EventId = 1203,
            Level = LogLevel.Information,
            Message = "Agent workflow step completed. WorkflowId={WorkflowId}, StepType={StepType}, Sequence={Sequence}")]
        public static partial void AgentWorkflowStepCompleted(
            this ILogger logger,
            Guid workflowId,
            string stepType,
            int sequence);

        [LoggerMessage(
            EventId = 1204,
            Level = LogLevel.Information,
            Message = "Agent workflow waiting for approval. WorkflowId={WorkflowId}, ApprovalId={ApprovalId}, ToolName={ToolName}")]
        public static partial void AgentWorkflowWaitingForApproval(
            this ILogger logger,
            Guid workflowId,
            string approvalId,
            string toolName);

        [LoggerMessage(
            EventId = 1205,
            Level = LogLevel.Information,
            Message = "Agent workflow cancelled. WorkflowId={WorkflowId}, ApprovalId={ApprovalId}")]
        public static partial void AgentWorkflowCancelled(
            this ILogger logger,
            Guid workflowId,
            string approvalId);

        [LoggerMessage(
            EventId = 1206,
            Level = LogLevel.Information,
            Message = "Agent workflow completed. WorkflowId={WorkflowId}, WorkflowType={WorkflowType}, SessionId={SessionId}")]
        public static partial void AgentWorkflowCompleted(
            this ILogger logger,
            Guid workflowId,
            string workflowType,
            Guid? sessionId);

        [LoggerMessage(
            EventId = 1207,
            Level = LogLevel.Error,
            Message = "Agent workflow step failed. WorkflowId={WorkflowId}, StepType={StepType}, Sequence={Sequence}")]
        public static partial void AgentWorkflowStepFailed(
            this ILogger logger,
            Exception exception,
            Guid workflowId,
            string stepType,
            int sequence);

        // ============================================================
        // Approved tool execution
        // Event IDs: 1300 - 1399
        // ============================================================

        [LoggerMessage(
            EventId = 1301,
            Level = LogLevel.Information,
            Message = "Approved tool execution started. ApprovalId={ApprovalId}, ToolName={ToolName}, SessionId={SessionId}")]
        public static partial void ApprovedToolExecutionStarted(
            this ILogger logger,
            string approvalId,
            string toolName,
            Guid sessionId);

        [LoggerMessage(
            EventId = 1302,
            Level = LogLevel.Warning,
            Message = "Approved tool execution authorization denied. ApprovalId={ApprovalId}, ToolName={ToolName}, Reason={Reason}")]
        public static partial void ApprovedToolExecutionAuthorizationDenied(
            this ILogger logger,
            string approvalId,
            string toolName,
            string? reason);

        [LoggerMessage(
            EventId = 1303,
            Level = LogLevel.Information,
            Message = "Approved tool execution completed. ApprovalId={ApprovalId}, ToolName={ToolName}, SessionId={SessionId}")]
        public static partial void ApprovedToolExecutionCompleted(
            this ILogger logger,
            string approvalId,
            string toolName,
            Guid sessionId);

        [LoggerMessage(
            EventId = 1304,
            Level = LogLevel.Error,
            Message = "Approved tool execution failed. ApprovalId={ApprovalId}, ToolName={ToolName}, SessionId={SessionId}")]
        public static partial void ApprovedToolExecutionFailed(
            this ILogger logger,
            Exception exception,
            string approvalId,
            string toolName,
            Guid sessionId);

        // ============================================================
        // Product
        // Event IDs: 1400 - 1499
        // ============================================================

        [LoggerMessage(
            EventId = 1401,
            Level = LogLevel.Information,
            Message = "Product created. ProductId={ProductId}, UserId={UserId}")]
        public static partial void ProductCreated(
            this ILogger logger,
            int productId,
            int userId);

        [LoggerMessage(
            EventId = 1402,
            Level = LogLevel.Information,
            Message = "Product updated. ProductId={ProductId}")]
        public static partial void ProductUpdated(
            this ILogger logger,
            int productId);

        [LoggerMessage(
            EventId = 1403,
            Level = LogLevel.Information,
            Message = "Product deleted. ProductId={ProductId}")]
        public static partial void ProductDeleted(
            this ILogger logger,
            int productId);
    }
}
