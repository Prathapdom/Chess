namespace Chess.Domain;

public static class ChessRules
{
    private static readonly (int File, int Rank)[] KnightOffsets =
    [
        (1, 2), (2, 1), (2, -1), (1, -2),
        (-1, -2), (-2, -1), (-2, 1), (-1, 2)
    ];

    private static readonly (int File, int Rank)[] KingOffsets =
    [
        (1, 1), (1, 0), (1, -1), (0, -1),
        (-1, -1), (-1, 0), (-1, 1), (0, 1)
    ];

    private static readonly (int File, int Rank)[] BishopDirections =
    [
        (1, 1), (1, -1), (-1, 1), (-1, -1)
    ];

    private static readonly (int File, int Rank)[] RookDirections =
    [
        (1, 0), (-1, 0), (0, 1), (0, -1)
    ];

    public static IReadOnlyList<ChessMove> GetLegalMoves(ChessPosition position)
    {
        ArgumentNullException.ThrowIfNull(position);

        var moves = new List<ChessMove>();
        foreach (var (square, piece) in position.EnumeratePieces())
        {
            if (piece.Color != position.SideToMove)
            {
                continue;
            }

            AddPseudoLegalMoves(position, square, piece, moves);
        }

        var legal = new List<ChessMove>(moves.Count);
        foreach (var move in moves)
        {
            var next = ApplyUnchecked(position, move);
            if (!IsInCheck(next, position.SideToMove))
            {
                legal.Add(move);
            }
        }

        return legal;
    }

    public static IReadOnlyList<ChessMove> GetLegalMoves(ChessPosition position, Square from)
    {
        return GetLegalMoves(position).Where(move => move.From == from).ToArray();
    }

    public static bool TryApplyMove(ChessPosition position, ChessMove requested, out ChessPosition next, out ChessMove applied)
    {
        ArgumentNullException.ThrowIfNull(position);

        foreach (var legalMove in GetLegalMoves(position))
        {
            if (legalMove.From == requested.From
                && legalMove.To == requested.To
                && legalMove.Promotion == requested.Promotion)
            {
                next = ApplyUnchecked(position, legalMove);
                applied = legalMove;
                return true;
            }
        }

        next = position;
        applied = default;
        return false;
    }

    public static bool IsInCheck(ChessPosition position, PieceColor color)
    {
        var king = position.EnumeratePieces()
            .FirstOrDefault(item => item.Piece.Type == PieceType.King && item.Piece.Color == color);

        if (king.Piece.Type != PieceType.King || king.Piece.Color != color)
        {
            return true;
        }

        return IsSquareAttacked(position, king.Square, color == PieceColor.White ? PieceColor.Black : PieceColor.White);
    }

    public static bool IsSquareAttacked(ChessPosition position, Square target, PieceColor byColor)
    {
        ArgumentNullException.ThrowIfNull(position);

        var pawnRank = target.Rank + (byColor == PieceColor.White ? -1 : 1);
        foreach (var fileOffset in new[] { -1, 1 })
        {
            var pawnSquare = new Square(target.File + fileOffset, pawnRank);
            if (pawnSquare.IsValid && position[pawnSquare] is { Type: PieceType.Pawn, Color: var color } && color == byColor)
            {
                return true;
            }
        }

        foreach (var (fileOffset, rankOffset) in KnightOffsets)
        {
            var square = new Square(target.File + fileOffset, target.Rank + rankOffset);
            if (square.IsValid && position[square] is { Type: PieceType.Knight, Color: var color } && color == byColor)
            {
                return true;
            }
        }

        foreach (var (fileOffset, rankOffset) in KingOffsets)
        {
            var square = new Square(target.File + fileOffset, target.Rank + rankOffset);
            if (square.IsValid && position[square] is { Type: PieceType.King, Color: var color } && color == byColor)
            {
                return true;
            }
        }

        if (IsAttackedAlong(position, target, byColor, BishopDirections, PieceType.Bishop, PieceType.Queen)
            || IsAttackedAlong(position, target, byColor, RookDirections, PieceType.Rook, PieceType.Queen))
        {
            return true;
        }

        return false;
    }

