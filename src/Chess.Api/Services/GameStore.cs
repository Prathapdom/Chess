using System.Collections.Concurrent;
using Chess.Api.Models;
using Chess.Domain;

namespace Chess.Api.Services;

public sealed class GameStore
{
    private readonly ConcurrentDictionary<Guid, GameSession> _games = new();

    public GameSnapshot Create(CreateGameRequest request)
    {
        var initialMinutes = Math.Clamp(request.InitialMinutes ?? 5, 1, 180);
        var incrementSeconds = Math.Clamp(request.IncrementSeconds ?? 3, 0, 60);
        var now = DateTimeOffset.UtcNow;
        var position = ChessPosition.CreateInitial();
        var id = Guid.NewGuid();
        var session = new GameSession
        {
            Id = id,
            WhitePlayer = CleanPlayerName(request.WhitePlayer, "White"),
            BlackPlayer = CleanPlayerName(request.BlackPlayer, "Black"),
            Position = position,
            Status = ChessRules.GetStatus(position),
            WhiteRemaining = TimeSpan.FromMinutes(initialMinutes),
            BlackRemaining = TimeSpan.FromMinutes(initialMinutes),
            Increment = TimeSpan.FromSeconds(incrementSeconds),
            TurnStartedAtUtc = now,
            UpdatedAtUtc = now
        };
        session.PositionHistory.Add(position.PositionKey());
        _games[id] = session;
        return ToSnapshot(session, now);
    }

    public IReadOnlyList<GameSnapshot> List()
    {
        var now = DateTimeOffset.UtcNow;
        return _games.Values
            .OrderByDescending(game => game.UpdatedAtUtc)
            .Select(game =>
            {
                lock (game.SyncRoot)
                {
                    RefreshClock(game, now);
                    return ToSnapshot(game, now);
                }
            })
            .ToArray();
    }

    public bool TryGet(Guid id, out GameSnapshot? snapshot)
    {
        if (!_games.TryGetValue(id, out var game))
        {
            snapshot = null;
            return false;
        }

        lock (game.SyncRoot)
        {
            var now = DateTimeOffset.UtcNow;
            RefreshClock(game, now);
            snapshot = ToSnapshot(game, now);
            return true;
        }
    }

    public StoreResult ApplyMove(Guid id, MoveRequest request)
    {
        if (!_games.TryGetValue(id, out var game))
        {
            return StoreResult.Failed("Game was not found.");
        }

        if (!TryParseColor(request.PlayerColor, out var playerColor))
        {
            return StoreResult.Failed("PlayerColor must be White or Black.");
        }

        if (!Square.TryParse(request.From, out var from) || !Square.TryParse(request.To, out var to))
        {
            return StoreResult.Failed("From and To must be valid squares such as e2 and e4.");
        }

        if (!TryParsePromotion(request.Promotion, out var promotion))
        {
            return StoreResult.Failed("Promotion must be one of q, r, b, or n.");
        }

        lock (game.SyncRoot)
        {
            var now = DateTimeOffset.UtcNow;
            RefreshClock(game, now);
            if (game.Status.IsTerminal())
            {
                return StoreResult.Failed("The game is already over.", ToSnapshot(game, now));
            }

            if (game.Position.SideToMove != playerColor)
            {
                return StoreResult.Failed("It is not this player's turn.", ToSnapshot(game, now));
            }

            var requestedMove = new ChessMove(from, to, promotion);
            if (!ChessRules.TryApplyMove(game.Position, requestedMove, out var next, out var applied))
            {
                return StoreResult.Failed("That move is not legal.", ToSnapshot(game, now));
            }

            if (playerColor == PieceColor.White)
            {
                game.WhiteRemaining += game.Increment;
            }
            else
            {
                game.BlackRemaining += game.Increment;
            }

            game.Position = next;
            game.Moves.Add(new GameMoveDto(applied.ToUci(), playerColor, now));
            game.PositionHistory.Add(next.PositionKey());
            game.Status = ChessRules.GetStatus(next, game.PositionHistory);
            game.TurnStartedAtUtc = now;
            game.UpdatedAtUtc = now;
            return StoreResult.Succeeded(ToSnapshot(game, now));
        }
    }

