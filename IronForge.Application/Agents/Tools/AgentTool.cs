using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;

namespace IronForge.Application.Agents.Tools;

/// <summary>
/// Base class for catalogue tools. A tool supplies its descriptor and a
/// handler method; the handler's parameters become the JSON schema the LLM
/// fills in (DI services and CancellationToken are not exposed).
/// </summary>
public abstract class AgentTool : IAgentTool
{
    private static readonly string[] NumericBoundKeywords =
        ["minimum", "maximum", "exclusiveMinimum", "exclusiveMaximum"];

    private static readonly AIJsonSchemaCreateOptions SchemaOptions =
        AIJsonSchemaCreateOptions.Default with { TransformSchemaNode = NormalizeNumericBounds };

    public abstract AgentToolDescriptor Descriptor { get; }

    /// <summary>The method the tool runs, e.g. <c>ExecuteAsync</c>.</summary>
    protected abstract Delegate Handler { get; }

    public AIFunction CreateFunction()
    {
        return AIFunctionFactory.Create(
            Handler,
            new AIFunctionFactoryOptions
            {
                Name = Descriptor.Name,
                Description = Descriptor.Description,
                JsonSchemaCreateOptions = SchemaOptions
            });
    }

    /// <summary>
    /// <c>[Range(typeof(decimal), "0", "1000")]</c> keeps its bounds as strings,
    /// which the schema exporter emits as <c>"maximum": "1000"</c>. OpenAI rejects
    /// such a schema, so string bounds are rewritten as JSON numbers.
    /// </summary>
    private static JsonNode NormalizeNumericBounds(AIJsonSchemaCreateContext context, JsonNode schema)
    {
        if (schema is not JsonObject obj)
            return schema;

        foreach (var keyword in NumericBoundKeywords)
        {
            if (obj[keyword] is JsonValue value &&
                value.TryGetValue(out string? text) &&
                decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
            {
                obj[keyword] = number;
            }
        }

        return schema;
    }
}
