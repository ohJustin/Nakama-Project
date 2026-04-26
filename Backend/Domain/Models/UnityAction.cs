using System.Text.Json.Nodes;

namespace ClaudeForUnity.Backend.Domain.Models;

public class UnityAction
{
    public string Action { get; set; } = "";
    public JsonObject? Data { get; set; }
}
