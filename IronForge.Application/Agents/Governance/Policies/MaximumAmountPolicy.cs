using System.Globalization;
using IronForge.Application.Agents.Governance;
using IronForge.Application.Agents.Models;

namespace IronForge.Application.Agents.Governance.Policies;

/// <summary>
/// Denies a call whose monetary argument exceeds the tool's maximum.
/// The amount is read from the first argument named in
/// <see cref="AmountArgumentNames"/>.
/// </summary>
public class MaximumAmountPolicy : IAgentPolicy
{
    public static readonly IReadOnlyList<string> AmountArgumentNames = ["amount", "price"];

    public ValueTask<AgentGovernanceDecision?> EvaluateAsync(
        AgentIdentity agent,
        AgentToolPolicy toolPolicy,
        AgentGovernanceContext context,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(Evaluate(toolPolicy, context));
    }

    private static AgentGovernanceDecision? Evaluate(
        AgentToolPolicy toolPolicy,
        AgentGovernanceContext context)
    {
        if (!toolPolicy.MaximumAmount.HasValue)
        {
            return null;
        }

        object? priceValue = null;
        var found = false;

        foreach (var name in AmountArgumentNames)
        {
            if (context.Arguments.TryGetValue(name, out priceValue))
            {
                found = true;
                break;
            }
        }

        if (!found)
        {
            return new AgentGovernanceDecision
            {
                Decision =
                    AgentGovernanceDecisionType.Deny,

                Reason =
                    "An amount is required for this operation.",

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
                    "The requested amount could not be validated.",

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
                    $"The requested amount {price} exceeds " +
                    $"the maximum allowed amount " +
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