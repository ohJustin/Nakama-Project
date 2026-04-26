using System.Text.Json.Nodes;

namespace ClaudeForUnity.Backend.Domain.Models;

/// <summary>
/// Normalized response returned by every IAiProvider implementation.
/// Provider-specific shapes (Anthropic content blocks, OpenAI tool_calls, Gemini functionCall parts)
/// are mapped into this common structure inside the provider — nothing above
/// Infrastructure ever sees a vendor-specific format.
/// </summary>
public class AiProviderResponse
{
    public string Reply { get; set; } = "";
    public List<ToolInvocation> ToolCalls { get; set; } = new();
}

public class ToolInvocation
{
    public string ToolName { get; set; } = "";
    public JsonObject Input { get; set; } = new();
}
