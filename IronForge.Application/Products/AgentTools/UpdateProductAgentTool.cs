using IronForge.Shared.Subscriptions;
using IronForge.Application.Agents.Governance;
using IronForge.Application.Agents.Tools;
using IronForge.Application.Products.DTOs;
using IronForge.Application.Products.Tools.Products;
using IronForge.Shared.Authorization;

namespace IronForge.Application.Products.AgentTools;

public sealed class UpdateProductAgentTool : AgentTool
{
    private readonly IUpdateProductTool _tool;

    public UpdateProductAgentTool(IUpdateProductTool tool)
    {
        _tool = tool;
    }

    public override AgentToolDescriptor Descriptor { get; } =
        new()
        {
            Name = "update_product",
            Description = "Updates an existing IronForge product.",
            RequiredPermission = Permissions.ProductsUpdate,
            Module = PlatformModules.Products,
            RiskLevel = AgentRiskLevel.Medium,
            RequiresApproval = true
        };

    protected override Delegate Handler => ExecuteAsync;

    private async Task<object> ExecuteAsync(int id, string name, decimal price)
    {
        var result = await _tool.ExecuteAsync(
            id,
            new ProductWriteDto
            {
                Name = name,
                Price = price
            });

        return new
        {
            Success = result.Success,
            Error = result.ErrorMessage,
            StatusCode = result.StatusCode
        };
    }
}
