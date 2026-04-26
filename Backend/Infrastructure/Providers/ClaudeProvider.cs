using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ClaudeForUnity.Backend.Domain.Interfaces;
using ClaudeForUnity.Backend.Domain.Models;

namespace ClaudeForUnity.Backend.Infrastructure.Providers;

public class ClaudeProvider : IAiProvider
{
    private readonly HttpClient _httpClient;
    public string ProviderId => "claude";

    public ClaudeProvider(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<AiProviderResponse> SendAsync(
        PromptRequest request,
        string systemPrompt,
        IEnumerable<ToolDefinition> tools)
    {
        var messages = new List<object>();

        foreach (var msg in request.ConversationHistory)
            messages.Add(new { role = msg.Role, content = msg.Content });

        messages.Add(new
        {
            role    = "user",
            content = $"{request.UserMessage}\n\n---\n{request.SceneContext}"
        });

        var body = new
        {
            model      = request.Model,
            max_tokens = 4096,
            system     = systemPrompt,
            tools      = ToAnthropicTools(tools),
            messages
        };

        var json = JsonSerializer.Serialize(body);
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

        httpRequest.Headers.Add("x-api-key", request.ApiKey);
        httpRequest.Headers.Add("anthropic-version", "2023-06-01");

        var response = await _httpClient.SendAsync(httpRequest);
        response.EnsureSuccessStatusCode();

        return ParseResponse(await response.Content.ReadAsStringAsync());
    }

    // ── Anthropic translation ────────────────────────────────────────────────

    private static IEnumerable<object> ToAnthropicTools(IEnumerable<ToolDefinition> tools) =>
        tools.Select(t => new
        {
            name         = t.Name,
            description  = t.Description,
            input_schema = new
            {
                type       = "object",
                properties = t.Parameters.ToDictionary(p => p.Name, BuildAnthropicProperty),
                required   = t.Parameters.Where(p => p.Required).Select(p => p.Name).ToArray()
            }
        });

    private static JsonObject BuildAnthropicProperty(ToolParameter p)
    {
        var prop = new JsonObject
        {
            ["type"]        = p.Type,
            ["description"] = p.Description
        };

        if (p.Type == "array" && p.ArrayItemType != null)
            prop["items"] = new JsonObject { ["type"] = p.ArrayItemType };

        if (p.EnumValues != null)
            prop["enum"] = new JsonArray(p.EnumValues.Select(v => JsonValue.Create(v)).ToArray<JsonNode?>());

        return prop;
    }

    // ── Response parsing ─────────────────────────────────────────────────────

    private static AiProviderResponse ParseResponse(string responseBody)
    {
        var result = new AiProviderResponse();
        var doc    = JsonDocument.Parse(responseBody);

        foreach (var block in doc.RootElement.GetProperty("content").EnumerateArray())
        {
            var type = block.GetProperty("type").GetString();

            if (type == "text")
            {
                result.Reply = block.GetProperty("text").GetString() ?? "";
            }
            else if (type == "tool_use")
            {
                var toolName  = block.GetProperty("name").GetString() ?? "";
                var inputJson = block.GetProperty("input").GetRawText();

                result.ToolCalls.Add(new ToolInvocation
                {
                    ToolName = toolName,
                    Input    = JsonNode.Parse(inputJson)?.AsObject() ?? new JsonObject()
                });
            }
        }

        return result;
    }
}
