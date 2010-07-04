﻿using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using AW2.Core;
using AW2.Game;
using AW2.Helpers;

namespace AW2.Graphics
{
    /// <summary>
    /// A viewport that follows a player.
    /// </summary>
    public class PlayerViewport : AWViewport
    {
        /// <summary>
        /// The player to follow.
        /// </summary>
        private Player player;

        private GobTrackerOverlay _gobTrackerOverlay;

        /// <summary>
        /// Last used sign of player's shake angle. Either 1 or -1.
        /// </summary>
        private float shakeSign;

        public GobTrackerOverlay GobTracker { get { return _gobTrackerOverlay; } set { _gobTrackerOverlay = value; } }

        /// <param name="player">Which player the viewport will follow.</param>
        /// <param name="onScreen">Where on screen is the viewport located.</param>
        /// <param name="getPostprocessEffectNames">Provider of names of postprocess effects.</param>
        public PlayerViewport(Player player, Rectangle onScreen, Func<IEnumerable<CanonicalString>> getPostprocessEffectNames)
            : base(onScreen, getPostprocessEffectNames)
        {
            this.player = player;
            shakeSign = -1;
            AddOverlayComponent(new MiniStatusOverlay(player));
            AddOverlayComponent(new ChatBoxOverlay(player));
            AddOverlayComponent(new RadarOverlay(player));
            AddOverlayComponent(new BonusListOverlay(player));
            AddOverlayComponent(new PlayerStatusOverlay(player));
            AddOverlayComponent(new ScoreOverlay(player));
            GobTracker = new GobTrackerOverlay(player);
            AddOverlayComponent(GobTracker);
        }

        public Player Player { get { return player; } }

        protected override Matrix ViewMatrix
        {
            get
            {
                // Shake only if gameplay is on. Otherwise freeze because
                // shake won't be attenuated either.
                if (AssaultWing.Instance.GameState == GameState.Gameplay)
                    shakeSign = -shakeSign;

                float viewShake = shakeSign * player.Shake;
                return Matrix.CreateLookAt(new Vector3(GetLookAtPos(), 1000), new Vector3(GetLookAtPos(), 0),
                    new Vector3((float)Math.Cos(MathHelper.PiOver2 + viewShake),
                                (float)Math.Sin(MathHelper.PiOver2 + viewShake),
                                0));
            }
        }

        protected override Vector2 GetLookAtPos()
        {
            return player.LookAtPos;
        }
    }
}
