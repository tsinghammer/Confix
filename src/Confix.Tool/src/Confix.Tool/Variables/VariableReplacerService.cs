using System.Text.Json;
using System.Text.Json.Nodes;
using Json.Schema;

namespace ConfiX.Variables;

public sealed class VariableReplacerService : IVariableReplacerService
{
    private readonly IVariableResolver _variableResolver;

    public VariableReplacerService(IVariableResolver variableResolver)
    {
        _variableResolver = variableResolver;
    }

    public async Task<JsonNode?> RewriteAsync(JsonNode? node, CancellationToken cancellationToken)
    {
        if (node is null)
        {
            return null;
        }
        var variables = GetVariables(node).ToArray();
        var resolved = await _variableResolver.ResolveVariables(variables, cancellationToken);

        return new JsonVariableRewriter().Rewrite(node, new(resolved));
    }

    private static IEnumerable<VariablePath> GetVariables(JsonNode node)
    {
        foreach (var value in JsonParser.ParseNode(node).Values)
        {
            if (
                value?.GetSchemaValueType() == SchemaValueType.String
                && VariablePath.TryParse(value.ToString(), out var parsed)
                && parsed.HasValue)
            {
                yield return parsed.Value;
            }
        }
    }
}