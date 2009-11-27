﻿using System;
using System.Windows.Forms;
using Microsoft.Xna.Framework;

namespace AW2
{
    /// <summary>
    /// A wrapper for <see cref="Microsoft.Xna.Framework.GameWindow"/>.
    /// </summary>
    public class AWGameWindow : AW2.Graphics.IWindow
    {
        GameWindow window;
        Form windowForm;
        GraphicsDeviceManager graphics;
        Rectangle windowedSize;

        /// <summary>
        /// Creates a new Assault Wing game window wrapper.
        /// </summary>
        public AWGameWindow(GameWindow window, GraphicsDeviceManager graphics)
        {
            this.window = window;
            windowForm = (Form)Form.FromHandle(window.Handle);
            this.graphics = graphics;
        }

        #region IWindow Members

        /// <summary>
        /// The title of the window.
        /// </summary>
        public string Title { get { return window.Title; } set { window.Title = value; } }

        /// <summary>
        /// Can the user resize the window.
        /// </summary>
        public bool AllowUserResizing
        {
            get { return window.AllowUserResizing; }
            set
            {
                // This piece of code may get called from another thread. Therefore
                // we cannot set window.AllowUserResizing = false; without Invoke().
                windowForm.Invoke(new Action(() => { window.AllowUserResizing = value; }));
            }
        }

        /// <summary>
        /// Dimensions of the area the game can draw on.
        /// </summary>
        public Rectangle ClientBounds { get { return window.ClientBounds; } }

        /// <summary>
        /// Minimum dimensions of the area the game can draw on.
        /// </summary>
        public Rectangle ClientBoundsMin
        {
            get
            {
                var min = windowForm.MinimumSize;
                return new Rectangle(0, 0, min.Width, min.Height);
            }
            set
            {
                windowForm.MinimumSize = new System.Drawing.Size(value.Width, value.Height);
            }
        }

        /// <summary>
        /// Low-level handle to the area the game can draw on.
        /// </summary>
        public IntPtr Handle { get { return window.Handle; } }

        /// <summary>
        /// Called when the dimensions of the drawable area has changed.
        /// </summary>
        public event EventHandler ClientSizeChanged
        {
            add { window.ClientSizeChanged += value; }
            remove { window.ClientSizeChanged -= value; }
        }

        /// <summary>
        /// Toggles between fullscreen and windowed mode.
        /// </summary>
        public void ToggleFullscreen()
        {
            var displayMode = graphics.GraphicsDevice.DisplayMode;
            lock (graphics)
            {
                // Set our window size and format preferences before switching.
                if (graphics.IsFullScreen)
                {
                    graphics.PreferredBackBufferWidth = windowedSize.Width;
                    graphics.PreferredBackBufferHeight = windowedSize.Height;
                }
                else
                {
                    windowedSize.Width = ClientBounds.Width;
                    windowedSize.Height = ClientBounds.Height;
                    graphics.PreferredBackBufferWidth = displayMode.Width;
                    graphics.PreferredBackBufferHeight = displayMode.Height;
                }
                graphics.ToggleFullScreen();
            }
        }

        #endregion
    }
}
