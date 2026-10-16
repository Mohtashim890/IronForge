using IronForge.Shared.Subscriptions;
using IronForge.Application.Agents.Governance;
using IronForge.Application.Agents.Tools;
using IronForge.Application.Products.Tools.Products;
using IronForge.Shared.Authorization;

namespace IronForge.Application.Products.AgentTools;

public sealed class DeleteProductAgentTool : AgentTool
{
    private readonly IDeleteProductTool _tool;

    public DeleteProductAgentTool(IDeleteProductTool tool)
    {
        _tool = tool;
    }

    public override AgentToolDescriptor Descriptor { get; } =
        new()
        {
            Name = "delete_product",
            Description = "Permanently deletes an existing IronForge product.",
            RequiredPermission = Permissions.ProductsDelete,
            Module = PlatformModules.Products,
            RiskLevel = AgentRiskLevel.High,
            RequiresApproval = true
        };

    protected override Delegate Handler => ExecuteAsync;

    private async Task<object> ExecuteAsync(int id)
    {
        var result = await _tool.ExecuteAsync(id);

        return new
        {
            Success = result.Success,
            Error = result.ErrorMessage,
            StatusCode = result.StatusCode
        };
    }
}
