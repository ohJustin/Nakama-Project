using ClaudeForUnity.Backend.Domain.Interfaces;
using ClaudeForUnity.Backend.Domain.Models;

namespace ClaudeForUnity.Backend.Infrastructure.Providers;

/// <summary>
/// OpenAI (GPT) implementation of IAiProvider. Stubbed for Phase 5.
///
/// Key translation differences vs. Anthropic (ClaudeProvider):
///   - Each tool wrapped in: { "type": "function", "function": { "name", "description", "parameters" } }
///   - Property schema uses "parameters" (same JSON Schema shape as Anthropic's input_schema)
///   - Response: tool_calls[] on the message object, arguments is a JSON *string* (deserialize it)
///   - Auth: Authorization: Bearer {apiKey} header
///
/// TODO (Phase 5):
///   - Implement ToOpenAiTools() following the ToAnthropicTools() pattern in ClaudeProvider
///   - Implement ParseResponse() — deserialize tool_calls[n].function.arguments (JSON string → JsonObject)
///   - Wire up API endpoint and auth
/// </summary>
public class OpenAiProvider : IAiProvider
{
    private readonly HttpClient _httpClient;
    public string ProviderId => "gpt";

    public OpenAiProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public Task<AiProviderResponse> SendAsync(
        PromptRequest request,
        string systemPrompt,
        IEnumerable<ToolDefinition> tools)
    {
        throw new NotImplementedException(
            "OpenAiProvider is not yet implemented. " +
            "See TODO comments above for implementation guidance.");
    }
}
