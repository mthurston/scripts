namespace ScratchTicketGame.Models;

public class Player
{
    public decimal Balance { get; private set; }
    public int TicketsBought { get; private set; }
    public int Wins { get; private set; }

    public Player(decimal startingBalance = 10m)
    {
        Balance = startingBalance;
    }

    public bool TryBuyTicket(decimal cost)
    {
        if (Balance < cost) return false;
        Balance -= cost;
        TicketsBought++;
        return true;
    }

    public void AddWinnings(decimal amount)
    {
        Balance += amount;
        Wins++;
    }
}
