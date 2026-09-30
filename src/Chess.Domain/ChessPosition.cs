using System.Text;

namespace Chess.Domain;

[Flags]
public enum CastlingRights
{
    None = 0,
    WhiteKingSide = 1,
    WhiteQueenSide = 2,
    BlackKingSide = 4,
    BlackQueenSide = 8
}

public sealed class ChessPosition
{
    private readonly Piece?[] _squares;

    public ChessPosition()
        : this(new Piece?[64], PieceColor.White, CastlingRights.None, null, 0, 1)
    {
    }

    private ChessPosition(
        Piece?[] squares,
        PieceColor sideToMove,
        CastlingRights castlingRights,
        Square? enPassantTarget,
        int halfmoveClock,
        int fullmoveNumber)
    {
        _squares = squares;
        SideToMove = sideToMove;
        CastlingRights = castlingRights;
        EnPassantTarget = enPassantTarget;
        HalfmoveClock = halfmoveClock;
        FullmoveNumber = fullmoveNumber;
    }

    public PieceColor SideToMove { get; internal set; }

    public CastlingRights CastlingRights { get; internal set; }

    public Square? EnPassantTarget { get; internal set; }

    public int HalfmoveClock { get; internal set; }

    public int FullmoveNumber { get; internal set; }

    public Piece? this[Square square]
    {
        get => square.IsValid ? _squares[square.Index] : null;
        internal set
        {
            if (!square.IsValid)
            {
                throw new ArgumentOutOfRangeException(nameof(square));
            }

            _squares[square.Index] = value;
        }
    }

    public IEnumerable<(Square Square, Piece Piece)> EnumeratePieces()
    {
        for (var index = 0; index < _squares.Length; index++)
        {
            if (_squares[index] is { } piece)
            {
                yield return (Square.FromIndex(index), piece);
            }
        }
    }

    public ChessPosition Clone() => new((Piece?[])_squares.Clone(), SideToMove, CastlingRights, EnPassantTarget, HalfmoveClock, FullmoveNumber);

    public string ToFen()
    {
        var board = new StringBuilder();
        for (var rank = 7; rank >= 0; rank--)
        {
            var empty = 0;
            for (var file = 0; file < 8; file++)
            {
                var piece = this[new Square(file, rank)];
                if (piece is null)
                {
                    empty++;
                    continue;
                }

                if (empty > 0)
                {
                    board.Append(empty);
                    empty = 0;
                }

                board.Append(piece.Value.FenSymbol);
            }

            if (empty > 0)
            {
                board.Append(empty);
            }

            if (rank > 0)
            {
                board.Append('/');
            }
        }

        var castling = CastlingRights == CastlingRights.None
            ? "-"
            : string.Concat(
                CastlingRights.HasFlag(CastlingRights.WhiteKingSide) ? "K" : string.Empty,
                CastlingRights.HasFlag(CastlingRights.WhiteQueenSide) ? "Q" : string.Empty,
                CastlingRights.HasFlag(CastlingRights.BlackKingSide) ? "k" : string.Empty,
                CastlingRights.HasFlag(CastlingRights.BlackQueenSide) ? "q" : string.Empty);

        return $"{board} {(SideToMove == PieceColor.White ? "w" : "b")} {castling} {(EnPassantTarget?.Name ?? "-")} {HalfmoveClock} {FullmoveNumber}";
    }

    public string PositionKey()
    {
        var fen = ToFen().Split(' ');
        return string.Join(' ', fen[0], fen[1], fen[2], fen[3]);
    }

    public static ChessPosition CreateInitial()
    {
        var position = new ChessPosition
        {
            CastlingRights = CastlingRights.WhiteKingSide
                | CastlingRights.WhiteQueenSide
                | CastlingRights.BlackKingSide
                | CastlingRights.BlackQueenSide
        };

        for (var file = 0; file < 8; file++)
        {
            position[new Square(file, 1)] = new Piece(PieceType.Pawn, PieceColor.White);
            position[new Square(file, 6)] = new Piece(PieceType.Pawn, PieceColor.Black);
        }

        var backRank = new[]
        {
            PieceType.Rook,
            PieceType.Knight,
            PieceType.Bishop,
            PieceType.Queen,
            PieceType.King,
            PieceType.Bishop,
            PieceType.Knight,
            PieceType.Rook
        };

        for (var file = 0; file < 8; file++)
        {
            position[new Square(file, 0)] = new Piece(backRank[file], PieceColor.White);
            position[new Square(file, 7)] = new Piece(backRank[file], PieceColor.Black);
        }

        return position;
    }

    public static ChessPosition FromFen(string fen)
    {
        if (string.IsNullOrWhiteSpace(fen))
        {
            throw new ArgumentException("FEN is required.", nameof(fen));
        }

        var fields = fen.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length < 4)
        {
            throw new FormatException("FEN must contain at least four fields.");
        }

        var position = new ChessPosition();
        var ranks = fields[0].Split('/');
        if (ranks.Length != 8)
        {
            throw new FormatException("FEN board must contain eight ranks.");
        }

        for (var fenRank = 0; fenRank < 8; fenRank++)
        {
            var file = 0;
            foreach (var symbol in ranks[fenRank])
            {
                if (char.IsDigit(symbol))
                {
                    file += symbol - '0';
                    continue;
                }

                if (file >= 8 || !Piece.TryFromFen(symbol, out var piece))
                {
                    throw new FormatException("FEN contains an invalid piece layout.");
                }

                position[new Square(file, 7 - fenRank)] = piece;
                file++;
            }

            if (file != 8)
            {
                throw new FormatException("FEN rank does not contain eight squares.");
            }
        }

        position.SideToMove = fields[1] switch
        {
            "w" => PieceColor.White,
            "b" => PieceColor.Black,
            _ => throw new FormatException("FEN side-to-move field is invalid.")
        };

        position.CastlingRights = ParseCastlingRights(fields[2]);
        position.EnPassantTarget = fields[3] == "-"
            ? null
            : Square.TryParse(fields[3], out var target)
                ? target
                : throw new FormatException("FEN en-passant field is invalid.");

        var halfmove = 0;
        if (fields.Length >= 5 && (!int.TryParse(fields[4], out halfmove) || halfmove < 0))
        {
            throw new FormatException("FEN halfmove field is invalid.");
        }

        var fullmove = 1;
        if (fields.Length >= 6 && (!int.TryParse(fields[5], out fullmove) || fullmove < 1))
        {
            throw new FormatException("FEN fullmove field is invalid.");
        }

        position.HalfmoveClock = halfmove;
        position.FullmoveNumber = fullmove;
        return position;
    }

    private static CastlingRights ParseCastlingRights(string value)
    {
        if (value == "-")
        {
            return CastlingRights.None;
        }

        var rights = CastlingRights.None;
        foreach (var symbol in value)
        {
            rights |= symbol switch
            {
                'K' => CastlingRights.WhiteKingSide,
                'Q' => CastlingRights.WhiteQueenSide,
                'k' => CastlingRights.BlackKingSide,
                'q' => CastlingRights.BlackQueenSide,
                _ => throw new FormatException("FEN castling field is invalid.")
            };
        }

        return rights;
    }
}
