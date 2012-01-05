using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using AW2.Core;
using AW2.Game.Arenas;
using AW2.Game.Gobs;
using AW2.Game.GobUtils;
using AW2.Graphics;
using AW2.Graphics.OverlayComponents;
using AW2.Helpers;
using AW2.Helpers.Collections;

namespace AW2.Game
{
    /// <summary>
    /// Basic implementation of game data.
    /// </summary>
    /// Gobs in an arena are kept on several arena layers. One of the layers
    /// is where the actual gameplay takes place. The rest are just for the looks.
    /// The gameplay layer is the default for all gob-related actions.
    /// To deal with some other layer, you need to know its layer index.
    /// There is also another special layer; the gameplay backlayer. It is
    /// at the same depth as the gameplay layer but is drawn behind it.
    /// The gameplay backlayer is for 2D graphics that needs to be behind gobs.
    public class DataEngine : AWGameComponent
    {
        /// <summary>
        /// Type templates, indexed by their type name.
        /// </summary>
        private NamedItemCollection<object> _templates;

        private IndexedItemCollection<Spectator> _spectators;

        /// <summary>
        /// Players and other spectators of the game session.
        /// </summary>
        public IndexedItemCollection<Spectator> Spectators
        {
            get { return _spectators; }
            private set
            {
                _spectators = value;
                _spectators.Added += SpectatorAddedHandler;
                _spectators.Removed += SpectatorRemovedHandler;
            }
        }

        public IEnumerable<Gob> Minions { get { return Spectators.SelectMany(spec => spec.Minions); } }
        public IEnumerable<Player> Players { get { return Spectators.OfType<Player>(); } }
        public Player ChatPlayer { get { return Players.First(plr => plr.IsLocal); } }

        // TODO: Maybe Arena is a more natural place for Devices, alongside Gobs?
        public IndexedItemCollection<ShipDevice> Devices { get; private set; }
        public Arena Arena { get; set; }
        public TimeSpan ArenaTotalTime { get { return Arena == null ? TimeSpan.Zero : Arena.TotalTime; } }
        public int ArenaFrameCount { get { return Arena == null ? 0 : Arena.FrameNumber; } }
        public WrappedTextList ChatHistory { get; private set; }
        public ArenaSilhouette ArenaSilhouette { get; private set; }

        /// <summary>
        /// In real time. If zero, then the arena does not time out.
        /// </summary>
        public TimeSpan ArenaFinishTime { get; set; }

        public event Action<Spectator> SpectatorAdded;
        public event Action<Spectator> SpectatorRemoved;

        public DataEngine(AssaultWingCore game, int updateOrder)
            : base(game, updateOrder)
        {
            Spectators = new IndexedItemCollection<Spectator>();
            Devices = new IndexedItemCollection<ShipDevice>();
            Devices.Added += device =>
            {
                device.Arena = Arena;
                device.Activate();
            };
            Devices.Removed += device => device.Dispose();
            ChatHistory = new WrappedTextList(Game);
            Viewports = new AWViewportCollection(Game.GraphicsDeviceService, 0, null);
            _templates = new NamedItemCollection<object>();
            ArenaSilhouette = new ArenaSilhouette(Game);
        }

        #region arenas

        public void StartArena()
        {
            ArenaSilhouette.Clear();
            foreach (var player in Spectators) player.ResetForArena();
        }

        #endregion arenas

        #region type templates

        /// <summary>
        /// Saves an object to be used as a template for a user-defined named type.
        /// </summary>
        public void AddTypeTemplate(CanonicalString typeName, object template)
        {
            try
            {
                _templates.Add(typeName, template);
            }
            catch (ArgumentException)
            {
                Log.Write("WARNING: Overwriting template for user-defined type " + typeName);
            }
        }

        public object GetTypeTemplate(CanonicalString typeName)
        {
            object result;
            _templates.TryGetValue(typeName, out result);
            return result;
        }

        public IEnumerable<T> GetTypeTemplates<T>()
        {
            return _templates.Values.OfType<T>();
        }

        #endregion type templates

        #region viewports

        private AWViewportCollection _viewports;
        public AWViewportCollection Viewports
        {
            get { return _viewports; }
            private set
            {
                if (_viewports != null) _viewports.Dispose();
                _viewports = value;
            }
        }

