namespace ClaudeForUnity.Backend.Domain.Models;

/// <summary>
/// The outbound response shape returned to the Unity plugin.
/// This shape is intentionally stable — the plugin never needs to change
/// when backend internals evolve. ToolUseParser maps AiProviderResponse → ClaudeResponse.
/// </summary>
public class ClaudeResponse
{
    public string Reply { get; set; } = "";
    public List<UnityAction> Actions { get; set; } = new();
    public Dictionary<string, string> Scripts { get; set; } = new();
}
