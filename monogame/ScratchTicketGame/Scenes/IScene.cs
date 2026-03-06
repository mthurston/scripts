using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace ScratchTicketGame.Scenes;

public interface IScene
{
    void Update(GameTime gameTime);
    void Draw(SpriteBatch spriteBatch);
}