        public void RearrangeViewports()
        {
            var playerCount = Game.DataEngine.Spectators.Where(player => player.NeedsViewport).Count();
            var viewportPermutation =
                playerCount <= 1 ? x => x
                : playerCount == 2 ? x => x == 0 ? 1 : x == 1 ? 0 : x
                : (Func<int, int>)(x => x == 0 ? 1 : x == 1 ? 2 : x == 2 ? 0 : x);
            RearrangeViewports(viewportPermutation);
        }

        private void RearrangeViewports(Func<int, int> viewportToPlayerPermutation)
        {
            if (Arena == null) return;
            var localPlayers = Game.DataEngine.Spectators.Where(player => player.NeedsViewport).ToList();
            Viewports = new AWViewportCollection(Game.GraphicsDeviceService, localPlayers.Count(),
                (index, rectangle) =>
                {
                    var viewport = localPlayers[viewportToPlayerPermutation(index)].CreateViewport(rectangle);
                    viewport.Reset(Arena.Dimensions / 2);
                    return viewport;
                });
        }

        /// <summary>
        /// Rearranges player viewports so that one player gets all screen space
        /// and the others get nothing.
        /// </summary>
        public void RearrangeViewports(int privilegedPlayer)
        {
            var player = Game.DataEngine.Players.ElementAt(privilegedPlayer);
            Viewports = new AWViewportCollection(Game.GraphicsDeviceService, 1, (index, viewport) => player.CreateViewport(viewport));
        }

        #endregion viewports

        #region miscellaneous

        public GameplayMode GameplayMode { get; set; }

        /// <summary>
        /// Clears all data about the state of the game session that is not
        /// needed when the game session is over.
        /// Data that is generated during a game session and is still relevant 
        /// after the game session is left untouched.
        /// </summary>
        /// <remarks>
        /// Call this method after the game session has ended.
        /// </remarks>
        public void ClearGameState()
        {
            if (Arena != null) Arena.Dispose();
            Arena = null;
            Viewports = new AWViewportCollection(Game.GraphicsDeviceService, 0, null);
            Spectators.Remove(spec => spec.IsDisconnected);
            foreach (var player in Spectators) player.ResetForArena();
            Devices.Clear();
        }

        public override void UnloadContent()
        {
            ArenaSilhouette.Dispose();
        }

        #endregion miscellaneous

        public void RemoveAllButLocalSpectators()
        {
            Spectators.Remove(spec => !spec.IsLocal);
        }

        #region Private methods

        private int GetFreeSpectatorID()
        {
            var usedIDs = Spectators.Select(spec => spec.ID);
            for (int id = 0; id <= byte.MaxValue; ++id)
                if (!usedIDs.Contains(id)) return id;
            throw new ApplicationException("There are no free spectator IDs");
        }

        private Color GetFreePlayerColor()
        {
            return GetPlayerColorPalette().Except(Players.Select(p => p.Color)).First();
        }

        private static IEnumerable<Color> GetPlayerColorPalette()
        {
            while (true)
            {
                yield return Color.CornflowerBlue;
                yield return Color.DeepPink;
                yield return Color.Orange;
                yield return Color.Orchid;
                yield return Color.YellowGreen;
                yield return Color.MediumSpringGreen;
                yield return Color.HotPink;
                yield return Color.Aquamarine;
                yield return Color.Olive;
                yield return Color.OrangeRed;
                yield return Color.Thistle;
                yield return Color.Violet;
                yield return Color.Turquoise;
                yield return Color.Fuchsia;
                yield return Color.LightSteelBlue;
                yield return Color.Lime;
                // 16 unique colours total
            }
        }

        #endregion Private methods

        #region Callbacks

        private void SpectatorAddedHandler(Spectator spectator)
        {
            spectator.Game = Game;
            spectator.ID = GetFreeSpectatorID();
            var player = spectator as Player;
            if (player != null && Game.NetworkMode != NetworkMode.Client)
            {
                player.Color = Color.Black; // reset to a color that won't affect free color picking
                player.Color = GetFreePlayerColor();
            }
            if (SpectatorAdded != null) SpectatorAdded(spectator);
        }

        private void SpectatorRemovedHandler(Spectator spectator)
        {
            if (SpectatorRemoved != null) SpectatorRemoved(spectator);
        }

        #endregion Callbacks
    }
}
