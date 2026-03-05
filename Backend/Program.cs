using DotNetEnv;
using ClaudeForUnity.Backend.Models;
using ClaudeForUnity.Backend.Services;
using ClaudeForUnity.Backend.Utils;

Env.Load();

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables();

builder.Services.AddHttpClient<ClaudeService>();

var app = builder.Build();

app.MapPost("/prompt", async (PromptRequest request, ClaudeService claudeService) =>
{
    try
    {
        var rawText = await claudeService.SendAsync(request);
        var parsed = ActionParser.Parse(rawText);
        return Results.Ok(parsed);
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

app.Run("http://localhost:3000");
