using Chess.Domain;

namespace Chess.Desktop;

internal sealed record CreateGameRequestDto(
    string? WhitePlayer,
    string? BlackPlayer,
    int? InitialMinutes,
    int? IncrementSeconds);

internal sealed record MoveRequestDto(
    string PlayerColor,
    string From,
    string To,
    string? Promotion);

internal sealed record ResignRequestDto(string PlayerColor);

internal sealed record GameMoveDto(
    string Uci,
    PieceColor PlayerColor,
    DateTimeOffset PlayedAtUtc);

internal sealed record GameSnapshotDto(
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
