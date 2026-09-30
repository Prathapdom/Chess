namespace Chess.Domain;

public readonly record struct Square(int File, int Rank)
{
    public int Index => (Rank * 8) + File;

    public string Name => $"{(char)('a' + File)}{Rank + 1}";

    public bool IsLight => ((File + Rank) & 1) == 1;

    public bool IsValid => File is >= 0 and < 8 && Rank is >= 0 and < 8;

    public static Square FromIndex(int index)
    {
        if (index is < 0 or >= 64)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return new Square(index % 8, index / 8);
    }

    public static bool TryParse(string? text, out Square square)
    {
        square = default;
        if (string.IsNullOrWhiteSpace(text) || text.Length != 2)
        {
            return false;
        }

        var file = char.ToLowerInvariant(text[0]) - 'a';
        var rank = text[1] - '1';
        square = new Square(file, rank);
        return square.IsValid;
    }

    public override string ToString() => Name;
}
