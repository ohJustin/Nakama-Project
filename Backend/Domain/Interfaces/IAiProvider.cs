using ClaudeForUnity.Backend.Domain.Models;

namespace ClaudeForUnity.Backend.Domain.Interfaces;

public interface IAiProvider
{
    /// Identifies this provider — used by PromptOrchestrator to match the model string prefix
    string ProviderId { get; }  // "claude", "gpt", "gemini"

    Task<AiProviderResponse> SendAsync(
        PromptRequest request,
        string systemPrompt,
        IEnumerable<ToolDefinition> tools);
}
