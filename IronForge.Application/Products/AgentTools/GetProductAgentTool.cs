using IronForge.Application.Agents.Governance;
using IronForge.Application.Agents.Tools;
using IronForge.Application.Products.DTOs;
using IronForge.Application.Products.Tools.Products;
using IronForge.Shared.Authorization;

namespace IronForge.Application.Products.AgentTools;

public sealed class GetProductAgentTool : AgentTool
{
    private readonly IGetProductTool _tool;

    public GetProductAgentTool(IGetProductTool tool)
    {
        _tool = tool;
    }

    public override AgentToolDescriptor Descriptor { get; } =
        new()
        {
            Name = "get_product",
            Description = "Gets a specific product by its ID if the current user is authorized to view it.",
            RequiredPermission = Permissions.ProductsRead,
            RiskLevel = AgentRiskLevel.Low
        };

    protected override Delegate Handler => ExecuteAsync;

    private async Task<object> ExecuteAsync(int id)
    {
        var result = await _tool.ExecuteAsync(id);

        if (!result.Success)
        {
            return new
            {
                Success = false,
                Product = (ProductDto?)null,
                Error = result.ErrorMessage ?? $"Product {id} could not be retrieved.",
                StatusCode = result.StatusCode
            };
        }

        return new
        {
            Success = true,
            Product = result.Product,
            Error = (string?)null,
            StatusCode = (int?)null
        };
    }
}
