# Claude for Unity — Full Design Specification

> Drop this file in your project root and reference it when starting Claude Code.
> Prime Claude Code with: "Read CLAUDE_FOR_UNITY_DESIGN.md and use it as the full design spec for this project. We are starting on Phase 1 — scaffold the .NET backend."

---

## What This Tool Is

A Unity Editor plugin that connects to a **hosted** .NET backend. Developers install the plugin, enter their AI provider API key in the settings panel, and start chatting — no local server to run, no extra setup. The plugin works with Claude, GPT, or Gemini depending on the developer's choice. Claude can:

- Create GameObjects in the scene from text descriptions
- Write complete, immediately testable C# Unity scripts
- Modify existing scene objects (transform, scale, rotation)
- Assign and modify materials
- Give design advice grounded in the developer's actual scene context
- Hold multi-turn conversations with memory of prior messages

Everything Claude does in the scene is registered with Unity's Undo system so Ctrl+Z always works.

---

## How It Works (System Flow)

```
Developer selects AI model + enters API key in plugin Settings panel (one time)
        ↓
Developer types prompt in Unity chat window
        ↓
ClaudeEditorWindow.cs collects prompt + conversation history
        ↓
SceneContextBuilder.cs serializes active scene + selected GameObject
        ↓
ClaudeApiClient.cs sends POST to https://[hosted-backend]/prompt
  (includes: userMessage, sceneContext, history, model, apiKey over HTTPS)
        ↓
PromptOrchestrator.cs resolves the correct IAiProvider from the model string
        ↓
IAiProvider (Claude/GPT/Gemini) calls the selected AI vendor API using the user's key
        ↓
AI responds with tool_use content blocks (typed, structured — no text parsing)
        ↓
ToolUseParser.cs extracts text blocks → Reply, tool_use blocks → Actions + Scripts
        ↓
Validated response returned to Unity plugin
        ↓
ActionExecutor.cs routes each action to the correct handler class
        ↓
Scene changes execute, .cs files are written, chat window updates
```

---

## Tool Use Format (Critical Design Decision)

Instead of asking the AI to format responses as a custom text structure, the backend uses the Anthropic API's native **tool_use** feature. Tools are defined once in `UnityToolDefinitions` as typed JSON schemas and passed on every request. Claude responds with structured `tool_use` content blocks — no regex, no parsing contracts.

### Request shape (tools array sent to Claude)

```json
{
  "model": "claude-sonnet-4-6",
  "system": "...",
  "tools": [
    {
      "name": "generate_script",
      "description": "Writes a complete, compilable C# Unity script to disk and optionally attaches it to a scene object after recompile.",
      "input_schema": {
        "type": "object",
        "properties": {
          "filename":  { "type": "string", "description": "Script filename without .cs extension" },
          "attach_to": { "type": "string", "description": "GameObject name to attach after recompile (optional)" },
          "code":      { "type": "string", "description": "Full compilable C# Unity script source code" }
        },
        "required": ["filename", "code"]
      }
    }
  ],
  "messages": [...]
}
```

### Response shape (what Claude returns)

```json
{
  "content": [
    {
      "type": "text",
      "text": "Here's a complete health system attached to your Player."
    },
    {
      "type": "tool_use",
      "name": "generate_script",
      "input": {
        "filename": "HealthSystem",
        "attach_to": "Player",
        "code": "using UnityEngine;\nusing System;\n\npublic class HealthSystem : MonoBehaviour\n{\n    public float maxHealth = 100f;\n    private float _currentHealth;\n    public event Action OnDeath;\n\n    void Start() { _currentHealth = maxHealth; }\n\n    public void TakeDamage(float amount)\n    {\n        _currentHealth -= amount;\n        if (_currentHealth <= 0) { _currentHealth = 0; OnDeath?.Invoke(); }\n    }\n\n    public float GetHealth() => _currentHealth;\n}"
      }
    }
  ]
}
```

`ToolUseParser` walks the `content` array: `type: "text"` blocks become `Reply`, `type: "tool_use"` blocks become `Actions`. For `generate_script`, the `code` parameter is extracted into `Scripts` — the Unity plugin's `GenerateScriptAction` sees no difference. Multiple tool calls in one response are fully supported.

---

## Backend Architecture (Layered)

The backend follows Clean Architecture: **Api → Application → Domain → Infrastructure**.
Dependencies only point inward — nothing in Domain or Application ever references Infrastructure.
External vendors (Anthropic, OpenAI, Google) are isolated entirely inside Infrastructure.

```
Backend/
  Api/             HTTP surface — Program.cs, endpoint mapping, request/response wiring
  Application/     Use cases and orchestration — PromptOrchestrator, PromptBuilder, UnityToolDefinitions, ToolUseParser
                   Provider-agnostic: typed tool schemas enforce action structure on any model
  Domain/          Core contracts and pure data — no external dependencies
    Interfaces/    IAiProvider — the only abstraction the rest of the app needs
    Models/        PromptRequest, ClaudeResponse, UnityAction — shared data shapes
  Infrastructure/  One class per AI vendor — handles HTTP, auth, and response extraction
    Providers/     ClaudeProvider, OpenAiProvider, GeminiProvider, ...
```

### IAiProvider — the key abstraction

```csharp
// Domain/Interfaces/IAiProvider.cs
public interface IAiProvider
{
    string ProviderId { get; }   // "claude", "openai", "gemini"
    Task<AiProviderResponse> SendAsync(PromptRequest request, string systemPrompt, IEnumerable<ToolDefinition> tools);
}
```

Each provider implements this one method. Everything above this layer (Application, Api) only
talks to `IAiProvider` — it never knows which vendor is being called.

### Provider selection flow

