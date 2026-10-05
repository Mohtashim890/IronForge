using IronForge.Application.Agents.Governance;
using IronForge.Application.Agents.Models;
using IronForge.Application.Products.DTOs;
using IronForge.Application.Products.Tools.Products;
using IronForge.Shared.Authorization;
using Microsoft.Extensions.AI;
using System.Diagnostics;

namespace IronForge.Application.Agents.Tools;

public static class ProductAIFunctions
{
    public static AIFunction CreateGetProductsTool(
        IGetProductsTool tool,
        AgentIdentity agent,
       IAgentGovernanceService governanceService,
        AgentToolPolicy policy)
    {
        return AIFunctionFactory.Create(
            async () =>
            {
                if (agent is null)
                {
                    throw new InvalidOperationException(
                        "No agent identity is available.");
                }

                var context =
                    new AgentGovernanceContext
                    {
                        ToolName = policy.ToolName
                    };

                var decision = governanceService.Evaluate(agent, policy, context);

                Activity.Current?.SetTag("ironforge.tool.governance_decision",
                    decision.Decision.ToString());

                if (decision.Decision ==
                    AgentGovernanceDecisionType.Deny)
                {
                    throw new UnauthorizedAccessException(
                        decision.Reason);
                }

                var result =
                    await tool.ExecuteAsync();

                if (!result.Success)
                {
                    throw new InvalidOperationException(
                        result.ErrorMessage ??
                        "Unable to retrieve products.");
                }

                return result.Products;
            },
            name: policy.ToolName,
            description:
                "Gets the products that the current user is authorized to view.");
    }

    public static AIFunction CreateGetProductTool(
    IGetProductTool tool,
    AgentIdentity agent,
    IAgentGovernanceService governanceService,
    AgentToolPolicy policy)
    {
        return AIFunctionFactory.Create(
       async (int id) =>
       {
           if (agent is null)
           {
               throw new InvalidOperationException(
                   "No agent identity is available.");
           }

           var context =
            new AgentGovernanceContext
            {
                ToolName = policy.ToolName,
                Arguments = new Dictionary<string, object?>
                {
                    ["id"] = id
                }
            };

           var decision =
            governanceService.Evaluate(agent, policy, context);

           Activity.Current?.SetTag("ironforge.tool.governance_decision",
                   decision.Decision.ToString());

           if (decision.Decision ==
               AgentGovernanceDecisionType.Deny)
           {
               throw new UnauthorizedAccessException(
                   decision.Reason);
           }

           var result =
               await tool.ExecuteAsync(id);

           if (!result.Success)
           {
               return new
               {
                   Success = false,
                   Product = (ProductDto?)null,
                   Error = result.ErrorMessage ??
                           $"Product {id} could not be retrieved.",
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
       },
       name: policy.ToolName,
       description:
           "Gets a specific product by its ID if the current user is authorized to view it.");
    }

    public static AIFunction CreateUpdateProductTool(
    IUpdateProductTool tool,
    AgentIdentity agent,
    IAgentGovernanceService governanceService,
    AgentToolPolicy policy)
    {
        var function =
            AIFunctionFactory.Create(
                async (
                    int id,
                    string name,
                    decimal price) =>
                {
                    if (agent is null)
                    {
                        throw new InvalidOperationException(
                            "No agent identity is available.");
                    }

                    var context =
                        new AgentGovernanceContext
                        {
                            ToolName = policy.ToolName,
                            Arguments = new Dictionary<string, object?>
                            {
                                ["id"] = id,
                                ["name"] = name,
                                ["price"] = price
                            }
                        };

                    var decision =
            governanceService.Evaluate(agent, policy, context);

                    Activity.Current?.SetTag("ironforge.tool.governance_decision",
                         decision.Decision.ToString());

                    if (decision.Decision ==
                        AgentGovernanceDecisionType.Deny)
                    {
                        throw new UnauthorizedAccessException(
                            decision.Reason);
                    }

                    var dto = new ProductWriteDto
                    {
                        Name = name,
                        Price = price
                    };

                    var result =
                        await tool.ExecuteAsync(id, dto);

                    return new
                    {
                        Success = result.Success,
                        Error = result.ErrorMessage,
                        StatusCode = result.StatusCode
                    };
                },
                name: policy.ToolName,
                description:
                    "Updates an existing IronForge product.");

        return new ApprovalRequiredAIFunction(function);
    }

    public static AIFunction CreateCreateProductTool(
    ICreateProductTool tool,
    AgentIdentity agent,
    IAgentGovernanceService governanceService,
    AgentToolPolicy policy)
    {
        var function =
            AIFunctionFactory.Create(
                async (
                    string name,
                    decimal price) =>
                {
                    if (agent is null)
                    {
                        throw new InvalidOperationException(
                            "No agent identity is available.");
                    }
                    var context =
                        new AgentGovernanceContext
                        {
                            ToolName = policy.ToolName,
                            Arguments = new Dictionary<string, object?>
                            {
                                ["name"] = name,
                                ["price"] = price
                            }
                        };

                    var decision =
            governanceService.Evaluate(agent, policy, context);

                    Activity.Current?.SetTag("ironforge.tool.governance_decision",
               decision.Decision.ToString());

                    if (decision.Decision ==
                        AgentGovernanceDecisionType.Deny)
                    {
                        throw new UnauthorizedAccessException(
                            decision.Reason);
                    }

                    var dto = new ProductWriteDto
                    {
                        Name = name,
                        Price = price
                    };

                    var result =
                        await tool.ExecuteAsync(dto);

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
                },
                name: policy.ToolName,
                description:
                    "Creates a new product in IronForge.");

        return new ApprovalRequiredAIFunction(function);
    }

    public static AIFunction CreateDeleteProductTool(
    IDeleteProductTool tool,
    AgentIdentity agent,
    IAgentGovernanceService governanceService,
    AgentToolPolicy policy)
    {
        var function =
            AIFunctionFactory.Create(
                async (int id) =>
                {
                    if (agent is null)
                    {
                        throw new InvalidOperationException(
                            "No agent identity is available.");
                    }

                    var context =
                        new AgentGovernanceContext
                        {
                            ToolName = policy.ToolName,
                            Arguments = new Dictionary<string, object?>
                            {
                                ["id"] = id
                            }
                        };

                    var decision =
            governanceService.Evaluate(agent, policy, context);

                    Activity.Current?.SetTag("ironforge.tool.governance_decision",
                       decision.Decision.ToString());

                    if (decision.Decision ==
                        AgentGovernanceDecisionType.Deny)
                    {
                        throw new UnauthorizedAccessException(
                            decision.Reason);
                    }

                    var result =
                        await tool.ExecuteAsync(id);

                    return new
                    {
                        Success = result.Success,
                        Error = result.ErrorMessage,
                        StatusCode = result.StatusCode
                    };
                },
                name: policy.ToolName,
                description:
                    "Permanently deletes an existing IronForge product.");

        return new ApprovalRequiredAIFunction(function);
    }
}