using ClaudeForUnity.Backend.Domain.Interfaces;
using ClaudeForUnity.Backend.Domain.Models;

namespace ClaudeForUnity.Backend.Infrastructure.Providers;

/// <summary>
/// Google Gemini implementation of IAiProvider. Stubbed for Phase 5.
///
/// Key translation differences vs. Anthropic (ClaudeProvider):
///   - Tools wrapped in: { "tools": [{ "functionDeclarations": [...] }] }
///   - Type names are uppercase: STRING, NUMBER, ARRAY, OBJECT
///   - Response uses "functionCall" content parts instead of "tool_use" blocks
///   - Auth: query param (?key=apiKey) or Authorization: Bearer header
///
/// TODO (Phase 5):
///   - Implement ToGeminiTools() following the ToAnthropicTools() pattern in ClaudeProvider
///   - Implement ParseResponse() for Gemini's functionCall response format
///   - Wire up API endpoint and auth
/// </summary>
public class GeminiProvider : IAiProvider
{
    private readonly HttpClient _httpClient;
    public string ProviderId => "gemini";

    public GeminiProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public Task<AiProviderResponse> SendAsync(
        PromptRequest request,
        string systemPrompt,
        IEnumerable<ToolDefinition> tools)
    {
        throw new NotImplementedException("TODO: GeminiProvider is not yet implemented.");
    }
}