```
Unity sends: { userMessage, sceneContext, history, model: "gpt-4o" }
                          ↓
PromptOrchestrator resolves IAiProvider by model string
                          ↓
IAiProvider.SendAsync() → AiProviderResponse (vendor handles HTTP + auth, normalizes tool_use blocks)
                          ↓
ToolUseParser.Parse(response) → ClaudeResponse (same for all providers)
```

The typed tool schemas in `UnityToolDefinitions` enforce action structure on any model. `ToolUseParser` maps the result the same way regardless of which provider was called.

### Model field in PromptRequest

```csharp
public class PromptRequest
{
    public string UserMessage { get; set; } = "";
    public string SceneContext { get; set; } = "";
    public List<ConversationMessage> ConversationHistory { get; set; } = new();
    public string Model { get; set; } = "claude-sonnet-4-6";   // default
}
```

The Unity plugin sends the model string from whatever the developer selected in the GUI.
API keys for each provider live in `.env` — never in the plugin.

---

## Full Folder Structure

```
ClaudeForUnity/
├── Backend/                                   # .NET 8 Web API
│   ├── ClaudeForUnity.Backend.csproj
│   ├── Program.cs                             # Api layer — wiring only
│   ├── .env                                   # All provider API keys go here
│   ├── Domain/
│   │   ├── Interfaces/
│   │   │   └── IAiProvider.cs                 # Provider contract
│   │   └── Models/
│   │       ├── PromptRequest.cs               # Includes Model field
│   │       ├── ToolDefinition.cs              # Provider-agnostic tool schema (ToolDefinition + ToolParameter)
│   │       ├── AiProviderResponse.cs          # Normalized response + ToolInvocation list
│   │       ├── ClaudeResponse.cs              # Outbound shape to Unity plugin
│   │       └── UnityAction.cs
│   ├── Application/
│   │   ├── PromptOrchestrator.cs              # Resolves provider, passes tools, maps response
│   │   ├── PromptBuilder.cs                   # System prompt — role + rules only
│   │   ├── UnityToolDefinitions.cs            # Typed JSON schemas for all 5 actions
│   │   └── ToolUseParser.cs                   # Maps AiProviderResponse → ClaudeResponse
│   └── Infrastructure/
│       └── Providers/
│           ├── ClaudeProvider.cs              # Anthropic API: tool_use request + response parsing
│           ├── OpenAiProvider.cs              # OpenAI API implementation
│           └── GeminiProvider.cs             # Google Gemini implementation
│
└── Assets/
    └── ClaudeForUnity/
        ├── Editor/                            # All Unity Editor-only code
        │   ├── Core/
        │   │   ├── ClaudeEditorWindow.cs      # Dockable chat UI
        │   │   ├── ClaudeApiClient.cs         # HTTP calls to backend
        │   │   ├── ActionExecutor.cs          # Routes actions to handlers
        │   │   └── SceneContextBuilder.cs     # Serializes scene for context
        │   ├── Actions/
        │   │   ├── CreateObjectAction.cs
        │   │   ├── ModifyObjectAction.cs
        │   │   ├── GenerateScriptAction.cs
        │   │   ├── SetMaterialAction.cs
        │   │   └── SuggestDesignAction.cs
        │   ├── UI/
        │   │   ├── ChatMessageView.cs
        │   │   └── ClaudeStyles.cs
        │   ├── PendingAttachments.cs          # Handles post-recompile attach
        │   └── ClaudeEditorMenu.cs            # Adds "Claude AI" to toolbar
        ├── Runtime/                           # Reserved for future use
        └── README.md
```

---

## Backend Code

### `ClaudeForUnity.Backend.csproj`
```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="DotNetEnv" Version="3.0.0" />
  </ItemGroup>
</Project>
```

### `Domain/Models/PromptRequest.cs`
```csharp
namespace ClaudeForUnity.Backend.Domain.Models;

public class PromptRequest
{
    public string UserMessage { get; set; } = "";
    public string SceneContext { get; set; } = "";
    public List<ConversationMessage> ConversationHistory { get; set; } = new();
    public string Model { get; set; } = "claude-sonnet-4-6";  // user's selected model
    public string ApiKey { get; set; } = "";                   // user's own key, sent over HTTPS
}

public class ConversationMessage
{
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
}
```

> **Security note:** The API key is never stored server-side. It travels in the HTTPS request body,
> gets used for that one call, and is discarded. The backend is a stateless proxy — it never
> logs or persists keys.

### `Domain/Models/ToolDefinition.cs`
```csharp
namespace ClaudeForUnity.Backend.Domain.Models;

/// <summary>
/// Provider-agnostic description of a tool the AI can call.
/// Each IAiProvider translates this into its own vendor format internally —
/// Anthropic uses input_schema, Gemini uses parameters with uppercase types,
/// OpenAI wraps in { type: "function", function: { ... } }.
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
```

### `Domain/Models/AiProviderResponse.cs`
```csharp
using System.Text.Json.Nodes;

namespace ClaudeForUnity.Backend.Domain.Models;

/// <summary>
/// Normalized response returned by every IAiProvider implementation.
/// Provider-specific shapes (Anthropic content blocks, OpenAI tool_calls, etc.)
/// are mapped into this common structure inside the provider — nothing above
/// Infrastructure ever sees vendor-specific formats.
/// </summary>
public class AiProviderResponse
{
    public string Reply { get; set; } = "";
    public List<ToolInvocation> ToolCalls { get; set; } = new();
}

public class ToolInvocation
{
    public string ToolName { get; set; } = "";
    public JsonObject Input { get; set; } = new();
}
```

