using System;
using System.Collections.Generic;
using System.Text;

namespace IronForge.Shared.Models.Agents
{
    public enum AgentExecutionState
    {
        Idle = 0,

        Sending = 1,

        Processing = 2,

        AwaitingApproval = 3,

        Completed = 4,

        Failed = 5
    }
}
