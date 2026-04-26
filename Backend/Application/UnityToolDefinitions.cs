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
                new() { Name = "title",       Type = "string", Description = "Short title for the suggestion",   Required = true  },
                new() { Name = "description", Type = "string", Description = "One or two sentence overview",     Required = true  },
                new() { Name = "steps",       Type = "array",  Description = "Ordered list of actionable steps", ArrayItemType = "string" }
            }
        }
    };
}