### `Domain/Models/ClaudeResponse.cs`
```csharp
namespace ClaudeForUnity.Backend.Domain.Models;

/// <summary>
/// The outbound response shape returned to the Unity plugin.
/// Shape is stable — the plugin never needs to change when backend internals evolve.
/// ToolUseParser maps AiProviderResponse → ClaudeResponse.
/// </summary>
public class ClaudeResponse
{
    public string Reply { get; set; } = "";
    public List<UnityAction> Actions { get; set; } = new();
    public Dictionary<string, string> Scripts { get; set; } = new();
}
```

### `Domain/Models/UnityAction.cs`
```csharp
using System.Text.Json.Nodes;

namespace ClaudeForUnity.Backend.Domain.Models;

public class UnityAction
{
    public string Action { get; set; } = "";
    public JsonObject? Data { get; set; }
}
```

### `Application/PromptBuilder.cs`
```csharp
namespace ClaudeForUnity.Backend.Application;

/// <summary>
/// Builds the system prompt. With tool_use, there is no need to engineer a custom
/// response format — available actions are defined as typed tool schemas in
/// UnityToolDefinitions and enforced by the API. The system prompt covers only
/// role identity, script quality rules, and general behavior guidelines.
/// </summary>
public static class PromptBuilder
{
    public static string BuildSystemPrompt() => """
        You are an expert Unity game developer AI assistant embedded inside the Unity Editor.

        ## SCRIPT WRITING RULES
        - Always write complete, compilable, immediately testable C# Unity scripts
        - Use MonoBehaviour unless the developer specifically asks for ScriptableObject or plain C#
        - Include using statements, proper access modifiers, and SerializeField where appropriate
        - Add brief comments on non-obvious logic
        - Never leave placeholder comments like "// implement this" — write the actual implementation

        ## RULES
        - Always include a helpful reply text even if no tool calls are needed
        - Only use components that exist in UnityEngine (Rigidbody, BoxCollider, AudioSource, etc.)
        - If the user asks a question with no scene changes needed, respond with text only — no tool calls
        - Be concise, practical, and think like a senior Unity developer
        """;
}
```

### `Application/ToolUseParser.cs`
```csharp
using ClaudeForUnity.Backend.Domain.Models;

namespace ClaudeForUnity.Backend.Application;

/// <summary>
/// Maps the normalized AiProviderResponse (from any IAiProvider) into the
/// ClaudeResponse shape that the Unity plugin consumes. No regex — the input
/// is already structured from the tool_use API response.
/// </summary>
public static class ToolUseParser
{
    public static ClaudeResponse Parse(AiProviderResponse response)
    {
        var result = new ClaudeResponse
        {
            Reply = response.Reply
        };

        foreach (var toolCall in response.ToolCalls)
        {
            result.Actions.Add(new UnityAction
            {
                Action = toolCall.ToolName,
                Data   = toolCall.Input
            });

            // generate_script carries code as a typed parameter — pull it into Scripts
            if (toolCall.ToolName == "generate_script")
            {
                var filename = toolCall.Input["filename"]?.GetValue<string>();
                var code     = toolCall.Input["code"]?.GetValue<string>();

                if (!string.IsNullOrEmpty(filename) && !string.IsNullOrEmpty(code))
                    result.Scripts[filename] = code;
            }
        }

        return result;
    }
}
```

### `Application/UnityToolDefinitions.cs`
```csharp
using ClaudeForUnity.Backend.Domain.Models;

namespace ClaudeForUnity.Backend.Application;

/// <summary>
/// The single source of truth for every action the AI can perform in Unity.
/// Returns provider-agnostic ToolDefinition objects — each IAiProvider translates
/// these into its own vendor format (Anthropic, Gemini, OpenAI) internally.
/// Adding a new action: add one entry here — no changes needed anywhere else.
/// </summary>
public static class UnityToolDefinitions
{
    public static IEnumerable<ToolDefinition> GetAll() => new[]
    {
        new ToolDefinition
        {
            Name        = "create_object",
            Description = "Creates a new GameObject in the Unity scene with optional mesh, transform, and components.",
            Parameters  = new List<ToolParameter>
            {
                new() { Name = "name",       Type = "string", Description = "The name for the new GameObject",                                              Required = true  },
                new() { Name = "position",   Type = "array",  Description = "World position [x, y, z]",                     ArrayItemType = "number"                       },
                new() { Name = "scale",      Type = "array",  Description = "Local scale [x, y, z]",                        ArrayItemType = "number"                       },
                new() { Name = "primitive",  Type = "string", Description = "Primitive mesh to attach",                     EnumValues = new[] { "Cube", "Sphere", "Capsule", "Plane", "Cylinder" } },
                new() { Name = "components", Type = "array",  Description = "UnityEngine component names (e.g. Rigidbody)", ArrayItemType = "string"                       }
            }
        },
        new ToolDefinition
        {
            Name        = "modify_object",
            Description = "Modifies the transform (position, scale, rotation) of an existing scene object by name.",
            Parameters  = new List<ToolParameter>
            {
                new() { Name = "target_name", Type = "string", Description = "Exact name of the GameObject to modify",  Required = true  },
                new() { Name = "position",    Type = "array",  Description = "New world position [x, y, z]",            ArrayItemType = "number" },
                new() { Name = "scale",       Type = "array",  Description = "New local scale [x, y, z]",               ArrayItemType = "number" },
                new() { Name = "rotation",    Type = "array",  Description = "New euler angles [x, y, z]",              ArrayItemType = "number" }
            }
        },
        new ToolDefinition
        {
            Name        = "generate_script",
            Description = "Writes a complete, compilable C# Unity script to disk and optionally attaches it to a scene object after recompile.",
            Parameters  = new List<ToolParameter>
            {
                new() { Name = "filename",  Type = "string", Description = "Script filename without .cs extension",                        Required = true  },
                new() { Name = "attach_to", Type = "string", Description = "GameObject name to attach after recompile (optional)"                          },
                new() { Name = "code",      Type = "string", Description = "Full compilable C# Unity script source code",                  Required = true  }
            }
        },
        new ToolDefinition
        {
            Name        = "set_material",
            Description = "Applies a material with a specified color and shader to a scene object.",
            Parameters  = new List<ToolParameter>
            {
                new() { Name = "target_name", Type = "string", Description = "Exact name of the GameObject",                   Required = true  },
                new() { Name = "color",       Type = "array",  Description = "RGB color values [r, g, b] in range 0–1",        ArrayItemType = "number" },
                new() { Name = "shader",      Type = "string", Description = "Shader name (e.g. Standard, Unlit/Color)"                         }
            }
        },
        new ToolDefinition
        {
            Name        = "suggest_design",
            Description = "Returns a structured design suggestion. No scene changes are made.",
            Parameters  = new List<ToolParameter>
            {
                new() { Name = "title",       Type = "string", Description = "Short title for the suggestion",  Required = true  },
                new() { Name = "description", Type = "string", Description = "One or two sentence overview",    Required = true  },
                new() { Name = "steps",       Type = "array",  Description = "Ordered list of actionable steps", ArrayItemType = "string" }
            }
        }
    };
}
```

