using ClaudeForUnity.Backend.Domain.Interfaces;
using ClaudeForUnity.Backend.Domain.Models;

namespace ClaudeForUnity.Backend.Application;

public class PromptOrchestrator
{
    private readonly IEnumerable<IAiProvider> _providers;

    public PromptOrchestrator(IEnumerable<IAiProvider> providers)
    {
        _providers = providers;
    }

    public async Task<ClaudeResponse> HandleAsync(PromptRequest request)
    {
        // Match provider by model string prefix: "claude-*" → ClaudeProvider, "gpt-*" → OpenAiProvider, etc.
        var provider = _providers.FirstOrDefault(p =>
            request.Model.StartsWith(p.ProviderId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"No provider registered for model: {request.Model}");

        var systemPrompt                      = PromptBuilder.BuildSystemPrompt();
        IEnumerable<ToolDefinition> tools     = UnityToolDefinitions.GetAll();

        var providerResponse = await provider.SendAsync(request, systemPrompt, tools);
        return ToolUseParser.Parse(providerResponse);
    }
}
