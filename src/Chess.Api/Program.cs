using Chess.Api.Hubs;
using Chess.Api.Models;
using Chess.Api.Services;
using Microsoft.AspNetCore.SignalR;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy("development-clients", policy =>
    {
        policy.AllowAnyHeader()
            .AllowAnyMethod()
            .SetIsOriginAllowed(_ => true)
            .AllowCredentials();
    });
});
builder.Services.AddSignalR();
builder.Services.AddSingleton<GameStore>();
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var app = builder.Build();

app.UseCors("development-clients");

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    service = "chess-api",
    utc = DateTimeOffset.UtcNow
}));

app.MapGet("/api/games", (GameStore store) => Results.Ok(store.List()));

app.MapPost("/api/games", (CreateGameRequest request, GameStore store) =>
{
    var snapshot = store.Create(request);
    return Results.Created($"/api/games/{snapshot.Id}", snapshot);
});

app.MapGet("/api/games/{id:guid}", (Guid id, GameStore store) =>
    store.TryGet(id, out var snapshot)
        ? Results.Ok(snapshot)
        : Results.NotFound(new { error = "Game was not found." }));

app.MapPost("/api/games/{id:guid}/moves", async (
    Guid id,
    MoveRequest request,
    GameStore store,
    IHubContext<ChessHub> hub) =>
{
    var result = store.ApplyMove(id, request);
    if (!result.Success)
    {
        return Results.BadRequest(new { error = result.Error, game = result.Snapshot });
    }

    await hub.Clients.Group(ChessHub.GroupName(id)).SendAsync("gameUpdated", result.Snapshot);
    return Results.Ok(result.Snapshot);
});

app.MapPost("/api/games/{id:guid}/resign", async (
    Guid id,
    ResignRequest request,
    GameStore store,
    IHubContext<ChessHub> hub) =>
{
    var result = store.Resign(id, request);
    if (!result.Success)
    {
        return Results.BadRequest(new { error = result.Error, game = result.Snapshot });
    }

    await hub.Clients.Group(ChessHub.GroupName(id)).SendAsync("gameUpdated", result.Snapshot);
    return Results.Ok(result.Snapshot);
});

app.MapHub<ChessHub>("/hubs/chess");

app.Run();

public partial class Program;
