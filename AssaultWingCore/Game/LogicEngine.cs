using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework.Input;
using AW2.Core;
using AW2.Game.Arenas;
using AW2.Game.GobUtils;
using AW2.Helpers;
using AW2.Helpers.Serialization;
using AW2.UI;

namespace AW2.Game
{
    /// <summary>
    /// Basic implementation of game logic.
    /// </summary>
    public class LogicEngine : AWGameComponent
    {
#if DEBUG
        private Control showOnlyPlayer1Control, showOnlyPlayer2Control, showEverybodyControl;
        private int _unfoundGobsToDeleteOnClientReportLimit = 10;
#endif

        public List<int> GobsToKillOnClient { get; private set; }
 
        public LogicEngine(AssaultWingCore game, int updateOrder)
            : base(game, updateOrder)
        {
            GobsToKillOnClient = new List<int>();
#if DEBUG
            showOnlyPlayer1Control = new KeyboardKey(Keys.F11);
            showOnlyPlayer2Control = new KeyboardKey(Keys.F12);
            showEverybodyControl = new KeyboardKey(Keys.F9);
#endif
        }

        public override void Initialize()
        {
            Log.Write("Loading user-defined types");

            var gobLoader = new TypeLoader(typeof(Gob), Helpers.Paths.GOBS);
            var deviceLoader = new TypeLoader(typeof(ShipDevice), Helpers.Paths.DEVICES);
            var particleLoader = new TypeLoader(typeof(Gob), Helpers.Paths.PARTICLES);
            var arenaLoader = new ArenaTypeLoader(typeof(Arena), Helpers.Paths.ARENAS);

            DeleteTemplates(gobLoader, deviceLoader, particleLoader, arenaLoader);

            foreach (Gob gob in gobLoader.LoadTemplates())
                Game.DataEngine.AddTypeTemplate(gob.TypeName, gob);
            foreach (ShipDevice device in deviceLoader.LoadTemplates())
                Game.DataEngine.AddTypeTemplate(device.TypeName, device);
            foreach (Gob particleEngine in particleLoader.LoadTemplates())
                Game.DataEngine.AddTypeTemplate(particleEngine.TypeName, particleEngine);
            Game.DataEngine.ArenaInfos = arenaLoader.LoadTemplates().Cast<Arena>().Select(arena => arena.Info).ToList();

            SaveTemplates(gobLoader, deviceLoader, particleLoader, arenaLoader);
            base.Initialize();
        }

        public void Reset()
        {
            GobsToKillOnClient.Clear();
        }

        public override void Update()
        {
            var data = Game.DataEngine;
            UpdateControls();

            foreach (var gob in data.Arena.Gobs) gob.Update();
            foreach (var device in data.Devices) device.Update();
            foreach (var player in data.Spectators) player.Update();

            Game.DataEngine.Arena.PerformNonphysicalCollisions();
            KillGobsOnClient();

            // Check for arena end. Network games end when the game server presses Esc.
            if (Game.NetworkMode == NetworkMode.Standalone)
            {
                if (data.GameplayMode.ArenaFinished(data.Arena, data.Players))
                    Game.FinishArena();
            }
        }

        private void KillGobsOnClient()
        {
            var unfoundGobs = new List<int>();
            foreach (var gobID in GobsToKillOnClient)
            {
                var gob = Game.DataEngine.Arena.Gobs.FirstOrDefault(gobb => gobb.ID == gobID);
                if (gob != null)
                    gob.DieOnClient();
                else
                    unfoundGobs.Add(gobID);
            }
            GobsToKillOnClient = unfoundGobs;
#if DEBUG
            if (GobsToKillOnClient.Count() >= _unfoundGobsToDeleteOnClientReportLimit )
            {
                Log.Write("WARNING: {0} unfound gobs to kill on client", GobsToKillOnClient.Count());
                _unfoundGobsToDeleteOnClientReportLimit *= 2;
            }
#endif
        }

        /// <summary>
        /// Checks general game controls and reacts to them.
        /// </summary>
        private void UpdateControls()
        {
#if DEBUG
            if (showEverybodyControl.Pulse)
                Game.ShowOnlyPlayer(-1);
            if (showOnlyPlayer1Control.Pulse)
                Game.ShowOnlyPlayer(0);
            if (showOnlyPlayer2Control.Pulse && Game.DataEngine.Spectators.Count > 1)
                Game.ShowOnlyPlayer(1);
#endif
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private void DeleteTemplates(params TypeLoader[] typeLoaders)
        {
            if (!Game.CommandLineOptions.DeleteTemplates) return;
            Log.Write("Deleting templates now...");
            foreach (var typeLoader in typeLoaders) typeLoader.DeleteTemplates();
            Log.Write("...templates deleted");
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private void SaveTemplates(params TypeLoader[] typeLoaders)
        {
            if (!Game.CommandLineOptions.SaveTemplates) return;
            Log.Write("Saving templates now...");
            foreach (var typeLoader in typeLoaders) typeLoader.SaveTemplateExamples();
            Log.Write("...templates saved");
        }
    }
}