using IronForge.Shared.Subscriptions;
using IronForge.Application.Agents.Governance;
using IronForge.Application.Agents.Tools;
using IronForge.Application.Products.DTOs;
using IronForge.Application.Products.Tools.Products;
using IronForge.Shared.Authorization;

namespace IronForge.Application.Products.AgentTools;

public sealed class CreateProductAgentTool : AgentTool
{
    private readonly ICreateProductTool _tool;

    public CreateProductAgentTool(ICreateProductTool tool)
    {
        _tool = tool;
    }

    public override AgentToolDescriptor Descriptor { get; } =
        new()
        {
            Name = "create_product",
            Description = "Creates a new product in IronForge.",
            RequiredPermission = Permissions.ProductsCreate,
            Module = PlatformModules.Products,
            RiskLevel = AgentRiskLevel.Medium,
            RequiresApproval = true
        };

    protected override Delegate Handler => ExecuteAsync;

    private async Task<object> ExecuteAsync(string name, decimal price)
    {
        var result = await _tool.ExecuteAsync(
            new ProductWriteDto
            {
                Name = name,
                Price = price
            });

        if (!result.Success)
        {
            return new
            {
                Success = false,
                Product = (ProductDto?)null,
                Error = result.ErrorMessage,
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
