using IronForge.Shared.Authorization;

namespace IronForge.Application.Agents.Governance;

public static class ProductAgentToolPolicies
{
    public static readonly AgentToolPolicy GetProducts =
        new()
        {
            ToolName = "get_products",
            RequiredPermission = Permissions.ProductsRead,
            RiskLevel = AgentRiskLevel.Low,
            RequiresApproval = false
        };

    public static readonly AgentToolPolicy GetProduct =
        new()
        {
            ToolName = "get_product",
            RequiredPermission = Permissions.ProductsRead,
            RiskLevel = AgentRiskLevel.Low,
            RequiresApproval = false
        };

    public static readonly AgentToolPolicy CreateProduct =
        new()
        {
            ToolName = "create_product",
            RequiredPermission = Permissions.ProductsCreate,
            RiskLevel = AgentRiskLevel.Medium,
            RequiresApproval = true,
            MaximumAmount = 10000
        };

    public static readonly AgentToolPolicy UpdateProduct =
        new()
        {
            ToolName = "update_product",
            RequiredPermission = Permissions.ProductsUpdate,
            RiskLevel = AgentRiskLevel.Medium,
            RequiresApproval = true
        };

    public static readonly AgentToolPolicy DeleteProduct =
        new()
        {
            ToolName = "delete_product",
            RequiredPermission = Permissions.ProductsDelete,
            RiskLevel = AgentRiskLevel.High,
            RequiresApproval = true
        };

    public static AgentToolPolicy? GetByToolName(
        string toolName)
    {
        return toolName switch
        {
            "get_products" =>
                GetProducts,

            "get_product" =>
                GetProduct,

            "create_product" =>
                CreateProduct,

            "update_product" =>
                UpdateProduct,

            "delete_product" =>
                DeleteProduct,

            _ => null
        };
    }
}