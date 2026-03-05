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
AI responds with hybrid format (JSON block + csharp code blocks)
        ↓
ActionParser.cs splits them — JSON for instructions, code blocks for scripts
        ↓
Validated response returned to Unity plugin
        ↓
ActionExecutor.cs routes each action to the correct handler class
        ↓
Scene changes execute, .cs files are written, chat window updates
```

---

## Hybrid Response Format (Critical Design Decision)

Claude must never put code inside JSON — special characters break JSON parsing. Instead, Claude returns two separate block types in one response:

```
```json
{
  "reply": "Here's a complete health system attached to your Player.",
  "actions": [
    {
      "action": "generate_script",
      "filename": "HealthSystem",
      "attach_to": "Player",
      "script_key": "HealthSystem"
    }
  ]
}
```

```csharp:HealthSystem
using UnityEngine;
using System;

public class HealthSystem : MonoBehaviour
{
    public float maxHealth = 100f;
    private float _currentHealth;
    public event Action OnDeath;

    void Start()
    {
        _currentHealth = maxHealth;
    }

    public void TakeDamage(float amount)
    {
        _currentHealth -= amount;
        if (_currentHealth <= 0)
        {
            _currentHealth = 0;
            OnDeath?.Invoke();
        }
    }

    public float GetHealth() => _currentHealth;
}
```
```

The `script_key` in the JSON action must exactly match the label after the colon in the csharp block. Multiple scripts in one response are supported — each gets its own labeled block.

---

## Backend Architecture (Layered)

The backend follows Clean Architecture: **Api → Application → Domain → Infrastructure**.
Dependencies only point inward — nothing in Domain or Application ever references Infrastructure.
External vendors (Anthropic, OpenAI, Google) are isolated entirely inside Infrastructure.

```
Backend/
  Api/             HTTP surface — Program.cs, endpoint mapping, request/response wiring
  Application/     Use cases and orchestration — PromptOrchestrator, PromptBuilder, ActionParser
                   Provider-agnostic: any model follows the hybrid format via the system prompt
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
    Task<string> SendAsync(PromptRequest request, string systemPrompt);
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
IAiProvider.SendAsync() → raw text back (vendor handles HTTP + auth)
                          ↓
ActionParser.Parse(rawText) → ClaudeResponse (same for all providers)
```

The system prompt enforces the hybrid format on any model. Parsing is always the same.

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
│   │       ├── ClaudeResponse.cs
│   │       └── UnityAction.cs
│   ├── Application/
│   │   ├── PromptOrchestrator.cs              # Resolves provider, calls it, parses result
│   │   ├── PromptBuilder.cs                   # System prompt (provider-agnostic)
│   │   └── ActionParser.cs                    # Parses hybrid response (same for all)
│   └── Infrastructure/
│       └── Providers/
│           ├── ClaudeProvider.cs              # Anthropic API implementation
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

### `Domain/Models/ClaudeResponse.cs`
```csharp
namespace ClaudeForUnity.Backend.Domain.Models;

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

public static class PromptBuilder
{
    public static string BuildSystemPrompt() => """
        You are an expert Unity game developer AI assistant embedded inside the Unity Editor.

        ## RESPONSE FORMAT
        Always respond with a JSON block first, then any code blocks after.

        ### JSON block (always required):
        ```json
        {
          "reply": "Your explanation to the developer",
          "actions": [ ... ]
        }
        ```

        ### Code blocks (only when generate_script is in actions):
        ```csharp:ScriptName
        // full script here
        ```

        The "script_key" in the action must exactly match the label after the colon in the code block.
        You can return multiple code blocks if multiple scripts are requested.

        ## AVAILABLE ACTIONS
        - create_object: { name, position[x,y,z], scale[x,y,z], primitive(Cube/Sphere/Capsule/Plane/Cylinder), components[] }
        - modify_object: { target_name, position, scale, rotation }
        - generate_script: { filename, attach_to, script_key }
        - set_material: { target_name, color[r,g,b], shader }
        - suggest_design: { title, description, steps[] }

        ## SCRIPT WRITING RULES
        - Always write complete, compilable, immediately testable C# Unity scripts
        - Use MonoBehaviour unless the developer specifically asks for ScriptableObject or plain C#
        - Include using statements, proper access modifiers, and SerializeField where appropriate
        - Add brief comments on non-obvious logic
        - Never leave placeholder comments like "// implement this" — write the actual implementation

        ## RULES
        - Always include a helpful "reply" even if no actions are needed
        - Only use components that exist in UnityEngine (Rigidbody, BoxCollider, AudioSource, etc.)
        - If the user asks a question with no scene changes needed, return an empty actions array
        - Be concise, practical, and think like a senior Unity developer
        """;
}
```

### `Application/ActionParser.cs`
```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ClaudeForUnity.Backend.Domain.Models;

namespace ClaudeForUnity.Backend.Application;

