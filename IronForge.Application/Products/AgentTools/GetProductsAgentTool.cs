using IronForge.Shared.Subscriptions;
using IronForge.Application.Agents.Governance;
using IronForge.Application.Agents.Tools;
using IronForge.Application.Products.DTOs;
using IronForge.Application.Products.Tools.Products;
using IronForge.Shared.Authorization;

namespace IronForge.Application.Products.AgentTools;

public sealed class GetProductsAgentTool : AgentTool
{
    private readonly IGetProductsTool _tool;

    public GetProductsAgentTool(IGetProductsTool tool)
    {
        _tool = tool;
    }

    public override AgentToolDescriptor Descriptor { get; } =
        new()
        {
            Name = "get_products",
            Description = "Gets the products that the current user is authorized to view.",
            RequiredPermission = Permissions.ProductsRead,
            Module = PlatformModules.Products,
            RiskLevel = AgentRiskLevel.Low
        };

    protected override Delegate Handler => ExecuteAsync;

    private async Task<List<ProductDto>> ExecuteAsync()
    {
        var result = await _tool.ExecuteAsync();

        if (!result.Success)
        {
            throw new InvalidOperationException(
                result.ErrorMessage ?? "Unable to retrieve products.");
        }

        return result.Products;
    }
}
