namespace ClaudeForUnity.Backend.Models;

public class PromptRequest
{
    public string ApiKey { get; set; } = "";
    public string UserMessage { get; set; } = "";
    public string SceneContext { get; set; } = "";
    public List<ConversationMessage> ConversationHistory { get; set; } = new();
}
