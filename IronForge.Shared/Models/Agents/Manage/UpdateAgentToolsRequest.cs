namespace IronForge.Shared.Models.Agents.Manage;

public sealed class UpdateAgentToolsRequest
{
    public List<AgentToolBindingDto> Tools { get; set; } = [];
}
