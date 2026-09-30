using Chess.Domain;

namespace Chess.Api.Models;

public sealed record CreateGameRequest(
    string? WhitePlayer,
    string? BlackPlayer,
    int? InitialMinutes,
    int? IncrementSeconds);

public sealed record MoveRequest(
    string PlayerColor,
    string From,
    string To,
    string? Promotion);

public sealed record ResignRequest(string PlayerColor);

public sealed record GameMoveDto(
    string Uci,
    PieceColor PlayerColor,
    DateTimeOffset PlayedAtUtc);

public sealed record GameSnapshot(
    Guid Id,
    string WhitePlayer,
    string BlackPlayer,
    string Fen,
    PieceColor SideToMove,
    GameStatus Status,
    IReadOnlyList<GameMoveDto> Moves,
    IReadOnlyList<string> LegalMoves,
    long WhiteClockMs,
    long BlackClockMs,
    int IncrementSeconds,
    DateTimeOffset TurnStartedAtUtc,
    DateTimeOffset ServerTimeUtc);

public sealed record StoreResult(bool Success, string? Error, GameSnapshot? Snapshot)
{
    public static StoreResult Failed(string error, GameSnapshot? snapshot = null) => new(false, error, snapshot);

    public static StoreResult Succeeded(GameSnapshot snapshot) => new(true, null, snapshot);
}
