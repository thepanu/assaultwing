using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using AW2.Core;
using AW2.Helpers;
using AW2.Menu.Main;
using AW2.UI;

namespace AW2.Menu
{
    /// <summary>
    /// The main menu component where the user can choose to go play, go setup, or go away.
    /// </summary>
    public class MainMenuComponent : MenuComponent
    {
        private const int MENU_ITEM_COUNT = 6; // number of items that fit in the menu at once

        /// <summary>
        /// Access only through <see cref="ItemCollections"/>.
        /// </summary>
        private MainMenuItemCollections _itemCollections;

        private Stack<Tuple<MainMenuItemCollection, int, int>> _currentItemsHistory; // items, currentIndex, topmostIndex
        private MainMenuItemCollection _currentItems;
        private ScrollableList _currentItem;

        private TriggeredCallbackCollection _commonCallbacks;
        private Vector2 _pos; // position of the component's background texture in menu system coordinates

        public override bool Active
        {
            set
            {
                base.Active = value;
                if (value)
                {
                    ResetItems();
                    MenuEngine.Game.Settings.ToFile();
                }
            }
        }

        public override Vector2 Center { get { return _pos + new Vector2(700, 455); } }
        public override string HelpText { get { return "Arrows move, Enter proceeds, Esc cancels"; } }

        public MainMenuItem CurrentItem { get { return _currentItems[_currentItem.CurrentIndex]; } }
        private MainMenuItemCollections ItemCollections
        {
            get
            {
                if (_itemCollections == null)
                {
                    _itemCollections = new MainMenuItemCollections(this);
                    ResetItems();
                }
                return _itemCollections;
            }
        }
        private MenuControls Controls { get { return MenuEngine.Controls; } }

        public MainMenuComponent(MenuEngineImpl menuEngine)
            : base(menuEngine)
        {
            _pos = new Vector2(0, 698);
            _currentItemsHistory = new Stack<Tuple<MainMenuItemCollection, int, int>>();
            _currentItem = new ScrollableList(MENU_ITEM_COUNT, () => _currentItems == null ? 0 : _currentItems.Count);
            InitializeControlCallbacks();
        }

        public void PushItems(MainMenuItemCollection items)
        {
            if (_currentItems != null) _currentItemsHistory.Push(Tuple.Create(_currentItems, _currentItem.CurrentIndex, _currentItem.TopmostIndex));
            _currentItems = items;
            _currentItem.CurrentIndex = 0;
        }

        private void PopItems()
        {
            var oldHistoryCount = _currentItemsHistory.Count;
            if (_currentItemsHistory.Count > 1)
            {
                var old = _currentItemsHistory.Pop();
                _currentItems = old.Item1;
                _currentItem.CurrentIndex = old.Item2;
                _currentItem.TopmostIndex = old.Item3;
                MenuEngine.Game.SoundEngine.PlaySound("MenuChangeItem");
            }
            if (_currentItemsHistory.Count == 1 && oldHistoryCount > 1)
            {
                MenuEngine.Game.Settings.ToFile();
                MenuEngine.Game.CutNetworkConnections();
                ApplyGraphicsSettings();
                ApplyControlsSettings();
            }
        }

        public override void Update()
        {
            if (!Active) return;
            MenuEngine.Game.WebData.LoginErrors.Do(queue =>
            {
                while (queue.Any()) MenuEngine.Game.ShowInfoDialog(queue.Dequeue());
            });
            if (_currentItems != ItemCollections.NetworkItems && MenuEngine.Game.NetworkMode != NetworkMode.Standalone)
                throw new ApplicationException("Unexpected NetworkMode " + MenuEngine.Game.NetworkMode + " in " + _currentItems.Name);
            _commonCallbacks.Update();
            foreach (var menuItem in _currentItems) menuItem.Update();
            _currentItems.Update();
        }

        public override void Draw(Vector2 view, SpriteBatch spriteBatch)
        {
            spriteBatch.Draw(MenuEngine.MenuContent.MainBackground, _pos - view, Color.White);
            _currentItem.ForEachVisible((realIndex, visibleIndex, isSelected) =>
            {
                if (isSelected) _currentItems[realIndex].DrawHighlight(spriteBatch, _pos - view, visibleIndex);
                _currentItems[realIndex].Draw(spriteBatch, _pos - view, visibleIndex);
            });

            if (_currentItems == ItemCollections.NetworkItems)
                DrawScheduledBattleDisplay(view, spriteBatch);

            var scrollUpPos = _pos - view + new Vector2(653, 260);
            var scrollDownPos = _pos - view + new Vector2(653, 580);
            if (_currentItem.IsScrollableUp) spriteBatch.Draw(Content.ScrollUpTexture, scrollUpPos, Color.White);
            if (_currentItem.IsScrollableDown) spriteBatch.Draw(Content.ScrollDownTexture, scrollDownPos, Color.White);
        }