### `Application/PromptOrchestrator.cs`
```csharp
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
        // Match provider by model string prefix: "claude-*" → ClaudeProvider, etc.
        var provider = _providers.FirstOrDefault(p =>
            request.Model.StartsWith(p.ProviderId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"No provider registered for model: {request.Model}");

        var systemPrompt                       = PromptBuilder.BuildSystemPrompt();
        IEnumerable<ToolDefinition> tools      = UnityToolDefinitions.GetAll();

        var providerResponse = await provider.SendAsync(request, systemPrompt, tools);
        return ToolUseParser.Parse(providerResponse);
    }
}
```

### `Infrastructure/Providers/ClaudeProvider.cs`
```csharp
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
            tools      = ToAnthropicTools(tools),   // translate to Anthropic format
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

    // ── Anthropic translation ──────────────────────────────────────────────────

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

    // ── Response parsing ───────────────────────────────────────────────────────

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
```

### `Infrastructure/Providers/GeminiProvider.cs`
```csharp
using ClaudeForUnity.Backend.Domain.Interfaces;
using ClaudeForUnity.Backend.Domain.Models;

namespace ClaudeForUnity.Backend.Infrastructure.Providers;

/// <summary>
/// Google Gemini implementation of IAiProvider.
///
/// Key translation differences vs. Anthropic:
///   - Tools are wrapped in { "tools": [{ "functionDeclarations": [...] }] }
///   - Type names are uppercase: STRING, NUMBER, ARRAY, OBJECT
///   - Response uses "functionCall" content parts instead of "tool_use" blocks
///   - Auth uses a query param (?key=) or Bearer token, not x-api-key header
///
/// TODO (Phase 5 — multi-provider):
///   - Implement ToGeminiTools() translation (mirrors ToAnthropicTools pattern)
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
        throw new NotImplementedException(
            "GeminiProvider is not yet implemented. " +
            "Implement ToGeminiTools() and ParseResponse() following the ClaudeProvider pattern.");
    }
}
```

### `Infrastructure/Providers/OpenAiProvider.cs`
```csharp
using ClaudeForUnity.Backend.Domain.Interfaces;
using ClaudeForUnity.Backend.Domain.Models;

namespace ClaudeForUnity.Backend.Infrastructure.Providers;

/// <summary>
/// OpenAI (GPT) implementation of IAiProvider.
///
/// Key translation differences vs. Anthropic:
///   - Each tool is wrapped in { "type": "function", "function": { ... } }
///   - Property schema uses "parameters" (same JSON Schema shape as Anthropic's input_schema)
///   - Response uses "tool_calls" array on the message, with "arguments" as a JSON string
///   - Auth uses Authorization: Bearer header
///
/// TODO (Phase 5 — multi-provider):
///   - Implement ToOpenAiTools() translation (mirrors ToAnthropicTools pattern, adds function wrapper)
///   - Implement ParseResponse() — deserialize tool_calls[].function.arguments (JSON string → JsonObject)
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
            "Implement ToOpenAiTools() and ParseResponse() following the ClaudeProvider pattern.");
    }
}
```

### `Program.cs`
```csharp
using DotNetEnv;
using ClaudeForUnity.Backend.Application;
using ClaudeForUnity.Backend.Domain.Interfaces;
using ClaudeForUnity.Backend.Domain.Models;
using ClaudeForUnity.Backend.Infrastructure.Providers;

Env.Load();

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables();

// Register typed HTTP clients for each provider
builder.Services.AddHttpClient<ClaudeProvider>();
builder.Services.AddHttpClient<OpenAiProvider>();
builder.Services.AddHttpClient<GeminiProvider>();

// Expose all providers as IAiProvider so PromptOrchestrator receives IEnumerable<IAiProvider>
builder.Services.AddScoped<IAiProvider>(sp => sp.GetRequiredService<ClaudeProvider>());
builder.Services.AddScoped<IAiProvider>(sp => sp.GetRequiredService<OpenAiProvider>());
builder.Services.AddScoped<IAiProvider>(sp => sp.GetRequiredService<GeminiProvider>());

builder.Services.AddScoped<PromptOrchestrator>();

var app = builder.Build();

app.MapPost("/prompt", async (PromptRequest request, PromptOrchestrator orchestrator) =>
{
    try
    {
        var result = await orchestrator.HandleAsync(request);
        return Results.Ok(result);
    }
    catch (Exception ex)
    {
        // Log ex server-side in production; return a safe message to the client
        return Results.Problem("An error occurred processing your request.");
    }
});

app.Run("http://localhost:3000");
```

### `.env`
```
ANTHROPIC_API_KEY=your_key_here
```

---

## Unity Plugin Code

