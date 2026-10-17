using System.Text.Json;
using IronForge.Application.Tests.Tenants;

namespace IronForge.Application.Tests.Agents;

public class AgentToolSchemaTests
{
    [Fact]
    public void ToolSchemas_EmitNumericBoundsAsNumbers()
    {
        foreach (var tool in SubscriptionRulesTests.Catalogue())
        {
            var schema = tool.CreateFunction().JsonSchema;

            foreach (var (keyword, value) in NumericBounds(schema))
            {
                Assert.True(
                    value.ValueKind == JsonValueKind.Number,
                    $"{tool.Descriptor.Name}: '{keyword}' is {value.ValueKind}, not a number.");
            }
        }
    }

    [Fact]
    public void LogWorkoutSchema_KeepsTheDecimalWeightRange()
    {
        var tool = SubscriptionRulesTests.Catalogue().Single(x => x.Descriptor.Name == "log_workout");

        var maximums = NumericBounds(tool.CreateFunction().JsonSchema)
            .Where(x => x.Keyword == "maximum")
            .Select(x => x.Value.GetDecimal());

        Assert.Contains(1000m, maximums);
    }

    private static IEnumerable<(string Keyword, JsonElement Value)> NumericBounds(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name is "minimum" or "maximum" or "exclusiveMinimum" or "exclusiveMaximum")
                    yield return (property.Name, property.Value);

                foreach (var nested in NumericBounds(property.Value))
                    yield return nested;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var nested in NumericBounds(item))
                    yield return nested;
            }
        }
    }
}