        private void DrawScheduledBattleDisplay(Vector2 view, SpriteBatch spriteBatch)
        {
            var backgroundPos = _pos - view + new Vector2(440, 600);
            var textStartPos = (backgroundPos + new Vector2(50, 43)).Round();
            spriteBatch.Draw(MenuEngine.MenuContent.SmallStatusPaneTexture, backgroundPos, Color.White);
            spriteBatch.DrawString(Content.FontBig, "Next Scheduled Game in:", textStartPos, Color.White);
            spriteBatch.DrawString(Content.FontSmall, "Everybody's Welcome to Join!\n\nYou can find all our Scheduled Games by selecting\n\"Find more in Forums\" in this menu.  (and you are of\ncourse free to play whenever you want)", textStartPos + new Vector2(0, 27), Color.White);
            var currentTime = DateTime.Now;
            var nextGame = MenuEngine.Game.WebData.NextScheduledGame;
            var text =
                !nextGame.HasValue || nextGame + TimeSpan.FromHours(2) <= currentTime ? "Not yet scheduled"
                : nextGame <= currentTime ? "Now! Join in!"
                : (nextGame.Value - currentTime).ToDurationString("d", "h", "min", null, usePlurals: false);
            spriteBatch.DrawString(Content.FontBig, text, textStartPos + new Vector2(260, 6), Color.YellowGreen);
        }

        private void ResetItems()
        {
            _currentItemsHistory.Clear();
            PushItems(ItemCollections.StartItems);
        }

        private void InitializeControlCallbacks()
        {
            _commonCallbacks = new TriggeredCallbackCollection
            {
                TriggeredCallback = MenuEngine.ResetCursorFade
            };
            _commonCallbacks.Callbacks.Add(new TriggeredCallback(Controls.Dirs.Up, () =>
            {
                _currentItem.CurrentIndex--;
                MenuEngine.Game.SoundEngine.PlaySound("MenuBrowseItem");
            }));
            _commonCallbacks.Callbacks.Add(new TriggeredCallback(Controls.Dirs.Down, () =>
            {
                _currentItem.CurrentIndex++;
                MenuEngine.Game.SoundEngine.PlaySound("MenuBrowseItem");
            }));
            _commonCallbacks.Callbacks.Add(new TriggeredCallback(Controls.Activate, () => CurrentItem.Action()));
            _commonCallbacks.Callbacks.Add(new TriggeredCallback(Controls.Dirs.Left, () => CurrentItem.ActionLeft()));
            _commonCallbacks.Callbacks.Add(new TriggeredCallback(Controls.Dirs.Right, () => CurrentItem.ActionRight()));
            _commonCallbacks.Callbacks.Add(new TriggeredCallback(Controls.Back, PopItems));
        }

        private void ApplyGraphicsSettings()
        {
            var window = MenuEngine.Game.Window;
            var gfxSetup = MenuEngine.Game.Settings.Graphics;
            var clientBounds = window.Impl.GetClientBounds();
            if (window.Impl.GetFullScreen() &&
                !(clientBounds.Width == gfxSetup.FullscreenWidth && clientBounds.Height == gfxSetup.FullscreenHeight))
            {
                window.Impl.SetFullScreen(gfxSetup.FullscreenWidth, gfxSetup.FullscreenHeight);
            }
            if (gfxSetup.IsVerticalSynced && !window.Impl.IsVerticalSynced()) window.Impl.EnableVerticalSync();
            if (!gfxSetup.IsVerticalSynced && window.Impl.IsVerticalSynced()) window.Impl.DisableVerticalSync();
        }

        private void ApplyControlsSettings()
        {
            var players = MenuEngine.Game.DataEngine.Players;
            var controls = new[] { MenuEngine.Game.Settings.Controls.Player1, MenuEngine.Game.Settings.Controls.Player2 };
            players.Zip(controls, (plr, ctrls) => plr.Controls = PlayerControls.FromSettings(ctrls)).ToArray();
            MenuEngine.Game.ChatStartControl = MenuEngine.Game.Settings.Controls.Chat.GetControl();
        }
    }
}