    public static GameStatus GetStatus(ChessPosition position, IReadOnlyList<string>? positionHistory = null)
    {
        ArgumentNullException.ThrowIfNull(position);

        var inCheck = IsInCheck(position, position.SideToMove);
        var legalMoves = GetLegalMoves(position);
        if (legalMoves.Count == 0)
        {
            return inCheck ? GameStatus.Checkmate : GameStatus.Stalemate;
        }

        if (position.HalfmoveClock >= 100)
        {
            return GameStatus.DrawFiftyMove;
        }

        if (IsInsufficientMaterial(position))
        {
            return GameStatus.DrawInsufficientMaterial;
        }

        if (positionHistory is not null && positionHistory.Count(key => key == position.PositionKey()) >= 3)
        {
            return GameStatus.DrawRepetition;
        }

        return inCheck ? GameStatus.Check : GameStatus.InProgress;
    }

    public static ChessPosition ApplyUnchecked(ChessPosition position, ChessMove move)
    {
        ArgumentNullException.ThrowIfNull(position);

        var movingPiece = position[move.From]
            ?? throw new InvalidOperationException($"There is no piece on {move.From}.");

        var next = position.Clone();
        var capturedPiece = next[move.To];
        next[move.From] = null;

        var isPawn = movingPiece.Type == PieceType.Pawn;
        var isCapture = capturedPiece is not null;

        if (isPawn
            && position.EnPassantTarget == move.To
            && move.From.File != move.To.File
            && capturedPiece is null)
        {
            var capturedPawnSquare = new Square(move.To.File, move.To.Rank + (movingPiece.Color == PieceColor.White ? -1 : 1));
            if (next[capturedPawnSquare] is { Type: PieceType.Pawn, Color: var capturedColor }
                && capturedColor != movingPiece.Color)
            {
                next[capturedPawnSquare] = null;
                isCapture = true;
            }
        }

        if (movingPiece.Type == PieceType.King && Math.Abs(move.To.File - move.From.File) == 2)
        {
            var rookFrom = move.To.File > move.From.File
                ? new Square(7, move.From.Rank)
                : new Square(0, move.From.Rank);
            var rookTo = move.To.File > move.From.File
                ? new Square(5, move.From.Rank)
                : new Square(3, move.From.Rank);

            if (next[rookFrom] is { Type: PieceType.Rook, Color: var rookColor } && rookColor == movingPiece.Color)
            {
                next[rookTo] = next[rookFrom];
                next[rookFrom] = null;
            }
        }

        var placedPiece = move.Promotion is { } promotion
            ? new Piece(promotion, movingPiece.Color)
            : movingPiece;
        next[move.To] = placedPiece;

        next.CastlingRights = UpdateCastlingRights(position.CastlingRights, movingPiece, move.From, capturedPiece, move.To);
        next.EnPassantTarget = isPawn && Math.Abs(move.To.Rank - move.From.Rank) == 2
            ? new Square(move.From.File, (move.From.Rank + move.To.Rank) / 2)
            : null;
        next.HalfmoveClock = isPawn || isCapture ? 0 : position.HalfmoveClock + 1;
        next.FullmoveNumber = position.SideToMove == PieceColor.Black ? position.FullmoveNumber + 1 : position.FullmoveNumber;
        next.SideToMove = position.SideToMove == PieceColor.White ? PieceColor.Black : PieceColor.White;
        return next;
    }

    private static void AddPseudoLegalMoves(ChessPosition position, Square from, Piece piece, ICollection<ChessMove> moves)
    {
        switch (piece.Type)
        {
            case PieceType.Pawn:
                AddPawnMoves(position, from, piece.Color, moves);
                break;
            case PieceType.Knight:
                foreach (var (fileOffset, rankOffset) in KnightOffsets)
                {
                    AddTargetMove(position, from, new Square(from.File + fileOffset, from.Rank + rankOffset), piece.Color, moves);
                }

                break;
            case PieceType.Bishop:
                AddSlidingMoves(position, from, piece.Color, BishopDirections, moves);
                break;
            case PieceType.Rook:
                AddSlidingMoves(position, from, piece.Color, RookDirections, moves);
                break;
            case PieceType.Queen:
                AddSlidingMoves(position, from, piece.Color, BishopDirections, moves);
                AddSlidingMoves(position, from, piece.Color, RookDirections, moves);
                break;
            case PieceType.King:
                foreach (var (fileOffset, rankOffset) in KingOffsets)
                {
                    AddTargetMove(position, from, new Square(from.File + fileOffset, from.Rank + rankOffset), piece.Color, moves);
                }

                AddCastlingMoves(position, from, piece.Color, moves);
                break;
        }
    }