### `Editor/ClaudeEditorMenu.cs`
```csharp
using UnityEditor;

public static class ClaudeEditorMenu
{
    [MenuItem("Claude AI/Open Assistant")]
    public static void OpenWindow()
    {
        ClaudeEditorWindow.OpenWindow();
    }
}
```

### `Editor/Core/ClaudeEditorWindow.cs`
```csharp
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

public class ClaudeEditorWindow : EditorWindow
{
    private string _userInput = "";
    private List<ChatMessage> _messages = new();
    private Vector2 _scrollPos;
    private bool _isLoading = false;

    public static void OpenWindow()
    {
        var window = GetWindow<ClaudeEditorWindow>("Claude AI");
        window.minSize = new Vector2(400, 600);
    }

    private void OnGUI()
    {
        DrawChatHistory();
        DrawInputArea();
    }

    private void DrawChatHistory()
    {
        _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos, GUILayout.ExpandHeight(true));
        foreach (var msg in _messages)
            ChatMessageView.Draw(msg);
        EditorGUILayout.EndScrollView();
    }

    private void DrawInputArea()
    {
        EditorGUILayout.BeginHorizontal();
        _userInput = EditorGUILayout.TextField(_userInput, GUILayout.ExpandWidth(true));

        GUI.enabled = !_isLoading;
        if (GUILayout.Button(_isLoading ? "..." : "Send", GUILayout.Width(60)))
            SendMessage();
        GUI.enabled = true;

        EditorGUILayout.EndHorizontal();
    }

    private async void SendMessage()
    {
        if (string.IsNullOrEmpty(_userInput)) return;

        _messages.Add(new ChatMessage("user", _userInput));
        var prompt = _userInput;
        _userInput = "";
        _isLoading = true;
        Repaint();

        var context = SceneContextBuilder.BuildContext();
        var response = await ClaudeApiClient.SendPrompt(prompt, context, _messages);
        ActionExecutor.Execute(response, _messages);

        _isLoading = false;
        Repaint();
    }
}
```

### `Editor/Core/SceneContextBuilder.cs`
```csharp
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using System.Text;

public static class SceneContextBuilder
{
    public static string BuildContext()
    {
        var sb = new StringBuilder();
        sb.AppendLine("## Current Unity Scene Context");
        sb.AppendLine($"Scene: {SceneManager.GetActiveScene().name}");

        var selected = Selection.activeGameObject;
        if (selected != null)
        {
            sb.AppendLine($"Selected Object: {selected.name}");
            sb.AppendLine($"Position: {selected.transform.position}");
            sb.AppendLine($"Scale: {selected.transform.localScale}");
            sb.AppendLine($"Rotation: {selected.transform.eulerAngles}");

            sb.AppendLine("Components:");
            foreach (var comp in selected.GetComponents<Component>())
                sb.AppendLine($"  - {comp.GetType().Name}");
        }
        else
        {
            sb.AppendLine("Selected Object: None");
        }

        sb.AppendLine("## Scene Hierarchy (root objects):");
        foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
            sb.AppendLine($"- {go.name}");

        return sb.ToString();
    }
}
```

### `Editor/Core/ClaudeApiClient.cs`
```csharp
using UnityEngine;
using UnityEngine.Networking;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

public static class ClaudeApiClient
{
    private const string BackendUrl = "http://localhost:3000/prompt";

    public static async Task<ClaudeResponse> SendPrompt(
        string userMessage,
        string sceneContext,
        List<ChatMessage> history)
    {
        var historyPayload = new List<object>();
        foreach (var msg in history)
            historyPayload.Add(new { role = msg.Role, content = msg.Content });

        var payload = new
        {
            userMessage,
            sceneContext,
            conversationHistory = historyPayload
        };

        var json = JsonConvert.SerializeObject(payload);
        var request = new UnityWebRequest(BackendUrl, "POST");
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");

        var operation = request.SendWebRequest();
        while (!operation.isDone)
            await Task.Yield();

        if (request.result != UnityWebRequest.Result.Success)
            throw new System.Exception($"Backend error: {request.error}");

        return JsonConvert.DeserializeObject<ClaudeResponse>(request.downloadHandler.text)!;
    }
}

public class ClaudeResponse
{
    public string Reply { get; set; } = "";
    public List<UnityActionData> Actions { get; set; } = new();
    public Dictionary<string, string> Scripts { get; set; } = new();
}

public class UnityActionData
{
    public string Action { get; set; } = "";
    // Raw data deserialized per action handler
}

public class ChatMessage
{
    public string Role { get; set; }
    public string Content { get; set; }

    public ChatMessage(string role, string content)
    {
        Role = role;
        Content = content;
    }
}
```

### `Editor/Core/ActionExecutor.cs`
```csharp
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

public static class ActionExecutor
{
    public static void Execute(ClaudeResponse response, List<ChatMessage> messages)
    {
        messages.Add(new ChatMessage("assistant", response.Reply));

        foreach (var action in response.Actions)
        {
            var data = JObject.FromObject(action);
            switch (action.Action)
            {
                case "create_object":   CreateObjectAction.Execute(data);                       break;
                case "modify_object":   ModifyObjectAction.Execute(data);                       break;
                case "generate_script": GenerateScriptAction.Execute(data, response.Scripts);   break;
                case "set_material":    SetMaterialAction.Execute(data);                        break;
                case "suggest_design":  SuggestDesignAction.Execute(data, messages);            break;
                default:
                    Debug.LogWarning($"Claude: Unknown action type '{action.Action}'");
                    break;
            }
        }
    }
}
```

