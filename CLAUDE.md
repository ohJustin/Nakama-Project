# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

All backend commands run from `Backend/`:

```bash
# Restore, build, run
dotnet restore
dotnet build
dotnet run          # serves on http://localhost:3000

# Test the /prompt endpoint manually
curl -X POST http://localhost:3000/prompt \
  -H "Content-Type: application/json" \
  -d '{"userMessage":"create a cube","sceneContext":"Scene: Test","apiKey":"sk-ant-...","model":"claude-sonnet-4-6","conversationHistory":[]}'
```

If `dotnet` is not on PATH: `export PATH="$PATH:/c/Program Files/dotnet"`.

API key for local dev goes in `Backend/.env` as `ANTHROPIC_API_KEY=sk-ant-...`.

## Architecture

This is a Unity Editor plugin (Phases 2–6, not yet built) + a hosted .NET 8 Web API backend (Phase 1, in progress). The full design spec is in [CLAUDE_FOR_UNITY_DESIGN.md](CLAUDE_FOR_UNITY_DESIGN.md).

### Backend layout (Clean Architecture)

```
Backend/
  Program.cs                    — DI wiring; maps POST /prompt
  Domain/
    Interfaces/
      IAiProvider.cs            — provider contract: ProviderId + SendAsync(request, systemPrompt, tools)
    Models/
      PromptRequest.cs          — incoming request (UserMessage, SceneContext, History, Model, ApiKey)
      ToolDefinition.cs         — provider-agnostic tool schema (ToolDefinition + ToolParameter)
      AiProviderResponse.cs     — normalized response from any provider (Reply + ToolCalls list)
      ToolInvocation.cs         — single tool call: ToolName + Input (JsonObject)
      ClaudeResponse.cs         — outbound response to Unity plugin (Reply, Actions, Scripts)
      UnityAction.cs            — single action: Action name + Data (JsonObject)
  Application/
    PromptOrchestrator.cs       — resolves IAiProvider by model string, calls it, maps AiProviderResponse → ClaudeResponse
    PromptBuilder.cs            — builds system prompt (role + rules only — no format engineering)
    UnityToolDefinitions.cs     — typed JSON schemas for all 5 Unity actions
    ToolUseParser.cs            — maps AiProviderResponse into ClaudeResponse; populates Scripts from generate_script tool calls
  Infrastructure/
    Providers/
      ClaudeProvider.cs         — Anthropic API: sends tool_use request, parses tool_use content blocks
      OpenAiProvider.cs         — OpenAI API implementation (planned)
      GeminiProvider.cs         — Google Gemini implementation (planned)
```

### Tool use format

Tools are defined in `UnityToolDefinitions` as typed JSON schemas and passed to the AI provider on every request. The AI returns structured `tool_use` content blocks — no text parsing, no regex.

- Text content blocks → `ClaudeResponse.Reply`
- `tool_use` content blocks → `ClaudeResponse.Actions`
- `generate_script` tool calls pass full C# source as a `code` string parameter — no code fences, no script_key contract

`ToolUseParser` maps the normalized `AiProviderResponse` to `ClaudeResponse` (the shape the Unity plugin consumes). Each provider normalizes its own vendor response format into `AiProviderResponse` internally — the rest of the app never sees vendor-specific shapes.

### BYOK model

The Unity plugin UI collects the user's own API key and sends it in the HTTPS request body. The backend is a stateless proxy — it never stores keys.

### Multi-provider support

`IAiProvider.SendAsync` accepts `IEnumerable<ToolDefinition>` (provider-agnostic schemas) and returns a normalized `AiProviderResponse`. Each provider translates `ToolDefinition` into its own vendor format internally — Anthropic uses `input_schema`, Gemini uses `functionDeclarations` with uppercase types, OpenAI wraps each tool in `{ type: "function", function: {...} }`. `PromptOrchestrator` resolves the correct provider by matching the `Model` field prefix (`"claude-*"` → `ClaudeProvider`, `"gpt-*"` → `OpenAiProvider`, `"gemini-*"` → `GeminiProvider`). `GeminiProvider` and `OpenAiProvider` are currently stubbed — adding a new provider means implementing one class with one method and zero changes to Application or Api layers.

### Unity plugin (future phases)

Will live under `Assets/ClaudeForUnity/` once Phase 2 begins. All scene mutations must use Unity's Undo system. `PendingAttachments` logic must run after assembly reload because newly generated scripts don't exist in memory until Unity recompiles.

---

## Project Progress

> **Keep this section updated.** After completing any task, update the relevant row so the next session starts with accurate context.

| Layer | Status |
|---|---|
| **Design docs** | ✅ Fully synced — Clean Architecture, tool_use, `ToolDefinition` model reflected in both `CLAUDE.md` and `CLAUDE_FOR_UNITY_DESIGN.md` |
| **Phase 1 — .NET Backend** | ✅ Clean Architecture refactor complete — `Domain/Application/Infrastructure` structure, builds with 0 errors |
| `ClaudeProvider` | ✅ Fully implemented — Anthropic `ToolDefinition` translation + `tool_use` response parsing |
| `GeminiProvider` / `OpenAiProvider` | 📋 Stubbed — `NotImplementedException` with vendor translation notes, ready for Phase 5 |
| **Phase 2 — Unity Plugin: Chat UI** | ⬜ Not started |
| **Phase 3 — Unity Plugin: Scene Context** | ⬜ Not started |
| **Phase 4 — Unity Plugin: Action Execution** | ⬜ Not started |
| **Phase 5 — Polish + Settings Panel** | ⬜ Not started — includes filling in `GeminiProvider` + `OpenAiProvider` |
| **Phase 6 — Deploy + Distribute** | ⬜ Not started |

### Next step
**Phase 2 — Unity Plugin: Chat UI.** Create `Assets/ClaudeForUnity/Editor/` folder structure, add Newtonsoft.Json via Package Manager, and implement `ClaudeEditorWindow`, `ClaudeApiClient`, `ChatMessageView`, and `ClaudeEditorMenu`. Done when the chat window opens in Unity, a prompt can be sent, and the reply appears in the window.