    private static void AddPawnMoves(ChessPosition position, Square from, PieceColor color, ICollection<ChessMove> moves)
    {
        var direction = color == PieceColor.White ? 1 : -1;
        var startRank = color == PieceColor.White ? 1 : 6;
        var promotionRank = color == PieceColor.White ? 7 : 0;
        var oneStep = new Square(from.File, from.Rank + direction);

        if (oneStep.IsValid && position[oneStep] is null)
        {
            AddPawnMoveOrPromotions(from, oneStep, promotionRank, moves);
            var twoStep = new Square(from.File, from.Rank + (direction * 2));
            if (from.Rank == startRank && position[twoStep] is null)
            {
                moves.Add(new ChessMove(from, twoStep));
            }
        }

        foreach (var fileOffset in new[] { -1, 1 })
        {
            var target = new Square(from.File + fileOffset, from.Rank + direction);
            if (!target.IsValid)
            {
                continue;
            }

            var targetPiece = position[target];
            if (targetPiece is { Color: var targetColor } && targetColor != color && targetPiece.Value.Type != PieceType.King)
            {
                AddPawnMoveOrPromotions(from, target, promotionRank, moves);
            }
            else if (targetPiece is null && position.EnPassantTarget == target)
            {
                moves.Add(new ChessMove(from, target));
            }
        }
    }

    private static void AddPawnMoveOrPromotions(Square from, Square to, int promotionRank, ICollection<ChessMove> moves)
    {
        if (to.Rank != promotionRank)
        {
            moves.Add(new ChessMove(from, to));
            return;
        }

        moves.Add(new ChessMove(from, to, PieceType.Queen));
        moves.Add(new ChessMove(from, to, PieceType.Rook));
        moves.Add(new ChessMove(from, to, PieceType.Bishop));
        moves.Add(new ChessMove(from, to, PieceType.Knight));
    }

    private static void AddTargetMove(ChessPosition position, Square from, Square to, PieceColor color, ICollection<ChessMove> moves)
    {
        if (!to.IsValid)
        {
            return;
        }

        var targetPiece = position[to];
        if (targetPiece is null || (targetPiece.Value.Color != color && targetPiece.Value.Type != PieceType.King))
        {
            moves.Add(new ChessMove(from, to));
        }
    }

    private static void AddSlidingMoves(
        ChessPosition position,
        Square from,
        PieceColor color,
        IEnumerable<(int File, int Rank)> directions,
        ICollection<ChessMove> moves)
    {
        foreach (var (fileOffset, rankOffset) in directions)
        {
            var target = new Square(from.File + fileOffset, from.Rank + rankOffset);
            while (target.IsValid)
            {
                var targetPiece = position[target];
                if (targetPiece is null)
                {
                    moves.Add(new ChessMove(from, target));
                }
                else
                {
                    if (targetPiece.Value.Color != color && targetPiece.Value.Type != PieceType.King)
                    {
                        moves.Add(new ChessMove(from, target));
                    }

                    break;
                }

                target = new Square(target.File + fileOffset, target.Rank + rankOffset);
            }
        }
    }

