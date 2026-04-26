using ClaudeForUnity.Backend.Domain.Models;

namespace ClaudeForUnity.Backend.Application;

/// <summary>
/// Maps the normalized AiProviderResponse (from any IAiProvider) into the
/// ClaudeResponse shape that the Unity plugin consumes.
/// No regex — the input is already structured from the tool_use API response.
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
