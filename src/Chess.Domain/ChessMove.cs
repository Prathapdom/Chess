namespace Chess.Domain;

public readonly record struct ChessMove(Square From, Square To, PieceType? Promotion = null)
{
    public string ToUci()
    {
        var promotion = Promotion switch
        {
            PieceType.Knight => "n",
            PieceType.Bishop => "b",
            PieceType.Rook => "r",
            PieceType.Queen => "q",
            _ => string.Empty
        };

        return $"{From.Name}{To.Name}{promotion}";
    }

    public static bool TryParseUci(string? text, out ChessMove move)
    {
        move = default;
        if (string.IsNullOrWhiteSpace(text) || (text.Length != 4 && text.Length != 5))
        {
            return false;
        }

        if (!Square.TryParse(text[..2], out var from) || !Square.TryParse(text[2..4], out var to))
        {
            return false;
        }

        PieceType? promotion = null;
        if (text.Length == 5)
        {
            promotion = char.ToLowerInvariant(text[4]) switch
            {
                'n' => PieceType.Knight,
                'b' => PieceType.Bishop,
                'r' => PieceType.Rook,
                'q' => PieceType.Queen,
                _ => null
            };

            if (promotion is null)
            {
                return false;
            }
        }

        move = new ChessMove(from, to, promotion);
        return true;
    }

    public override string ToString() => ToUci();
}
