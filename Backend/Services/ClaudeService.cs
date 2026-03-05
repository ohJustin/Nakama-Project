using System.Text;
using System.Text.Json;
using ClaudeForUnity.Backend.Models;
using ClaudeForUnity.Backend.Utils;

namespace ClaudeForUnity.Backend.Services;

public class ClaudeService
{
    private readonly HttpClient _httpClient;

    public ClaudeService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string> SendAsync(PromptRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ApiKey))
            throw new InvalidOperationException("ApiKey is required");

        var messages = new List<object>();

        foreach (var msg in request.ConversationHistory)
            messages.Add(new { role = msg.Role, content = msg.Content });

        messages.Add(new
        {
            role = "user",
            content = $"{request.UserMessage}\n\n---\n{request.SceneContext}"
        });

        var body = new
        {
            model = "claude-sonnet-4-6",
            max_tokens = 4096,
            system = PromptBuilder.BuildSystemPrompt(),
            messages
        };


        /*
        TODO: Support multiple models
             - Issue here. We should be supporting multiple interchangable models depending on the user's preference.
             - We should allow users to select from a list of available models (e.g. "claude-sonnet-4-6", "claude-sonnet-3", etc.) and pass that selection in the API request.
             - This will require updating the PromptRequest model to include a Model property, and updating the UI to allow users to select their preferred model.

        */
        var json = JsonSerializer.Serialize(body);
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        httpRequest.Headers.Add("x-api-key", request.ApiKey);
        httpRequest.Headers.Add("anthropic-version", "2023-06-01");

        var response = await _httpClient.SendAsync(httpRequest);
        response.EnsureSuccessStatusCode();

        var responseBody = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(responseBody);
        return doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? "";
    }
}
