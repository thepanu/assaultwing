using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace AW2.Graphics
{
    /// <summary>
    /// Type of horizontal alignment.
    /// </summary>
    public enum HorizontalAlignment
    {
        /// <summary>
        /// Aligned to the left.
        /// </summary>
        Left,

        /// <summary>
        /// Horizontally centered.
        /// </summary>
        Center,

        /// <summary>
        /// Aligned to the right.
        /// </summary>
        Right,

        /// <summary>
        /// Covers the whole viewport.
        /// </summary>
        Stretch,
    }

    /// <summary>
    /// Type of vertical alignment.
    /// </summary>
    public enum VerticalAlignment
    {
        /// <summary>
        /// Aligned to the top.
        /// </summary>
        Top,

        /// <summary>
        /// Vertically centered.
        /// </summary>
        Center,

        /// <summary>
        /// Aligned to the bottom.
        /// </summary>
        Bottom,

        /// <summary>
        /// Covers the whole viewport.
        /// </summary>
        Stretch,
    }

    /// <summary>
    /// An overlay graphics component, for example in player's screen during play.
    /// </summary>
    /// An overlay component is drawn onto the active viewport of the backbuffer.
    /// The component is aligned by setting a point of reference in the viewport.
    /// This point can be top, center, bottom on the vertical axis, and left, center,
    /// right on the horizontal axis.
    public abstract class OverlayComponent
    {
        /// <summary>
        /// Horizontal alignment of the component in the backbuffer viewport.
        /// </summary>
        public HorizontalAlignment HorizontalAlignment { get; set; }

        /// <summary>
        /// Vertical alignment of the component in the backbuffer viewport.
        /// </summary>
        public VerticalAlignment VerticalAlignment { get; set; }

        /// <summary>
        /// Alignment adjustment; added to the coordinates obtained by the chosen alignment.
        /// Initially set to Vector2.Zero, which gives no adjustment to the chosen alignment.
        /// </summary>
        public Vector2 CustomAlignment { get; set; }

        /// <summary>
        /// Is the component visible.
        /// </summary>
        public bool Visible { get; set; }

        /// <summary>
        /// The dimensions of the component in pixels.
        /// </summary>
        public abstract Point Dimensions { get; }

        public OverlayComponent(HorizontalAlignment horizontal, VerticalAlignment vertical)
        {
            HorizontalAlignment = horizontal;
            VerticalAlignment = vertical;
            Visible = true;
            LoadContent();
        }

        /// <summary>
        /// Draws the overlay graphics component, correctly aligned in the
        /// graphics device's current viewport.
        /// </summary>
        /// <param name="spriteBatch">The sprite batch to use. This method
        /// will call <c>Begin</c> and <c>End</c>.</param>
        public void Draw(SpriteBatch spriteBatch)
        {
            if (!Visible) return;
            var gfx = AssaultWing.Instance.GraphicsDevice;
            var oldViewport = gfx.Viewport;
            var newViewport = oldViewport;
            var dimensions = Dimensions;
            switch (HorizontalAlignment)
            {
                case HorizontalAlignment.Left:
                    break;
                case HorizontalAlignment.Center:
                    newViewport.X += Math.Max(0, (oldViewport.Width - dimensions.X) / 2);
                    break;
                case HorizontalAlignment.Right:
                    newViewport.X += Math.Max(0, oldViewport.Width - dimensions.X);
                    break;
                case HorizontalAlignment.Stretch:
                    dimensions.X = oldViewport.Width;
                    break;
            }
            switch (VerticalAlignment)
            {
                case VerticalAlignment.Top:
                    break;
                case VerticalAlignment.Center:
                    newViewport.Y += Math.Max(0, (oldViewport.Height - dimensions.Y) / 2);
                    break;
                case VerticalAlignment.Bottom:
                    newViewport.Y += Math.Max(0, oldViewport.Height - dimensions.Y);
                    break;
                case VerticalAlignment.Stretch:
                    dimensions.Y = oldViewport.Height;
                    break;
            }
            newViewport.X += (int)CustomAlignment.X;
            newViewport.Y += (int)CustomAlignment.Y;
            newViewport.Width = Math.Min(oldViewport.Width, dimensions.X);
            newViewport.Height = Math.Min(oldViewport.Height, dimensions.Y);
            gfx.Viewport = newViewport;
            spriteBatch.Begin();
            DrawContent(spriteBatch);
            spriteBatch.End();
            gfx.Viewport = oldViewport;
        }

        /// <summary>
        /// Draws the overlay graphics component using the guarantee that the
        /// graphics device's viewport is set to the exact area needed by the component.
        /// </summary>
        /// <param name="spriteBatch">The sprite batch to use. <c>Begin</c> is assumed
        /// to have been called and <c>End</c> is assumed to be called after this
        /// method returns.</param>
        protected abstract void DrawContent(SpriteBatch spriteBatch);

        /// <summary>
        /// Called when graphics resources need to be loaded.
        /// </summary>
        public virtual void LoadContent() { }

        /// <summary>
        /// Called when graphics resources need to be unloaded.
        /// </summary>
        public virtual void UnloadContent() { }
    }
}
