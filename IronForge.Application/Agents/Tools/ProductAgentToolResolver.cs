using IronForge.Application.Agents.Governance;
using IronForge.Application.Agents.Models;
using IronForge.Application.Products.Tools.Products;
using Microsoft.Extensions.AI;

namespace IronForge.Application.Agents.Tools;

public class ProductAgentToolResolver : IAgentToolResolver
{
    private readonly IGetProductsTool _getProductsTool;
    private readonly IGetProductTool _getProductTool;
    private readonly ICreateProductTool _createProductTool;
    private readonly IUpdateProductTool _updateProductTool;
    private readonly IDeleteProductTool _deleteProductTool;
    private readonly IAgentGovernanceService _agentGovernanceService;

    public ProductAgentToolResolver(
        IGetProductsTool getProductsTool,
        IGetProductTool getProductTool,
        ICreateProductTool createProductTool,
        IUpdateProductTool updateProductTool,
        IDeleteProductTool deleteProductTool,
        IAgentGovernanceService agentGovernanceService)
    {
        _getProductsTool = getProductsTool;
        _getProductTool = getProductTool;
        _createProductTool = createProductTool;
        _updateProductTool = updateProductTool;
        _deleteProductTool = deleteProductTool;
        _agentGovernanceService = agentGovernanceService;
    }

    public AIFunction Resolve(
        string toolName,
        AgentIdentity agent)
    {
        return toolName switch
        {
            "get_products" =>
                ProductAIFunctions.CreateGetProductsTool(
                    _getProductsTool,
                    agent,
                    _agentGovernanceService,
                    ProductAgentToolPolicies.GetProducts),

            "get_product" =>
                ProductAIFunctions.CreateGetProductTool(
                    _getProductTool,
                    agent,
                    _agentGovernanceService,
                    ProductAgentToolPolicies.GetProduct),

            "create_product" =>
                ProductAIFunctions.CreateCreateProductTool(
                    _createProductTool,
                    agent,
                    _agentGovernanceService,
                    ProductAgentToolPolicies.CreateProduct),

            "update_product" =>
                ProductAIFunctions.CreateUpdateProductTool(
                    _updateProductTool,
                    agent,
                    _agentGovernanceService,
                    ProductAgentToolPolicies.UpdateProduct),

            "delete_product" =>
                ProductAIFunctions.CreateDeleteProductTool(
                    _deleteProductTool,
                    agent,
                    _agentGovernanceService,
                    ProductAgentToolPolicies.DeleteProduct),

            _ => throw new InvalidOperationException(
                $"Unknown agent tool '{toolName}'.")
        };
    }
}