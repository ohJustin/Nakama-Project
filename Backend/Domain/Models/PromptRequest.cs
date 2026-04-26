namespace ClaudeForUnity.Backend.Domain.Models;

public class PromptRequest
{
    public string UserMessage { get; set; } = "";
    public string SceneContext { get; set; } = "";
    public List<ConversationMessage> ConversationHistory { get; set; } = new();

    /// <summary>Model string sent from the Unity plugin (e.g. "claude-sonnet-4-6", "gpt-4o", "gemini-1.5-pro").</summary>
    public string Model { get; set; } = "claude-sonnet-4-6";

    /// <summary>User's own API key — sent over HTTPS, never stored server-side.</summary>
    public string ApiKey { get; set; } = "";
}

public class ConversationMessage
{
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
}
