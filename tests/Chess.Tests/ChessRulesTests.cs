using Chess.Domain;
using Xunit;

namespace Chess.Tests;

public sealed class ChessRulesTests
{
    [Fact]
    public void InitialPositionHasTwentyLegalMoves()
    {
        var position = ChessPosition.CreateInitial();

        Assert.Equal(20, ChessRules.GetLegalMoves(position).Count);
    }

    [Fact]
    public void PawnDoubleStepUpdatesEnPassantTarget()
    {
        var position = ChessPosition.CreateInitial();

        Assert.True(ChessMove.TryParseUci("e2e4", out var move));
        Assert.True(ChessRules.TryApplyMove(position, move, out var next, out _));
        Assert.Equal("rnbqkbnr/pppppppp/8/8/4P3/8/PPPP1PPP/RNBQKBNR b KQkq e3 0 1", next.ToFen());
    }

    [Fact]
    public void CastlingMovesTheRook()
    {
        var position = ChessPosition.FromFen("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");

        Assert.Contains(new ChessMove(new Square(4, 0), new Square(6, 0)), ChessRules.GetLegalMoves(position));
        Assert.True(ChessRules.TryApplyMove(position, new ChessMove(new Square(4, 0), new Square(6, 0)), out var next, out _));
        Assert.Equal(new Piece(PieceType.King, PieceColor.White), next[new Square(6, 0)]);
        Assert.Equal(new Piece(PieceType.Rook, PieceColor.White), next[new Square(5, 0)]);
        Assert.Equal(CastlingRights.BlackKingSide | CastlingRights.BlackQueenSide, next.CastlingRights);
    }

    [Fact]
    public void EnPassantCapturesThePawnBehindTheTargetSquare()
    {
        var position = ChessPosition.FromFen("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 1");

        var move = new ChessMove(new Square(4, 4), new Square(3, 5));
        Assert.True(ChessRules.TryApplyMove(position, move, out var next, out _));
        Assert.Equal(new Piece(PieceType.Pawn, PieceColor.White), next[new Square(3, 5)]);
        Assert.Null(next[new Square(3, 4)]);
    }

    [Fact]
    public void PromotionCreatesTheRequestedPiece()
    {
        var position = ChessPosition.FromFen("4k3/P7/8/8/8/8/8/4K3 w - - 0 1");
        var move = new ChessMove(new Square(0, 6), new Square(0, 7), PieceType.Knight);

        Assert.True(ChessRules.TryApplyMove(position, move, out var next, out _));
        Assert.Equal(new Piece(PieceType.Knight, PieceColor.White), next[new Square(0, 7)]);
    }

    [Fact]
    public void FoolMateIsCheckmate()
    {
        var position = ChessPosition.CreateInitial();
        position = Apply(position, "f2f3");
        position = Apply(position, "e7e5");
        position = Apply(position, "g2g4");
        position = Apply(position, "d8h4");

        Assert.Equal(GameStatus.Checkmate, ChessRules.GetStatus(position));
    }

    [Fact]
    public void StalemateIsDetected()
    {
        var position = ChessPosition.FromFen("7k/5Q2/6K1/8/8/8/8/8 b - - 0 1");

        Assert.Equal(GameStatus.Stalemate, ChessRules.GetStatus(position));
    }

    [Fact]
    public void InsufficientMaterialIsDetected()
    {
        var position = ChessPosition.FromFen("4k3/8/8/8/8/8/6N1/4K3 w - - 0 1");

        Assert.Equal(GameStatus.DrawInsufficientMaterial, ChessRules.GetStatus(position));
    }

    [Fact]
    public void MovesThatExposeTheKingAreRejected()
    {
        var position = ChessPosition.FromFen("4r1k1/8/8/8/8/8/4R3/4K3 w - - 0 1");

        Assert.False(ChessRules.TryApplyMove(position, new ChessMove(new Square(4, 1), new Square(5, 1)), out _, out _));
    }

    private static ChessPosition Apply(ChessPosition position, string uci)
    {
        Assert.True(ChessMove.TryParseUci(uci, out var move));
        Assert.True(ChessRules.TryApplyMove(position, move, out var next, out _));
        return next;
    }
}
