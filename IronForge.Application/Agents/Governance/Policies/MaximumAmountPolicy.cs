using System.Globalization;
using IronForge.Application.Agents.Governance;
using IronForge.Application.Agents.Models;

namespace IronForge.Application.Agents.Governance.Policies;

public class MaximumAmountPolicy : IAgentPolicy
{
    public AgentGovernanceDecision? Evaluate(
        AgentIdentity agent,
        AgentToolPolicy toolPolicy,
        AgentGovernanceContext context)
    {
        if (!toolPolicy.MaximumAmount.HasValue)
        {
            return null;
        }

        if (!context.Arguments.TryGetValue(
                "price",
                out var priceValue))
        {
            return new AgentGovernanceDecision
            {
                Decision =
                    AgentGovernanceDecisionType.Deny,

                Reason =
                    "A price is required for this operation.",

                RiskLevel =
                    toolPolicy.RiskLevel,

                RequiresApproval =
                    toolPolicy.RequiresApproval
            };
        }

        if (!decimal.TryParse(
                Convert.ToString(
                    priceValue,
                    CultureInfo.InvariantCulture),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out var price))
        {
            return new AgentGovernanceDecision
            {
                Decision =
                    AgentGovernanceDecisionType.Deny,

                Reason =
                    "The requested price could not be validated.",

                RiskLevel =
                    toolPolicy.RiskLevel,

                RequiresApproval =
                    toolPolicy.RequiresApproval
            };
        }

        if (price > toolPolicy.MaximumAmount.Value)
        {
            return new AgentGovernanceDecision
            {
                Decision =
                    AgentGovernanceDecisionType.Deny,

                Reason =
                    $"The requested price {price} exceeds " +
                    $"the maximum allowed price " +
                    $"{toolPolicy.MaximumAmount.Value}.",

                RiskLevel =
                    toolPolicy.RiskLevel,

                RequiresApproval =
                    toolPolicy.RequiresApproval
            };
        }

        return null;
    }
}