### `Editor/Actions/CreateObjectAction.cs`
```csharp
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json.Linq;

public static class CreateObjectAction
{
    public static void Execute(JToken data)
    {
        var name = data["name"]?.ToString() ?? "New Object";
        var go = new GameObject(name);

        if (data["position"] != null)
        {
            go.transform.position = new Vector3(
                data["position"][0].Value<float>(),
                data["position"][1].Value<float>(),
                data["position"][2].Value<float>()
            );
        }

        if (data["scale"] != null)
        {
            go.transform.localScale = new Vector3(
                data["scale"][0].Value<float>(),
                data["scale"][1].Value<float>(),
                data["scale"][2].Value<float>()
            );
        }

        if (data["primitive"] != null)
        {
            var primitiveType = System.Enum.Parse<PrimitiveType>(data["primitive"].ToString());
            var primitive = GameObject.CreatePrimitive(primitiveType);
            primitive.name = name + "_Mesh";
            primitive.transform.SetParent(go.transform);
            primitive.transform.localPosition = Vector3.zero;
        }

        var components = data["components"] as JArray;
        if (components != null)
        {
            foreach (var comp in components)
            {
                var typeName = comp.ToString();
                var type = System.Type.GetType(typeName) ??
                           System.Type.GetType($"UnityEngine.{typeName}, UnityEngine");
                if (type != null)
                    go.AddComponent(type);
                else
                    Debug.LogWarning($"Claude: Could not find component type '{typeName}'");
            }
        }

        Undo.RegisterCreatedObjectUndo(go, $"Claude: Create {name}");
        Selection.activeGameObject = go;
        Debug.Log($"Claude: Created '{name}'");
    }
}
```

### `Editor/Actions/ModifyObjectAction.cs`
```csharp
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json.Linq;

public static class ModifyObjectAction
{
    public static void Execute(JToken data)
    {
        var targetName = data["target_name"]?.ToString();
        if (string.IsNullOrEmpty(targetName))
        {
            Debug.LogError("Claude: modify_object missing target_name");
            return;
        }

        var go = GameObject.Find(targetName);
        if (go == null)
        {
            Debug.LogError($"Claude: Could not find object '{targetName}'");
            return;
        }

        Undo.RecordObject(go.transform, $"Claude: Modify {targetName}");

        if (data["position"] != null)
            go.transform.position = new Vector3(
                data["position"][0].Value<float>(),
                data["position"][1].Value<float>(),
                data["position"][2].Value<float>()
            );

        if (data["scale"] != null)
            go.transform.localScale = new Vector3(
                data["scale"][0].Value<float>(),
                data["scale"][1].Value<float>(),
                data["scale"][2].Value<float>()
            );

        if (data["rotation"] != null)
            go.transform.eulerAngles = new Vector3(
                data["rotation"][0].Value<float>(),
                data["rotation"][1].Value<float>(),
                data["rotation"][2].Value<float>()
            );

        Debug.Log($"Claude: Modified '{targetName}'");
    }
}
```

### `Editor/Actions/GenerateScriptAction.cs`
```csharp
using UnityEditor;
using UnityEngine;
using System.IO;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

public static class GenerateScriptAction
{
    public static void Execute(JToken data, Dictionary<string, string> scripts)
    {
        var filename = data["filename"]?.ToString();
        var attachTo = data["attach_to"]?.ToString();

        if (string.IsNullOrEmpty(filename) || !scripts.TryGetValue(filename, out var code))
        {
            Debug.LogError($"Claude: No code found for script '{filename}'");
            return;
        }

        var folderPath = "Assets/ClaudeGenerated/Scripts";
        if (!Directory.Exists(folderPath))
            Directory.CreateDirectory(folderPath);

        var filePath = $"{folderPath}/{filename}.cs";
        File.WriteAllText(filePath, code);
        AssetDatabase.Refresh();

        if (!string.IsNullOrEmpty(attachTo))
            PendingAttachments.Queue(filePath, attachTo);

        Debug.Log($"Claude: Script '{filename}.cs' written to {filePath}");
    }
}
```

### `Editor/Actions/SetMaterialAction.cs`
```csharp
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json.Linq;

public static class SetMaterialAction
{
    public static void Execute(JToken data)
    {
        var targetName = data["target_name"]?.ToString();
        if (string.IsNullOrEmpty(targetName)) return;

        var go = GameObject.Find(targetName);
        if (go == null)
        {
            Debug.LogError($"Claude: Could not find object '{targetName}'");
            return;
        }

        var renderer = go.GetComponentInChildren<Renderer>();
        if (renderer == null)
        {
            Debug.LogError($"Claude: No Renderer found on '{targetName}'");
            return;
        }

        var mat = new Material(Shader.Find("Standard"));

        if (data["color"] != null)
        {
            mat.color = new Color(
                data["color"][0].Value<float>(),
                data["color"][1].Value<float>(),
                data["color"][2].Value<float>()
            );
        }

        if (data["shader"] != null)
        {
            var shader = Shader.Find(data["shader"].ToString());
            if (shader != null) mat.shader = shader;
        }

        Undo.RecordObject(renderer, $"Claude: Set Material on {targetName}");
        renderer.material = mat;
        Debug.Log($"Claude: Material applied to '{targetName}'");
    }
}
```

### `Editor/Actions/SuggestDesignAction.cs`
```csharp
using UnityEngine;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

public static class SuggestDesignAction
{
    public static void Execute(JToken data, List<ChatMessage> messages)
    {
        var title = data["title"]?.ToString() ?? "Design Suggestion";
        var description = data["description"]?.ToString() ?? "";
        var steps = data["steps"] as JArray;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"**{title}**");
        sb.AppendLine(description);

        if (steps != null)
        {
            sb.AppendLine("\nSteps:");
            int i = 1;
            foreach (var step in steps)
                sb.AppendLine($"{i++}. {step}");
        }

        messages.Add(new ChatMessage("suggestion", sb.ToString()));
        Debug.Log($"Claude Design Suggestion: {title}");
    }
}
```

