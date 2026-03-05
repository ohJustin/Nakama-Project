namespace ClaudeForUnity.Backend.Models;

public class ClaudeResponse
{
    public string Reply { get; set; } = "";
    public List<UnityAction> Actions { get; set; } = new();
    public Dictionary<string, string> Scripts { get; set; } = new();
}
