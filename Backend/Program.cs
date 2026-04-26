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
builder.Services.AddHttpClient<GeminiProvider>();
builder.Services.AddHttpClient<OpenAiProvider>();

// Expose all providers as IAiProvider so PromptOrchestrator receives IEnumerable<IAiProvider>
builder.Services.AddScoped<IAiProvider>(sp => sp.GetRequiredService<ClaudeProvider>());
builder.Services.AddScoped<IAiProvider>(sp => sp.GetRequiredService<GeminiProvider>());
builder.Services.AddScoped<IAiProvider>(sp => sp.GetRequiredService<OpenAiProvider>());

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
        // TODO: replace with structured logging (ILogger) before production deployment
        Console.Error.WriteLine($"[ERROR] {ex.Message}");
        return Results.Problem("An error occurred processing your request.");
    }
});

app.Run("http://localhost:3000");