### `Editor/PendingAttachments.cs`
```csharp
using UnityEditor;
using UnityEngine;
using System.Collections.Generic;

[InitializeOnLoad]
public static class PendingAttachments
{
    private static List<(string scriptPath, string targetName)> _queue = new();

    static PendingAttachments()
    {
        AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
    }

    public static void Queue(string scriptPath, string targetName)
    {
        _queue.Add((scriptPath, targetName));
        EditorPrefs.SetString("ClaudeForUnity_PendingAttachments",
            Newtonsoft.Json.JsonConvert.SerializeObject(_queue));
    }

    private static void OnAfterAssemblyReload()
    {
        var stored = EditorPrefs.GetString("ClaudeForUnity_PendingAttachments", "");
        if (string.IsNullOrEmpty(stored)) return;

        var pending = Newtonsoft.Json.JsonConvert.DeserializeObject
            <List<(string, string)>>(stored);

        foreach (var (scriptPath, targetName) in pending)
        {
            var go = GameObject.Find(targetName);
            if (go == null) continue;

            var scriptAsset = AssetDatabase.LoadAssetAtPath<MonoScript>(scriptPath);
            if (scriptAsset == null) continue;

            var type = scriptAsset.GetClass();
            if (type != null && go.GetComponent(type) == null)
            {
                go.AddComponent(type);
                Debug.Log($"Claude: Attached '{type.Name}' to '{targetName}'");
            }
        }

        EditorPrefs.DeleteKey("ClaudeForUnity_PendingAttachments");
    }
}
```

### `Editor/UI/ChatMessageView.cs`
```csharp
using UnityEditor;
using UnityEngine;

public static class ChatMessageView
{
    public static void Draw(ChatMessage msg)
    {
        var isUser = msg.Role == "user";
        var bgColor = isUser ? new Color(0.2f, 0.4f, 0.8f, 0.2f) : new Color(0.1f, 0.1f, 0.1f, 0.3f);
        var label = isUser ? "You" : "Claude";

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUILayout.Label(label, EditorStyles.boldLabel);
        EditorGUILayout.SelectableLabel(msg.Content, EditorStyles.wordWrappedLabel,
            GUILayout.ExpandWidth(true), GUILayout.MinHeight(20));
        EditorGUILayout.EndVertical();
        GUILayout.Space(4);
    }
}
```

---

## Build Phases (Start to Finish)

### Phase 1 — .NET Backend
**Goal: Send a prompt, get back clean structured action data**

- [ ] `dotnet new webapi -n ClaudeForUnity.Backend`
- [ ] Add `DotNetEnv` NuGet package
- [ ] `Domain/Interfaces/IAiProvider.cs` — interface (ProviderId + SendAsync with tools)
- [ ] `Domain/Models/PromptRequest.cs` — includes Model + ApiKey fields
- [ ] `Domain/Models/ToolDefinition.cs` — provider-agnostic ToolDefinition + ToolParameter
- [ ] `Domain/Models/AiProviderResponse.cs` — normalized response (Reply + ToolCalls)
- [ ] `Domain/Models/ClaudeResponse.cs` — outbound shape to Unity plugin
- [ ] `Domain/Models/UnityAction.cs`
- [ ] `Application/PromptBuilder.cs` — role + rules system prompt (no format engineering)
- [ ] `Application/UnityToolDefinitions.cs` — typed JSON schemas for all 5 actions
- [ ] `Application/ToolUseParser.cs` — maps AiProviderResponse → ClaudeResponse
- [ ] `Application/PromptOrchestrator.cs` — resolves IAiProvider, calls it, returns result
- [ ] `Infrastructure/Providers/ClaudeProvider.cs` — Anthropic tool_use HTTP implementation (full)
- [ ] `Infrastructure/Providers/GeminiProvider.cs` — stub (NotImplementedException, translation notes in comments)
- [ ] `Infrastructure/Providers/OpenAiProvider.cs` — stub (NotImplementedException, translation notes in comments)
- [ ] Wire up `Program.cs` with DI (typed clients + IAiProvider registrations)
- [ ] Add `.env` with API key
- [ ] Test with Postman or curl before touching Unity

**Test curl:**
```bash
curl -X POST http://localhost:3000/prompt \
  -H "Content-Type: application/json" \
  -d '{"userMessage":"create a cube at 0,1,0","sceneContext":"Scene: TestScene","model":"claude-sonnet-4-6","apiKey":"sk-ant-...","conversationHistory":[]}'
```

---

### Phase 2 — Unity Plugin Skeleton
**Goal: Chat window opens, talks to backend, shows replies**

- [ ] Create `Assets/ClaudeForUnity/Editor/` folder structure
- [ ] Add Newtonsoft.Json to Unity project (via Package Manager)
- [ ] Implement `ChatMessage.cs`
- [ ] Implement `ClaudeApiClient.cs`
- [ ] Implement `ClaudeEditorWindow.cs`
- [ ] Implement `ChatMessageView.cs`
- [ ] Implement `ClaudeEditorMenu.cs`
- [ ] Test: open window, send a prompt, see reply in chat

---

### Phase 3 — Scene Context
**Goal: Claude knows what's in the scene**

- [ ] Implement `SceneContextBuilder.cs`
- [ ] Wire into `ClaudeEditorWindow.SendMessage()`
- [ ] Test: select a GameObject, ask Claude what components it has

---

### Phase 4 — Action Execution
**Goal: Claude's responses actually change the scene**

- [ ] Implement `ActionExecutor.cs`
- [ ] Implement `CreateObjectAction.cs`
- [ ] Implement `ModifyObjectAction.cs`
- [ ] Implement `GenerateScriptAction.cs`
- [ ] Implement `PendingAttachments.cs`
- [ ] Implement `SetMaterialAction.cs`
- [ ] Implement `SuggestDesignAction.cs`
- [ ] Test each action with a targeted prompt