    private static void AddCastlingMoves(ChessPosition position, Square from, PieceColor color, ICollection<ChessMove> moves)
    {
        var opponent = color == PieceColor.White ? PieceColor.Black : PieceColor.White;
        var homeRank = color == PieceColor.White ? 0 : 7;
        var kingHome = new Square(4, homeRank);
        if (from != kingHome || IsSquareAttacked(position, kingHome, opponent))
        {
            return;
        }

        var kingSide = color == PieceColor.White ? CastlingRights.WhiteKingSide : CastlingRights.BlackKingSide;
        if (position.CastlingRights.HasFlag(kingSide)
            && position[new Square(5, homeRank)] is null
            && position[new Square(6, homeRank)] is null
            && position[new Square(7, homeRank)] is { Type: PieceType.Rook, Color: var rookColor } && rookColor == color
            && !IsSquareAttacked(position, new Square(5, homeRank), opponent)
            && !IsSquareAttacked(position, new Square(6, homeRank), opponent))
        {
            moves.Add(new ChessMove(from, new Square(6, homeRank)));
        }

        var queenSide = color == PieceColor.White ? CastlingRights.WhiteQueenSide : CastlingRights.BlackQueenSide;
        if (position.CastlingRights.HasFlag(queenSide)
            && position[new Square(1, homeRank)] is null
            && position[new Square(2, homeRank)] is null
            && position[new Square(3, homeRank)] is null
            && position[new Square(0, homeRank)] is { Type: PieceType.Rook, Color: var queenRookColor } && queenRookColor == color
            && !IsSquareAttacked(position, new Square(3, homeRank), opponent)
            && !IsSquareAttacked(position, new Square(2, homeRank), opponent))
        {
            moves.Add(new ChessMove(from, new Square(2, homeRank)));
        }
    }

    private static bool IsAttackedAlong(
        ChessPosition position,
        Square target,
        PieceColor byColor,
        IEnumerable<(int File, int Rank)> directions,
        PieceType primary,
        PieceType secondary)
    {
        foreach (var (fileOffset, rankOffset) in directions)
        {
            var square = new Square(target.File + fileOffset, target.Rank + rankOffset);
            while (square.IsValid)
            {
                if (position[square] is { } piece)
                {
                    if (piece.Color == byColor && (piece.Type == primary || piece.Type == secondary))
                    {
                        return true;
                    }

                    break;
                }

                square = new Square(square.File + fileOffset, square.Rank + rankOffset);
            }
        }

        return false;
    }

    private static CastlingRights UpdateCastlingRights(
        CastlingRights rights,
        Piece movingPiece,
        Square from,
        Piece? capturedPiece,
        Square to)
    {
        if (movingPiece.Type == PieceType.King)
        {
            rights &= movingPiece.Color == PieceColor.White
                ? ~(CastlingRights.WhiteKingSide | CastlingRights.WhiteQueenSide)
                : ~(CastlingRights.BlackKingSide | CastlingRights.BlackQueenSide);
        }

        if (movingPiece.Type == PieceType.Rook)
        {
            rights = RemoveRookRight(rights, movingPiece.Color, from);
        }

        if (capturedPiece is { Type: PieceType.Rook } capturedRook)
        {
            rights = RemoveRookRight(rights, capturedRook.Color, to);
        }

        return rights;
    }

    private static CastlingRights RemoveRookRight(CastlingRights rights, PieceColor color, Square square)
    {
        if (color == PieceColor.White && square == new Square(0, 0))
        {
            rights &= ~CastlingRights.WhiteQueenSide;
        }
        else if (color == PieceColor.White && square == new Square(7, 0))
        {
            rights &= ~CastlingRights.WhiteKingSide;
        }
        else if (color == PieceColor.Black && square == new Square(0, 7))
        {
            rights &= ~CastlingRights.BlackQueenSide;
        }
        else if (color == PieceColor.Black && square == new Square(7, 7))
        {
            rights &= ~CastlingRights.BlackKingSide;
        }

        return rights;
    }

    private static bool IsInsufficientMaterial(ChessPosition position)
    {
        var nonKings = position.EnumeratePieces()
            .Where(item => item.Piece.Type != PieceType.King)
            .ToArray();

        if (nonKings.Any(item => item.Piece.Type is PieceType.Pawn or PieceType.Rook or PieceType.Queen))
        {
            return false;
        }

        if (nonKings.Length <= 1)
        {
            return true;
        }

        return nonKings.Length == 2
            && nonKings.All(item => item.Piece.Type == PieceType.Bishop)
            && nonKings[0].Square.IsLight == nonKings[1].Square.IsLight;
    }
}
