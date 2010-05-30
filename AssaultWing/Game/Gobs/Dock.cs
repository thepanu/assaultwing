using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using AW2.Helpers;
using AW2.Sound;

namespace AW2.Game.Gobs
{
    /// <summary>
    /// A docking platform.
    /// </summary>
    public class Dock : Gob
    {
        #region Dock fields

        private static readonly TimeSpan DOCK_SOUND_STOP_DELAY = TimeSpan.FromSeconds(0.5);

        /// <summary>
        /// Speed of repairing damageable gobs, measured in damage/second.
        /// Use a negative value for repairing, positive for damaging.
        /// </summary>
        [TypeParameter]
        private float _repairSpeed;

        /// <summary>
        /// Speed of charging primary weapons of ships, measured in charge/second.
        /// </summary>
        [TypeParameter]
        private float _weapon1ChargeSpeed;

        /// <summary>
        /// Speed of charging secondary weapons of ships, measured in charge/second.
        /// </summary>
        [TypeParameter]
        private float _weapon2ChargeSpeed;

        private TimeSpan _lastDockSoundTime;
        private AWSound _dockSound;
        [Obsolete] private Cue _dockSoundCue;

        #endregion Dock fields

        private bool MustBeSilent
        {
            get { return _lastDockSoundTime < Arena.TotalTime - DOCK_SOUND_STOP_DELAY; }
        }

        /// This constructor is only for serialisation.
        public Dock()
        {
            _repairSpeed = -10;
            _weapon1ChargeSpeed = 100;
            _weapon2ChargeSpeed = 100;
        }

        public Dock(CanonicalString typeName)
            : base(typeName)
        {
            movable = false;
        }

        public override void Activate()
        {
            base.Activate();
            _dockSound = new AWSound("Docking");
        }

        public override void Update()
        {
            base.Update();
            if (MustBeSilent) _dockSound.EnsureIsStopped(AudioStopOptions.AsAuthored);
        }

        public override void Collide(CollisionArea myArea, CollisionArea theirArea, bool stuck)
        {
            // We assume we have only one Receptor collision area which handles docking.
            // Then 'theirArea.Owner' must be damageable.
            if (myArea.Name == "Dock")
            {
                if (theirArea.Owner.Owner != null) EnsureDockSoundPlaying();
                theirArea.Owner.InflictDamage(AssaultWing.Instance.PhysicsEngine.ApplyChange(_repairSpeed, AssaultWing.Instance.GameTime.ElapsedGameTime), new DeathCause());
                Ship ship = theirArea.Owner as Ship;
                if (ship != null)
                {
                    ship.ExtraDevice.Charge += AssaultWing.Instance.PhysicsEngine.ApplyChange(_weapon1ChargeSpeed, AssaultWing.Instance.GameTime.ElapsedGameTime);
                    ship.Weapon2.Charge += AssaultWing.Instance.PhysicsEngine.ApplyChange(_weapon2ChargeSpeed, AssaultWing.Instance.GameTime.ElapsedGameTime);
                }
            }
        }

        public override void Dispose()
        {
            _dockSound.Dispose();
            base.Dispose();
        }

        private void EnsureDockSoundPlaying()
        {
            _lastDockSoundTime = Arena.TotalTime;
            _dockSound.EnsureIsPlaying();
        }
    }
}