---

### Phase 5 — Polish
**Goal: Stable enough for other developers**

- [ ] Loading indicator while waiting for response
- [ ] Error message if backend is unreachable or key is invalid
- [ ] Settings panel:
  - Backend URL (default: your hosted URL, overridable for local dev)
  - Model selector dropdown (Claude / GPT / Gemini + specific model versions)
  - API key field per provider (stored in Unity EditorPrefs, not in code)
- [ ] Auto-create `Assets/ClaudeGenerated/Scripts/` folder
- [ ] End-to-end test of all action types across at least 2 providers

---

### Phase 6 — Deploy and Distribute
**Goal: Someone installs it and is prompting Claude/GPT/Gemini in under 5 minutes**

**Backend:**
- [ ] Deploy backend to hosting provider (fly.io / Railway / Azure)
- [ ] Set environment variables for any server-side config (logging, CORS, etc.)
- [ ] Verify HTTPS is enforced (keys travel in request body)
- [ ] Smoke test all 3 providers from production URL

**Plugin:**
- [ ] Bake hosted backend URL as default in `ClaudeApiClient.cs`
- [ ] Export as `.unitypackage`
- [ ] Write README: install steps, where to enter API key, example prompts
- [ ] Publish plugin + link to hosted backend on GitHub

---

## Running the Backend

**Local development:**
```bash
cd Backend
dotnet run
# Server starts on http://localhost:3000
# Point plugin settings to http://localhost:3000 while developing
```

**Production deployment (example — fly.io):**
```bash
fly launch       # detects .NET app, creates fly.toml
fly deploy       # builds and deploys
# Your backend is now at https://your-app.fly.dev
```

Other zero-config options: Railway, Azure App Service (free tier), Render.
All support .NET 8 out of the box with a Dockerfile or buildpack.

---

## Example Prompts Developers Can Use

```
"Create a player character with a capsule collider and rigidbody"
"Generate a health system script and attach it to the Player"
"Scale the Platform object to be 3x wider"
"Make the floor a dark grey matte material"
"Add an enemy with patrol AI"
"I'm building a top-down shooter, how should I structure my scene?"
"Create 5 platforms spread across the x-axis from -10 to 10"
"Generate a jump controller and attach it to Player"
```

---

## Key Design Decisions (Why We Made Them)

**Why Clean Architecture (Api/Application/Domain/Infrastructure)?** Each layer has one job and depends only on what's below it — this is the Dependency Rule. The biggest payoff: adding a new AI provider means writing one class that implements `IAiProvider` — zero changes to Application or Api. Without this, multi-provider support means duplicating orchestration logic per vendor. This matches how `eShopOnWeb`, `jasontaylordev/CleanArchitecture`, and `ardalis/CleanArchitecture` are structured.

**Why IAiProvider as the abstraction boundary?** Tool schemas are provider-agnostic — any model that supports tool_use (Claude, GPT, Gemini) receives the same `UnityToolDefinitions` and returns structured inputs for each action. The only thing that varies per provider is the HTTP call, auth headers, and how to extract text and tool_use blocks from the vendor's response format. Each provider normalizes its response into `AiProviderResponse`; everything above Infrastructure sees only that common shape. `IAiProvider` captures exactly that surface and nothing more.

**Why .NET backend instead of Node?** Keeps the entire stack in C#, easier for Unity developers to understand and modify, and opens the door to eventually compiling the backend into the Unity plugin itself as a DLL — removing the need for a separate server.

**Why a hosted backend instead of calling AI providers directly from Unity?** Two reasons. First, AI provider SDKs and raw HTTP plumbing don't belong in a Unity Editor plugin — the backend is the right place for that logic. Second, the backend is the unified abstraction point for multi-provider support: Claude, GPT, and Gemini all look the same to the plugin (one POST to `/prompt`), and provider-specific differences are handled server-side.

**Why BYOK (Bring Your Own Key)?** You can't pay for every user's AI API calls — costs would scale with usage and you'd need billing infrastructure. BYOK is standard for developer tools. Users enter their own Claude/OpenAI/Google key once in the plugin settings. The key travels in the HTTPS request body, is used for that one call, and is never stored. The backend is a stateless proxy.

**Why not call AI providers directly from the Unity plugin?** You could — HTTPS from UnityWebRequest works fine. But then multi-provider support, prompt engineering, and response parsing all live in the plugin, which requires users to update the plugin every time you change anything. The hosted backend gives you a layer you control independently.

**Why tool_use instead of a custom response format?** The original hybrid JSON + csharp block approach required engineering a complex system prompt to produce a custom text format, then regex-parsing it back out — a layer of fragility with no safety net. One stray newline or whitespace variation breaks the parse. Native tool_use (`tools` array in the API request) gives Claude typed, schema-validated inputs for every action. The AI fills in the fields; the backend reads structured JSON. Code for `generate_script` becomes an ordinary string parameter — no code-in-JSON escaping risk, no `script_key` ↔ fence label contract, no `Validate()` throwing on malformed output. `ToolUseParser` is 30 lines of straightforward iteration; `ActionParser` was 60 lines of regex with edge cases. The tool_use approach also aligns with the MCP (Model Context Protocol) paradigm, making it easier to extend to MCP-native clients in the future.

**Why Undo registration on every action?** Developers need to trust the tool. If Claude makes a mistake or does something unexpected, one Ctrl+Z should fix it. Without this, developers would be afraid to use it on real projects.

**Why PendingAttachments with assembly reload?** Unity must recompile C# before a new script type exists in memory. You cannot call `AddComponent<HealthSystem>()` on a script that was just written to disk — Unity doesn't know it exists yet. The pending queue fires after the recompile completes.
