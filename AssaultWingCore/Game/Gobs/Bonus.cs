using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using AW2.Core;
using AW2.Game.Collisions;
using AW2.Game.GobUtils;
using AW2.Helpers;
using AW2.Helpers.Serialization;

namespace AW2.Game.Gobs
{
    /// <summary>
    /// A bonus that can be collected by a player.
    /// </summary>
    public class Bonus : Gob
    {
        /// <summary>
        /// Types of gobs to create on being collected.
        /// </summary>
        [TypeParameter, ShallowCopy]
        private CanonicalString[] _collectGobTypes;

        /// <summary>
        /// Lifetime of the bonus, in seconds.
        /// </summary>
        [TypeParameter]
        private float _lifetime;

        /// <summary>
        /// Time at which the bonus dies, in game time.
        /// </summary>
        private TimeSpan _deathTime;

        /// <summary>
        /// What happens when the bonus is collected.
        /// </summary>
        [TypeParameter]
        private CanonicalString _bonusActionTypeName;

        public override bool IsDamageable { get { return true; } }

        /// <summary>
        /// This constructor is only for serialisation.
        /// </summary>
        public Bonus()
        {
            _collectGobTypes = new[] { (CanonicalString)"dummypeng" };
            _lifetime = 10;
            _bonusActionTypeName = (CanonicalString)"dummygob";
        }

        public Bonus(CanonicalString typeName)
            : base(typeName)
        {
        }

        public override void Activate()
        {
            base.Activate();
            _deathTime = Arena.TotalTime + TimeSpan.FromSeconds(_lifetime);
        }

        public override void Update()
        {
            base.Update();
            if (_deathTime <= Arena.TotalTime)
                Die();
        }

        public override bool CollideIrreversible(CollisionArea myArea, CollisionArea theirArea)
        {
            var theirShip = theirArea.Owner as Ship;
            if (myArea.Type == CollisionAreaType.BonusCollect && theirShip != null)
            {
                DoBonusAction(theirShip);
                Game.SoundEngine.PlaySound("BonusCollection", this);
                DeathGobTypes = _collectGobTypes;
                Die();
                return true;
            }
            return false;
        }

        private void DoBonusAction(Gob host)
        {
            if (Game.NetworkMode == NetworkMode.Client) return;
            if (host.Owner != null) host.Owner.ArenaStatistics.BonusesCollected++;
            var gameAction = BonusAction.Create<BonusAction>(_bonusActionTypeName, host, gob => { });
            if (gameAction == null) return;
            Gob.CreateGob<ArenaMessage>(Game, (CanonicalString)"bonusmessage", gob =>
            {
                gob.ResetPos(Pos, Vector2.Zero, Gob.DEFAULT_ROTATION);
                gob.Owner = host.Owner;
                gob.Message = gameAction.BonusText;
                gob.IconName = gameAction.BonusIconName;
                if (host.Owner != null) gob.DrawColor = host.Owner.Color;
                Game.DataEngine.Arena.Gobs.Add(gob);
            });
            var playerOwner = host.Owner as Player;
            if (playerOwner != null) playerOwner.Messages.Add(new PlayerMessage("You collected " + gameAction.BonusText, playerOwner.Color));
            if (host.Owner != null) Game.Stats.Send(new
            {
                Bonus = _bonusActionTypeName.Value,
                Player = Game.Stats.GetStatsString(host.Owner),
                Pos = Pos,
                BirthPos = BirthPos,
            });
        }
    }
}
