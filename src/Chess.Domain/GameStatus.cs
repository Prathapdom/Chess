namespace Chess.Domain;

public enum GameStatus
{
    InProgress,
    Check,
    Checkmate,
    Stalemate,
    DrawFiftyMove,
    DrawRepetition,
    DrawInsufficientMaterial,
    WhiteFlagged,
    BlackFlagged,
    WhiteResigned,
    BlackResigned
}

public static class GameStatusExtensions
{
    public static bool IsTerminal(this GameStatus status) => status is not (GameStatus.InProgress or GameStatus.Check);
}
