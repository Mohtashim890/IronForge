using System;
using System.Collections.Generic;
using System.Text;

namespace IronForge.Shared.Models.Agents.Workflows
{
    public class AgentWorkflowStepDto
    {
        public Guid Id { get; set; }

        public int Sequence { get; set; }

        public string StepType { get; set; } = "";

        public string Status { get; set; } = "";

        public string? ErrorMessage { get; set; }

        public DateTime CreatedAtUtc { get; set; }

        public DateTime? StartedAtUtc { get; set; }

        public DateTime? CompletedAtUtc { get; set; }
    }
}
