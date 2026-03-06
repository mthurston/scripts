using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using ScratchTicketGame.Models;
using ScratchTicketGame.Scenes;

namespace ScratchTicketGame;

public class Game1 : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch = null!;
    private SpriteFont _font = null!;

    public Texture2D WhiteTexture { get; private set; } = null!;
    public Player Player { get; } = new Player(startingBalance: 10m);

    private IScene _currentScene = null!;
    private readonly Random _rng = new();

    public Game1()
    {
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth  = 800,
            PreferredBackBufferHeight = 600,
        };
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        Window.Title = "Lucky Scratch — Ticket Vending Machine";
    }

    protected override void LoadContent()
    {
        _spriteBatch  = new SpriteBatch(GraphicsDevice);
        _font         = Content.Load<SpriteFont>("Fonts/GameFont");

        WhiteTexture = new Texture2D(GraphicsDevice, 1, 1);
        WhiteTexture.SetData(new[] { Color.White });

        _currentScene = new VendingMachineScene(this, _font, _rng);
    }

    public void SwitchToVending() =>
        _currentScene = new VendingMachineScene(this, _font, _rng);

    public void SwitchToScratch(Ticket ticket) =>
        _currentScene = new ScratchTicketScene(this, _font, ticket);

    protected override void Update(GameTime gameTime)
    {
        if (Keyboard.GetState().IsKeyDown(Keys.Escape))
            Exit();

        _currentScene.Update(gameTime);
        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        GraphicsDevice.Clear(new Color(18, 18, 28));
        _spriteBatch.Begin();
        _currentScene.Draw(_spriteBatch);
        _spriteBatch.End();
        base.Draw(gameTime);
    }
}