public static class ActionParser
{
    public static ClaudeResponse Parse(string rawText)
    {
        var result = new ClaudeResponse();

        // Extract JSON block
        var jsonMatch = Regex.Match(rawText, @"```json\n([\s\S]*?)\n```");
        if (!jsonMatch.Success)
        {
            result.Reply = rawText;
            return result;
        }

        var jsonDoc = JsonNode.Parse(jsonMatch.Groups[1].Value)!;
        result.Reply = jsonDoc["reply"]?.GetValue<string>() ?? "";

        var actions = jsonDoc["actions"]?.AsArray();
        if (actions != null)
        {
            foreach (var action in actions)
            {
                var actionObj = action?.AsObject();
                if (actionObj == null) continue;

                result.Actions.Add(new UnityAction
                {
                    Action = actionObj["action"]?.GetValue<string>() ?? "",
                    Data = actionObj
                });
            }
        }

        // Extract all csharp:Key blocks
        var csharpMatches = Regex.Matches(rawText, @"```csharp:(\w+)\n([\s\S]*?)\n```");
        foreach (Match match in csharpMatches)
        {
            result.Scripts[match.Groups[1].Value] = match.Groups[2].Value;
        }

        Validate(result);
        return result;
    }

    private static void Validate(ClaudeResponse response)
    {
        foreach (var action in response.Actions)
        {
            if (action.Action != "generate_script") continue;

            var scriptKey = action.Data?["script_key"]?.GetValue<string>();
            if (string.IsNullOrEmpty(scriptKey))
                throw new InvalidOperationException("generate_script action is missing script_key");

            if (!response.Scripts.ContainsKey(scriptKey))
                throw new InvalidOperationException($"No code block found for script_key: {scriptKey}");
        }
    }
}
```

### `Infrastructure/Providers/ClaudeProvider.cs`
```csharp
using System.Text;
using System.Text.Json;
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

    public async Task<string> SendAsync(PromptRequest request, string systemPrompt)
    {
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
            model = request.Model,
            max_tokens = 4096,
            system = systemPrompt,
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

        var responseBody = await response.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(responseBody);
        return doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? "";
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

// Register all providers — DI resolves the right one at runtime
builder.Services.AddHttpClient<ClaudeProvider>();
builder.Services.AddHttpClient<OpenAiProvider>();
builder.Services.AddHttpClient<GeminiProvider>();

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
        return Results.Problem(ex.Message);
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
        var scriptKey = data["script_key"]?.ToString();

        if (!scripts.TryGetValue(scriptKey, out var code))
        {
            Debug.LogError($"Claude: No code found for script_key '{scriptKey}'");
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
**Goal: Send a prompt, get back clean parsed data**

- [ ] `dotnet new webapi -n ClaudeForUnity.Backend`
- [ ] Add `DotNetEnv` NuGet package
- [ ] `Domain/Interfaces/IAiProvider.cs` — interface
- [ ] `Domain/Models/` — PromptRequest, ClaudeResponse, UnityAction
- [ ] `Application/PromptBuilder.cs` — system prompt
- [ ] `Application/ActionParser.cs` — hybrid response parsing
- [ ] `Application/PromptOrchestrator.cs` — resolves provider, coordinates the call
- [ ] `Infrastructure/Providers/ClaudeProvider.cs` — Anthropic HTTP implementation
- [ ] Wire up `Program.cs` with DI
- [ ] Add `.env` with API key
- [ ] Test with Postman or curl before touching Unity

**Test curl:**
```bash
curl -X POST http://localhost:3000/prompt \
  -H "Content-Type: application/json" \
  -d '{"userMessage":"create a cube at 0,1,0","sceneContext":"Scene: TestScene","conversationHistory":[]}'
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

**Why IAiProvider as the abstraction boundary?** The hybrid JSON+csharp format is enforced through the system prompt, not through vendor-specific behavior. Any model (Claude, GPT, Gemini) can be instructed to follow it. So the parsing logic in `ActionParser` is universal — the only thing that varies per provider is the HTTP call, auth headers, and how you extract the text content from the response. `IAiProvider` captures exactly that surface and nothing more.

**Why .NET backend instead of Node?** Keeps the entire stack in C#, easier for Unity developers to understand and modify, and opens the door to eventually compiling the backend into the Unity plugin itself as a DLL — removing the need for a separate server.

**Why a hosted backend instead of calling AI providers directly from Unity?** Two reasons. First, AI provider SDKs and raw HTTP plumbing don't belong in a Unity Editor plugin — the backend is the right place for that logic. Second, the backend is the unified abstraction point for multi-provider support: Claude, GPT, and Gemini all look the same to the plugin (one POST to `/prompt`), and provider-specific differences are handled server-side.

**Why BYOK (Bring Your Own Key)?** You can't pay for every user's AI API calls — costs would scale with usage and you'd need billing infrastructure. BYOK is standard for developer tools. Users enter their own Claude/OpenAI/Google key once in the plugin settings. The key travels in the HTTPS request body, is used for that one call, and is never stored. The backend is a stateless proxy.

**Why not call AI providers directly from the Unity plugin?** You could — HTTPS from UnityWebRequest works fine. But then multi-provider support, prompt engineering, and response parsing all live in the plugin, which requires users to update the plugin every time you change anything. The hosted backend gives you a layer you control independently.

**Why the hybrid JSON + csharp block format?** Code inside JSON strings is fragile — one unescaped quote or backslash breaks the entire parse. Separating them into distinct labeled blocks means a malformed script never corrupts the action instructions, and vice versa.

**Why Undo registration on every action?** Developers need to trust the tool. If Claude makes a mistake or does something unexpected, one Ctrl+Z should fix it. Without this, developers would be afraid to use it on real projects.

**Why PendingAttachments with assembly reload?** Unity must recompile C# before a new script type exists in memory. You cannot call `AddComponent<HealthSystem>()` on a script that was just written to disk — Unity doesn't know it exists yet. The pending queue fires after the recompile completes.
