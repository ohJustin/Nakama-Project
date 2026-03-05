using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using ClaudeForUnity.Backend.Models;

namespace ClaudeForUnity.Backend.Utils;

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
