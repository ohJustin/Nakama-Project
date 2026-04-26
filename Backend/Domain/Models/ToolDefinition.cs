namespace ClaudeForUnity.Backend.Domain.Models;

/// <summary>
/// Provider-agnostic description of a tool the AI can call.
/// Each IAiProvider translates this into its own vendor format internally:
///   Anthropic  → input_schema
///   Gemini     → parameters with uppercase types inside functionDeclarations
///   OpenAI     → { type: "function", function: { parameters: ... } }
/// </summary>
public class ToolDefinition
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<ToolParameter> Parameters { get; set; } = new();
}

public class ToolParameter
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>JSON Schema type: "string", "number", "boolean", "array"</summary>
    public string Type { get; set; } = "string";

    public bool Required { get; set; }

    /// <summary>For string parameters — restricts to an allowed set of values.</summary>
    public string[]? EnumValues { get; set; }

    /// <summary>For array parameters — the type of each item ("string" or "number").</summary>
    public string? ArrayItemType { get; set; }
}
