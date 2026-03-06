namespace ScratchTicketGame.Models;

public enum TicketSymbol { Seven, Dollar, Star, Bar, Cherry }

public class TicketCell
{
    public TicketSymbol Symbol { get; init; }
    public bool IsScratched { get; set; }
}

public class Ticket
{
    public const decimal Cost = 1m;
    public const decimal WinPrize = 5m;

    public TicketCell[,] Cells { get; } = new TicketCell[3, 3];

    public bool IsFullyScratched =>
        Enumerable.Range(0, 3)
            .SelectMany(r => Enumerable.Range(0, 3).Select(c => Cells[r, c]))
            .All(cell => cell.IsScratched);

    public Ticket(Random rng)
    {
        var symbols = Enum.GetValues<TicketSymbol>();

        // 30% chance of being a winner
        bool isWinner = rng.NextDouble() < 0.30;

        if (isWinner)
        {
            // Pick winning symbol, place 3 of them randomly, fill rest freely
            var winSymbol = symbols[rng.Next(symbols.Length)];
            var positions = Enumerable.Range(0, 9).OrderBy(_ => rng.Next()).ToList();
            var winPositions = positions.Take(3).ToHashSet();

            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                {
                    int pos = r * 3 + c;
                    var sym = winPositions.Contains(pos) ? winSymbol : symbols[rng.Next(symbols.Length)];
                    Cells[r, c] = new TicketCell { Symbol = sym };
                }
        }
        else
        {
            // Generate cells ensuring no 3-match exists
            TicketSymbol[] grid;
            do
            {
                grid = Enumerable.Range(0, 9)
                    .Select(_ => symbols[rng.Next(symbols.Length)])
                    .ToArray();
            } while (HasThreeMatch(grid));

            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                    Cells[r, c] = new TicketCell { Symbol = grid[r * 3 + c] };
        }
    }

    public bool CheckWin()
    {
        var allCells = Enumerable.Range(0, 3)
            .SelectMany(r => Enumerable.Range(0, 3).Select(c => Cells[r, c]));
        return allCells
            .GroupBy(cell => cell.Symbol)
            .Any(g => g.Count() >= 3);
    }

    private static bool HasThreeMatch(TicketSymbol[] grid) =>
        grid.GroupBy(s => s).Any(g => g.Count() >= 3);
}
