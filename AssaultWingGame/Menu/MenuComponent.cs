using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace AW2.Menu
{
    /// <summary>
    /// A component in the menu system.
    /// </summary>
    public abstract class MenuComponent
    {
        /// <summary>
        /// The menu system of which this component is part of.
        /// </summary>
        public MenuEngineImpl MenuEngine { get; private set; }

        /// <summary>
        /// Does the menu component react to input.
        /// </summary>
        public virtual bool Active { set; get; }

        /// <summary>
        /// The center of the menu component in menu system coordinates.
        /// </summary>
        /// This is a good place to center the menu view to when the menu component
        /// is to be seen well on the screen.
        public abstract Vector2 Center { get; }

        /// <summary>
        /// Creates a new menu component for a menu system.
        /// </summary>
        /// <param name="menuEngine">The menu system.</param>
        public MenuComponent(MenuEngineImpl menuEngine)
        {
            MenuEngine = menuEngine;
        }

        /// <summary>
        /// Called when graphics resources need to be loaded.
        /// </summary>
        public virtual void LoadContent() { }

        /// <summary>
        /// Called when graphics resources need to be unloaded.
        /// </summary>
        public virtual void UnloadContent() { }

        /// <summary>
        /// Updates the menu component.
        /// </summary>
        public abstract void Update();

        /// <summary>
        /// Draws the menu component.
        /// </summary>
        /// <param name="view">Top left corner of the menu view in menu system coordinates.</param>
        /// <param name="spriteBatch">The sprite batch to use. <c>Begin</c> is assumed
        /// to have been called and <c>End</c> is assumed to be called after this
        /// method returns.</param>
        public abstract void Draw(Vector2 view, SpriteBatch spriteBatch);
    }
}