    public StoreResult Resign(Guid id, ResignRequest request)
    {
        if (!_games.TryGetValue(id, out var game))
        {
            return StoreResult.Failed("Game was not found.");
        }

        if (!TryParseColor(request.PlayerColor, out var playerColor))
        {
            return StoreResult.Failed("PlayerColor must be White or Black.");
        }

        lock (game.SyncRoot)
        {
            var now = DateTimeOffset.UtcNow;
            RefreshClock(game, now);
            if (game.Status.IsTerminal())
            {
                return StoreResult.Failed("The game is already over.", ToSnapshot(game, now));
            }

            game.Status = playerColor == PieceColor.White ? GameStatus.WhiteResigned : GameStatus.BlackResigned;
            game.UpdatedAtUtc = now;
            return StoreResult.Succeeded(ToSnapshot(game, now));
        }
    }

    private static GameSnapshot ToSnapshot(GameSession game, DateTimeOffset now)
    {
        var whiteClock = game.WhiteRemaining;
        var blackClock = game.BlackRemaining;
        if (!game.Status.IsTerminal())
        {
            var elapsed = now - game.TurnStartedAtUtc;
            if (elapsed > TimeSpan.Zero)
            {
                if (game.Position.SideToMove == PieceColor.White)
                {
                    whiteClock -= elapsed;
                }
                else
                {
                    blackClock -= elapsed;
                }
            }
        }

        return new GameSnapshot(
            game.Id,
            game.WhitePlayer,
            game.BlackPlayer,
            game.Position.ToFen(),
            game.Position.SideToMove,
            game.Status,
            game.Moves.ToArray(),
            game.Status.IsTerminal()
                ? Array.Empty<string>()
                : ChessRules.GetLegalMoves(game.Position).Select(move => move.ToUci()).ToArray(),
            Math.Max(0, (long)whiteClock.TotalMilliseconds),
            Math.Max(0, (long)blackClock.TotalMilliseconds),
            (int)game.Increment.TotalSeconds,
            game.TurnStartedAtUtc,
            now);
    }

    private static void RefreshClock(GameSession game, DateTimeOffset now)
    {
        if (game.Status.IsTerminal())
        {
            return;
        }

        var elapsed = now - game.TurnStartedAtUtc;
        if (elapsed <= TimeSpan.Zero)
        {
            return;
        }

        if (game.Position.SideToMove == PieceColor.White)
        {
            game.WhiteRemaining -= elapsed;
            if (game.WhiteRemaining <= TimeSpan.Zero)
            {
                game.WhiteRemaining = TimeSpan.Zero;
                game.Status = GameStatus.WhiteFlagged;
            }
        }
        else
        {
            game.BlackRemaining -= elapsed;
            if (game.BlackRemaining <= TimeSpan.Zero)
            {
                game.BlackRemaining = TimeSpan.Zero;
                game.Status = GameStatus.BlackFlagged;
            }
        }

        game.TurnStartedAtUtc = now;
    }

    private static string CleanPlayerName(string? value, string fallback)
    {
        var name = value?.Trim();
        return string.IsNullOrWhiteSpace(name) ? fallback : name[..Math.Min(name.Length, 32)];
    }

    private static bool TryParseColor(string? value, out PieceColor color)
    {
        return Enum.TryParse(value, ignoreCase: true, out color)
            && color is PieceColor.White or PieceColor.Black;
    }

    private static bool TryParsePromotion(string? value, out PieceType? promotion)
    {
        promotion = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (value.Length != 1)
        {
            return false;
        }

        promotion = char.ToLowerInvariant(value[0]) switch
        {
            'q' => PieceType.Queen,
            'r' => PieceType.Rook,
            'b' => PieceType.Bishop,
            'n' => PieceType.Knight,
            _ => null
        };
        return promotion is not null;
    }

    private sealed class GameSession
    {
        public required Guid Id { get; init; }
        public required string WhitePlayer { get; init; }
        public required string BlackPlayer { get; init; }
        public required ChessPosition Position { get; set; }
        public required TimeSpan WhiteRemaining { get; set; }
        public required TimeSpan BlackRemaining { get; set; }
        public required TimeSpan Increment { get; init; }
        public required DateTimeOffset TurnStartedAtUtc { get; set; }
        public required DateTimeOffset UpdatedAtUtc { get; set; }
        public required GameStatus Status { get; set; }
        public List<GameMoveDto> Moves { get; } = [];
        public List<string> PositionHistory { get; } = [];
        public object SyncRoot { get; } = new();
    }
}
