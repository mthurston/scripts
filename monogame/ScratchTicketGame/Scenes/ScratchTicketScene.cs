using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using ScratchTicketGame.Models;

namespace ScratchTicketGame.Scenes;

public class ScratchTicketScene : IScene
{
    private readonly Game1 _game;
    private readonly SpriteFont _font;
    private readonly Ticket _ticket;

    private MouseState _prevMouse;
    private KeyboardState _prevKeyboard;

    private bool _resultProcessed;
    private string _resultMessage = "";
    private Color _resultColor = Color.White;

    // 3x3 grid layout — centered in 800x600 window
    private const int GridX = 202;
    private const int GridY = 175;
    private const int CellSize = 92;
    private const int CellPad = 10;

    private static readonly Dictionary<TicketSymbol, string> SymbolLabel = new()
    {
        [TicketSymbol.Seven] = " 7",
        [TicketSymbol.Dollar] = " $",
        [TicketSymbol.Star]   = " *",
        [TicketSymbol.Bar]    = "BAR",
        [TicketSymbol.Cherry] = " CH",
    };

    private static readonly Dictionary<TicketSymbol, Color> SymbolColor = new()
    {
        [TicketSymbol.Seven]  = Color.Red,
        [TicketSymbol.Dollar] = Color.Gold,
        [TicketSymbol.Star]   = Color.DeepSkyBlue,
        [TicketSymbol.Bar]    = Color.Orange,
        [TicketSymbol.Cherry] = Color.HotPink,
    };

    public ScratchTicketScene(Game1 game, SpriteFont font, Ticket ticket)
    {
        _game = game;
        _font = font;
        _ticket = ticket;
    }

    private Rectangle GetCellRect(int row, int col) => new(
        GridX + col * (CellSize + CellPad),
        GridY + row * (CellSize + CellPad),
        CellSize, CellSize);

    public void Update(GameTime gameTime)
    {
        var mouse = Mouse.GetState();
        var kb = Keyboard.GetState();

        // Click to scratch individual cells
        if (mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released)
        {
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                    if (!_ticket.Cells[r, c].IsScratched && GetCellRect(r, c).Contains(mouse.Position))
                        _ticket.Cells[r, c].IsScratched = true;
        }

        // S key to scratch all at once
        if (kb.IsKeyDown(Keys.S) && _prevKeyboard.IsKeyUp(Keys.S))
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < 3; c++)
                    _ticket.Cells[r, c].IsScratched = true;

        // Evaluate result when fully scratched
        if (_ticket.IsFullyScratched && !_resultProcessed)
        {
            _resultProcessed = true;
            if (_ticket.CheckWin())
            {
                _game.Player.AddWinnings(Ticket.WinPrize);
                _resultMessage = $"*** YOU WIN ${Ticket.WinPrize:F2}! ***";
                _resultColor = Color.Gold;
            }
            else
            {
                _resultMessage = "Better luck next time!";
                _resultColor = Color.IndianRed;
            }
        }

        // Return to vending machine after fully scratched
        if (_ticket.IsFullyScratched &&
            kb.IsKeyDown(Keys.Space) && _prevKeyboard.IsKeyUp(Keys.Space))
            _game.SwitchToVending();

        _prevMouse = mouse;
        _prevKeyboard = kb;
    }

    public void Draw(SpriteBatch sb)
    {
        var wt = _game.WhiteTexture;

        // Ticket background
        sb.Draw(wt, new Rectangle(148, 25, 504, 545), new Color(200, 180, 100));
        sb.Draw(wt, new Rectangle(153, 30, 494, 535), new Color(255, 250, 195));

        // Ticket header
        sb.Draw(wt, new Rectangle(153, 30, 494, 55), new Color(180, 20, 20));
        DrawText(sb, "LUCKY SCRATCH", new Vector2(210, 40), Color.Gold, 1.4f);

        DrawText(sb, "Match 3 symbols to WIN!", new Vector2(205, 98), new Color(120, 0, 0), 0.9f);
        DrawText(sb, $"Prize: ${Ticket.WinPrize:F2}", new Vector2(340, 135), new Color(150, 0, 0), 0.85f);

        // Scratch grid
        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 3; c++)
            {
                var rect = GetCellRect(r, c);
                var cell = _ticket.Cells[r, c];

                if (cell.IsScratched)
                {
                    // Revealed cell
                    sb.Draw(wt, rect, new Color(235, 220, 150));
                    sb.Draw(wt, new Rectangle(rect.X + 3, rect.Y + 3, rect.Width - 6, rect.Height - 6),
                        new Color(255, 248, 195));
                    var label = SymbolLabel[cell.Symbol];
                    var color = SymbolColor[cell.Symbol];
                    DrawText(sb, label, new Vector2(rect.X + 14, rect.Y + 22), color, 2.0f);
                }
                else
                {
                    // Un-scratched cell (silver scratch coating)
                    sb.Draw(wt, rect, new Color(140, 140, 145));
                    sb.Draw(wt, new Rectangle(rect.X + 3, rect.Y + 3, rect.Width - 6, rect.Height - 6),
                        new Color(165, 165, 170));
                    DrawText(sb, "SCRATCH", new Vector2(rect.X + 10, rect.Y + 36), new Color(100, 100, 105), 0.75f);
                }
            }
        }

        // Result banner
        if (_ticket.IsFullyScratched)
        {
            sb.Draw(wt, new Rectangle(153, 495, 494, 45), new Color(30, 30, 30, 200));
            DrawText(sb, _resultMessage, new Vector2(170, 505), _resultColor, 1.1f);
            DrawText(sb, "[SPACE] Back to Machine", new Vector2(200, 545), new Color(100, 0, 0), 0.8f);
        }
        else
        {
            DrawText(sb, "Click cells  or  [S] Scratch All", new Vector2(175, 515), new Color(120, 0, 0), 0.85f);
        }

        // HUD — balance overlay top-left
        sb.Draw(wt, new Rectangle(0, 0, 145, 28), new Color(0, 0, 0, 160));
        DrawText(sb, $"Balance: ${_game.Player.Balance:F2}", new Vector2(6, 6), Color.White, 0.85f);

        // ESC hint
        DrawText(sb, "[ESC] Quit", new Vector2(650, 575), new Color(80, 80, 100), 0.75f);
    }

    private void DrawText(SpriteBatch sb, string text, Vector2 pos, Color color, float scale = 1f) =>
        sb.DrawString(_font, text, pos, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
}
