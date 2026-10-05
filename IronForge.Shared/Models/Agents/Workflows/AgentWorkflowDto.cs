using System;
using System.Collections.Generic;
using System.Text;

namespace IronForge.Shared.Models.Agents.Workflows
{
    public class AgentWorkflowDto
    {
        public Guid Id { get; set; }

        public Guid? SessionId { get; set; }

        public string WorkflowType { get; set; } = "";

        public string Status { get; set; } = "";

        public string? CurrentStep { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime UpdatedAtUtc { get; set; }

        public DateTime? CompletedAtUtc { get; set; }

        public DateTime? FailedAtUtc { get; set; }

        public List<AgentWorkflowStepDto> Steps { get; set; } = new();
    }
}
