using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using AW2.Core;
using AW2.Game.GobUtils;
using AW2.Helpers;
using AW2.Net;
using AW2.Net.Messages;
using AW2.UI;

namespace AW2.Game
{
    /// <summary>
    /// Basic implementation of game logic.
    /// </summary>
    public class LogicEngine : AWGameComponent
    {
        Control fullscreenControl, escapeControl;
#if DEBUG
        Control showOnlyPlayer1Control, showOnlyPlayer2Control, showEverybodyControl;
#endif

        public LogicEngine()
        {
            escapeControl = new KeyboardKey(Keys.Escape);
            fullscreenControl = new KeyboardKey(Keys.F10);
#if DEBUG
            showOnlyPlayer1Control = new KeyboardKey(Keys.F11);
            showOnlyPlayer2Control = new KeyboardKey(Keys.F12);
            showEverybodyControl = new KeyboardKey(Keys.F9);
#endif
        }

        public override void Initialize()
        {
            Helpers.Log.Write("Loading user-defined types");

            var gobLoader = new TypeLoader(typeof(Gob), Helpers.Paths.GOBS);
            var deviceLoader = new TypeLoader(typeof(ShipDevice), Helpers.Paths.DEVICES);
            var particleLoader = new TypeLoader(typeof(Gob), Helpers.Paths.PARTICLES);
            var arenaLoader = new ArenaTypeLoader(typeof(Arena), Helpers.Paths.ARENAS);

            DeleteTemplates(gobLoader, deviceLoader, particleLoader, arenaLoader);

            foreach (Gob gob in gobLoader.LoadTemplates())
                AssaultWing.Instance.DataEngine.AddTypeTemplate(gob.TypeName, gob);
            foreach (ShipDevice device in deviceLoader.LoadTemplates())
                AssaultWing.Instance.DataEngine.AddTypeTemplate(device.TypeName, device);
            foreach (Gob particleEngine in particleLoader.LoadTemplates())
                AssaultWing.Instance.DataEngine.AddTypeTemplate(particleEngine.TypeName, particleEngine);
            AssaultWing.Instance.DataEngine.ArenaInfos = arenaLoader.LoadTemplates().Cast<Arena>().Select(arena => arena.Info).ToList();

            SaveTemplates(gobLoader, deviceLoader, particleLoader, arenaLoader);
            base.Initialize();
        }

        public override void Update()
        {
            var data = AssaultWing.Instance.DataEngine;
            UpdateControls();

            // Update gobs, weapons and players.
            foreach (var gob in data.Arena.Gobs) gob.Update();
            foreach (var device in data.Devices) device.Update();
            foreach (var player in data.Spectators) player.Update();

            AssaultWing.Instance.DataEngine.Arena.PerformNonphysicalCollisions();

            // Check for arena end. Network games end when the game server presses Esc.
            if (AssaultWing.Instance.NetworkMode == NetworkMode.Standalone)
            {
                if (data.GameplayMode.ArenaFinished(data.Arena, data.Players))
                    AssaultWing.Instance.FinishArena();
            }
        }

        /// <summary>
        /// Checks general game controls and reacts to them.
        /// </summary>
        private void UpdateControls()
        {
            if (fullscreenControl.Pulse)
                AssaultWing.Instance.ToggleFullscreen();
#if DEBUG
            if (showEverybodyControl.Pulse)
                AssaultWing.Instance.ShowOnlyPlayer(-1);
            if (showOnlyPlayer1Control.Pulse)
                AssaultWing.Instance.ShowOnlyPlayer(0);
            if (showOnlyPlayer2Control.Pulse && AssaultWing.Instance.DataEngine.Spectators.Count > 1)
                AssaultWing.Instance.ShowOnlyPlayer(1);
#endif
            if (escapeControl.Pulse)
            {
                var dialogData = AssaultWing.Instance.NetworkMode == NetworkMode.Server
                    ? new AW2.Graphics.OverlayComponents.CustomOverlayDialogData(
                        "Finish Arena? (Yes/No)",
                        new TriggeredCallback(TriggeredCallback.GetYesControl(), AssaultWing.Instance.FinishArena),
                        new TriggeredCallback(TriggeredCallback.GetNoControl(), AssaultWing.Instance.ResumePlay))
                    : new AW2.Graphics.OverlayComponents.CustomOverlayDialogData(
                        "Quit to Main Menu? (Yes/No)",
                        new TriggeredCallback(TriggeredCallback.GetYesControl(), AssaultWing.Instance.ShowMenu),
                        new TriggeredCallback(TriggeredCallback.GetNoControl(), AssaultWing.Instance.ResumePlay));

                AssaultWing.Instance.ShowDialog(dialogData);
            }
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private void DeleteTemplates(params TypeLoader[] typeLoaders)
        {
            if (!AssaultWing.Instance.CommandLineArgs.Contains("-deletetemplates")) return;
            Log.Write("Parameter -deletetemplates given, deleting templates now...");
            foreach (var typeLoader in typeLoaders) typeLoader.DeleteTemplates();
            Log.Write("...templates deleted");
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private void SaveTemplates(params TypeLoader[] typeLoaders)
        {
            if (!AssaultWing.Instance.CommandLineArgs.Contains("-savetemplates")) return;
            Log.Write("Parameter -savetemplates given, saving templates now...");
            foreach (var typeLoader in typeLoaders) typeLoader.SaveTemplateExamples();
            Log.Write("...templates saved");
        }
    }
}
