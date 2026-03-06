using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using ScratchTicketGame.Models;

namespace ScratchTicketGame.Scenes;

public class VendingMachineScene : IScene
{
    private readonly Game1 _game;
    private readonly SpriteFont _font;
    private readonly Random _rng;

    private KeyboardState _prevKeyboard;
    private MouseState _prevMouse;

    private readonly Rectangle _buyButton = new(300, 430, 200, 55);

    public VendingMachineScene(Game1 game, SpriteFont font, Random rng)
    {
        _game = game;
        _font = font;
        _rng = rng;
    }

    public void Update(GameTime gameTime)
    {
        var kb = Keyboard.GetState();
        var mouse = Mouse.GetState();

        bool buyPressed =
            (kb.IsKeyDown(Keys.Space) && _prevKeyboard.IsKeyUp(Keys.Space)) ||
            (mouse.LeftButton == ButtonState.Pressed &&
             _prevMouse.LeftButton == ButtonState.Released &&
             _buyButton.Contains(mouse.Position));

        if (buyPressed && _game.Player.TryBuyTicket(Ticket.Cost))
            _game.SwitchToScratch(new Ticket(_rng));

        _prevKeyboard = kb;
        _prevMouse = mouse;
    }

    public void Draw(SpriteBatch sb)
    {
        var wt = _game.WhiteTexture;

        // Machine body
        sb.Draw(wt, new Rectangle(100, 30, 600, 540), new Color(55, 55, 75));
        sb.Draw(wt, new Rectangle(108, 38, 584, 524), new Color(40, 40, 58));

        // Title bar
        sb.Draw(wt, new Rectangle(108, 38, 584, 60), new Color(30, 30, 50));
        DrawText(sb, "LUCKY SCRATCH", new Vector2(220, 48), Color.Gold, 1.5f);

        // Subtitle
        DrawText(sb, "TICKET VENDING MACHINE", new Vector2(195, 115), Color.Yellow, 0.95f);

        // Ticket display window
        sb.Draw(wt, new Rectangle(185, 155, 430, 195), new Color(25, 25, 38));
        sb.Draw(wt, new Rectangle(190, 160, 420, 185), new Color(255, 255, 200, 30));

        DrawText(sb, "SCRATCH TICKET", new Vector2(255, 175), Color.LightYellow, 1.1f);
        DrawText(sb, $"PRICE:  ${Ticket.Cost:F2}", new Vector2(255, 220), Color.Cyan, 1.0f);
        DrawText(sb, $"PRIZE:  ${Ticket.WinPrize:F2}", new Vector2(255, 255), Color.LightGreen, 1.0f);
        DrawText(sb, "Match 3 symbols to WIN!", new Vector2(235, 295), Color.LightGray, 0.85f);

        // Balance display
        sb.Draw(wt, new Rectangle(185, 370, 430, 45), new Color(20, 20, 35));
        DrawText(sb, $"BALANCE:  ${_game.Player.Balance:F2}", new Vector2(265, 381), Color.White, 1.0f);

        // Buy button
        bool canAfford = _game.Player.Balance >= Ticket.Cost;
        var btnColor = canAfford ? new Color(0, 160, 0) : new Color(80, 80, 80);
        sb.Draw(wt, _buyButton, btnColor);
        if (canAfford)
        {
            sb.Draw(wt, new Rectangle(302, 432, 196, 51), new Color(0, 200, 0, 160));
            DrawText(sb, "BUY  [SPACE]", new Vector2(325, 448), Color.White, 1.0f);
        }
        else
        {
            DrawText(sb, "INSUFFICIENT FUNDS", new Vector2(310, 448), Color.LightGray, 0.85f);
        }

        // Stats footer
        DrawText(sb, $"Tickets: {_game.Player.TicketsBought}     Wins: {_game.Player.Wins}",
            new Vector2(230, 515), new Color(130, 130, 150), 0.85f);

        // ESC hint
        DrawText(sb, "[ESC] Quit", new Vector2(650, 575), new Color(80, 80, 100), 0.75f);
    }

    private void DrawText(SpriteBatch sb, string text, Vector2 pos, Color color, float scale = 1f) =>
        sb.DrawString(_font, text, pos, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
}
