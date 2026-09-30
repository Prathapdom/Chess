namespace Chess.Domain;

public enum PieceColor
{
    White,
    Black
}

public enum PieceType
{
    Pawn,
    Knight,
    Bishop,
    Rook,
    Queen,
    King
}

public readonly record struct Piece(PieceType Type, PieceColor Color)
{
    public PieceColor OpponentColor => Color == PieceColor.White ? PieceColor.Black : PieceColor.White;

    public char FenSymbol
    {
        get
        {
            var symbol = Type switch
            {
                PieceType.Pawn => 'p',
                PieceType.Knight => 'n',
                PieceType.Bishop => 'b',
                PieceType.Rook => 'r',
                PieceType.Queen => 'q',
                PieceType.King => 'k',
                _ => throw new ArgumentOutOfRangeException()
            };

            return Color == PieceColor.White ? char.ToUpperInvariant(symbol) : symbol;
        }
    }

    public string UnicodeSymbol => (Color, Type) switch
    {
        (PieceColor.White, PieceType.King) => "♔",
        (PieceColor.White, PieceType.Queen) => "♕",
        (PieceColor.White, PieceType.Rook) => "♖",
        (PieceColor.White, PieceType.Bishop) => "♗",
        (PieceColor.White, PieceType.Knight) => "♘",
        (PieceColor.White, PieceType.Pawn) => "♙",
        (PieceColor.Black, PieceType.King) => "♚",
        (PieceColor.Black, PieceType.Queen) => "♛",
        (PieceColor.Black, PieceType.Rook) => "♜",
        (PieceColor.Black, PieceType.Bishop) => "♝",
        (PieceColor.Black, PieceType.Knight) => "♞",
        (PieceColor.Black, PieceType.Pawn) => "♟",
        _ => "?"
    };

    public static bool TryFromFen(char symbol, out Piece piece)
    {
        piece = default;
        var color = char.IsUpper(symbol) ? PieceColor.White : PieceColor.Black;
        var type = char.ToLowerInvariant(symbol) switch
        {
            'p' => PieceType.Pawn,
            'n' => PieceType.Knight,
            'b' => PieceType.Bishop,
            'r' => PieceType.Rook,
            'q' => PieceType.Queen,
            'k' => PieceType.King,
            _ => (PieceType?)null
        };

        if (type is null)
        {
            return false;
        }

        piece = new Piece(type.Value, color);
        return true;
    }

    public override string ToString() => FenSymbol.ToString();
}
