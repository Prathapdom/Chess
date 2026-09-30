# Chess

Chess is a Windows-first, Lichess-inspired chess application built around a shared C# rules engine.

The first vertical slice includes:

- Complete core move legality: castling, en passant, promotion, check, checkmate, stalemate, and draw detection.
- A WinUI 3 desktop board for local two-player games.
- An ASP.NET Core API with in-memory games and a SignalR hub for real-time clients.
- Server-authoritative clocks and move validation.
- A test project covering the chess rules that are easiest to regress.

The API is intentionally a modular-monolith foundation. PostgreSQL, Redis, accounts, ratings, matchmaking, Stockfish analysis, tournaments, and browser clients can be added without moving the rules out of `Chess.Domain`.

## Projects

```text
src/Chess.Domain      Shared chess rules and FEN support
src/Chess.Api         ASP.NET Core REST API and SignalR hub
src/Chess.Desktop     WinUI 3 desktop client
tests/Chess.Tests     Rules and API-facing domain tests
```

## Build

The machine uses the SDK installed at `F:\dev\dotnet` and the shared package cache at `F:\dev\nuget\packages`:

```powershell
$taskDotnetRoot = 'F:\dev\dotnet'
$env:DOTNET_ROOT = $taskDotnetRoot
$env:Path = "$taskDotnetRoot;$env:Path"
$env:NUGET_PACKAGES = 'F:\dev\nuget\packages'

dotnet build Chess.sln
dotnet test tests\Chess.Tests\Chess.Tests.csproj
```

## Run the API

```powershell
$taskDotnetRoot = 'F:\dev\dotnet'
$env:DOTNET_ROOT = $taskDotnetRoot
$env:Path = "$taskDotnetRoot;$env:Path"
$env:NUGET_PACKAGES = 'F:\dev\nuget\packages'

dotnet run --project src\Chess.Api --urls http://localhost:5180
```

The API exposes `GET /api/health`, `GET /api/games`, `POST /api/games`, game move/resign endpoints, and `/hubs/chess` for SignalR clients. The desktop client currently uses the REST endpoints and polls active online games; the hub is ready for a push-based client.

## Notes

This is an independent implementation. The Lichess source repository is AGPL-licensed, so its source code and assets should not be copied into this project without following those terms.
