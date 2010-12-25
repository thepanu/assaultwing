using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using AW2.Core;
using AW2.Game.Gobs;
using AW2.Game.GobUtils;
using AW2.Graphics.OverlayComponents;
using AW2.Helpers;
using AW2.Helpers.Serialization;
using AW2.UI;

namespace AW2.Game
{
    /// <summary>
    /// Player of the game. 
    /// </summary>
    [System.Diagnostics.DebuggerDisplay("ID:{ID} Name:{Name} ShipName:{ShipName}")]
    public class Player : Spectator
    {
        public class PlayerMessageEntry
        {
            public PlayerMessage Message { get; private set; }
            public TimeSpan EntryTime { get; private set; }
            public PlayerMessageEntry(PlayerMessage message, TimeSpan entryTime)
            {
                Message = message;
                EntryTime = entryTime;
            }
        }

        public static readonly Color PRETEXT_COLOR = new Color(1f, 1f, 1f);
        public static readonly Color DEFAULT_COLOR = new Color(1f, 1f, 1f);
        public static readonly Color BONUS_COLOR = new Color(0.3f, 0.7f, 1f);
        public static readonly Color DEATH_COLOR = new Color(1f, 0.2f, 0.2f);
        public static readonly Color KILL_COLOR = new Color(0.2f, 1f, 0.2f);
        public static readonly Color SPECIAL_KILL_COLOR = new Color(255, 228, 0);
        public static readonly Color PLAYER_STATUS_COLOR = new Color(1f, 0.52f, 0.13f);
        private const int MESSAGE_KEEP_COUNT = 100;

        /// <summary>
        /// Time between death of player's ship and birth of a new ship,
        /// measured in seconds.
        /// </summary>
        private const float MOURNING_DELAY = 3;

        #region Player fields about general things

        /// <summary>
        /// How many reincarnations the player has left.
        /// </summary>
        protected int _lives;

        /// <summary>
        /// Time at which the player's ship is born, measured in game time.
        /// </summary>
        private TimeSpan _shipSpawnTime;

        /// <summary>
        /// Amount of accumulated damage that determines the amount of shake 
        /// the player is suffering right now. Measured relative to
        /// the maximum damage of the player's ship.
        /// </summary>
        /// Shaking affects the player's viewport and is caused by
        /// the player's ship receiving damage.
        private float _relativeShakeDamage;

        /// <summary>
        /// Function that maps relative shake damage to radians that the player's
        /// viewport will tilt to produce sufficient shake.
        /// </summary>
        private Curve _shakeCurve;

        /// <summary>
        /// Function that maps a parameter to relative shake damage.
        /// </summary>
        /// Used in attenuating shake.
        private Curve _shakeAttenuationCurve;

        /// <summary>
        /// Inverse of <c>shakeAttenuationCurve</c>.
        /// </summary>
        /// Used in attenuating shake.
        private Curve _shakeAttenuationInverseCurve;

        /// <summary>
        /// Current amount of shake. Access this field through property <c>Shake</c>.
        /// </summary>
        private float _shake;

        /// <summary>
        /// Time when field <c>shake</c> was calculated, in game time.
        /// </summary>
        private TimeSpan _shakeUpdateTime;

        private Vector2 _lastLookAtPos;
        private List<GobTrackerItem> _gobTrackerItems = new List<GobTrackerItem>();

        #endregion Player fields about general things

        #region Player fields about statistics

        /// <summary>
        /// Number of opposing players' ships this player has killed.
        /// </summary>
        private int _kills;

        /// <summary>
        /// Number of times this player has died for some other reason
        /// than another player killing him.
        /// </summary>
        private int _suicides;

        #endregion Player fields about statistics

        #region Player properties

        public List<GobTrackerItem> GobTrackerItems { get { return _gobTrackerItems; } set { _gobTrackerItems = value; } }

        public int KillsWithoutDying { get; set; }

        /// <summary>
        /// The player's Color on radar.
        /// </summary>
        public Color PlayerColor { get; set; }

        /// <summary>
        /// Does the player need a viewport on the game window.
        /// </summary>
        public override bool NeedsViewport { get { return !IsRemote; } }

        /// <summary>
        /// Does the player state need to be updated to the clients.
        /// For use by game server only.
        /// </summary>
        public bool MustUpdateToClients { get; set; }

        /// <summary>
        /// The ship the player is controlling in the game arena.
        /// </summary>
        public Ship Ship { get; set; }

        public Vector2 LookAtPos
        {
            get
            {
                if (Ship != null) _lastLookAtPos = Ship.Pos + Ship.DrawPosOffset;
                return _lastLookAtPos;
            }
        }

        /// <summary>
        /// If positive, how many reincarnations the player has left.
        /// If negative, the player has infinite lives.
        /// If zero, the player cannot play.
        /// </summary>
        public int Lives { get { return _lives; } set { _lives = value; } }

        /// <summary>
        /// Amount of shake the player is suffering right now, in radians.
        /// Shaking affects the player's viewport and is caused by
        /// the player's ship receiving damage.
        /// </summary>
        public float Shake
        {
            get
            {
                if (Game.DataEngine.ArenaTotalTime > _shakeUpdateTime)
                {
                    // Attenuate shake damage for any skipped frames.
                    float skippedTime = (float)(Game.DataEngine.ArenaTotalTime - Game.GameTime.ElapsedGameTime - _shakeUpdateTime).TotalSeconds;
                    AttenuateShake(skippedTime);

                    // Calculate new shake.
                    _shake = _shakeCurve.Evaluate(_relativeShakeDamage);
                    _shakeUpdateTime = Game.DataEngine.ArenaTotalTime;

                    // Attenuate shake damage for the current frame.
                    AttenuateShake((float)Game.GameTime.ElapsedGameTime.TotalSeconds);
                }
                return _shake;
            }
        }

        /// <summary>
        /// Increases the player's shake according to an amount of damage
        /// the player's ship has received. Negative amount will reduce
        /// shake.
        /// </summary>
        /// Shake won't get negative. There will be no shake if the player
        /// doesn't have a ship.
        /// <param name="damageAmount">The amount of damage.</param>
        public void IncreaseShake(float damageAmount)
        {
            if (Ship == null) return;
            _relativeShakeDamage = Math.Max(0, _relativeShakeDamage + damageAmount / Ship.MaxDamageLevel);
        }

        /// <summary>
        /// The name of the type of ship the player has chosen to fly.
        /// </summary>
        public CanonicalString ShipName { get; set; }

        /// <summary>
        /// The name of the primary weapon as the player has chosen it.
        /// </summary>
        public CanonicalString Weapon1Name
        {
            get
            {
                if (Ship != null) return Ship.Weapon1Name;
                var shipType = (Ship)Game.DataEngine.GetTypeTemplate(ShipName);
                return shipType.Weapon1Name;
            }
        }

        /// <summary>
        /// The name of the secondary weapon as the player has chosen it.
        /// </summary>
        public CanonicalString Weapon2Name { get; set; }

        /// <summary>
        /// The name of the extra device as the player has chosen it.
        /// </summary>
        public CanonicalString ExtraDeviceName { get; set; }

        public GameActionCollection BonusActions { get; private set; }

        /// <summary>
        /// Messages to display in the player's chat box, oldest first.
        /// </summary>
        public List<PlayerMessageEntry> Messages { get; private set; }

        public PostprocessEffectNameContainer PostprocessEffectNames { get; private set; }

        #endregion Player properties

        #region Player properties about statistics

        /// <summary>
        /// Number of opposing players' ships this player has killed.
        /// </summary>
        public int Kills { get { return _kills; } set { _kills = value; } }

        /// <summary>
        /// Number of times this player has died for some other reason
        /// than another player killing him.
        /// </summary>
        public int Suicides { get { return _suicides; } set { _suicides = value; } }

        #endregion Player properties about statistics

        public event Action<PlayerMessage> NewMessage;

        #region Constructors

        /// <summary>
        /// Creates a new player who plays at the local game instance.
        /// </summary>
        /// <param name="name">Name of the player.</param>
        /// <param name="shipTypeName">Name of the type of ship the player is flying.</param>
        /// <param name="weapon2Name">Name of the type of secondary weapon.</param>
        /// <param name="extraDeviceName">Name of the type of extra device.</param>
        /// <param name="controls">Player's in-game controls.</param>
        public Player(AssaultWingCore game, string name, CanonicalString shipTypeName, CanonicalString weapon2Name,
            CanonicalString extraDeviceName, PlayerControls controls)
            : this(game, name, shipTypeName, weapon2Name, extraDeviceName, controls, -1)
        {
        }

        /// <summary>
        /// Creates a new player who plays at a remote game instance.
        /// </summary>
        /// <param name="name">Name of the player.</param>
        /// <param name="shipTypeName">Name of the type of ship the player is flying.</param>
        /// <param name="weapon2Name">Name of the type of secondary weapon.</param>
        /// <param name="extraDeviceName">Name of the type of extra device.</param>
        /// <param name="connectionId">Identifier of the connection to the remote game instance
        /// at which the player lives.</param>
        /// <see cref="AW2.Net.Connection.ID"/>
        public Player(AssaultWingCore game, string name, CanonicalString shipTypeName, CanonicalString weapon2Name,
            CanonicalString extraDeviceName, int connectionId)
            : this(game, name, shipTypeName, weapon2Name, extraDeviceName, new PlayerControls
            {
                Thrust = new RemoteControl(),
                Left = new RemoteControl(),
                Right = new RemoteControl(),
                Down = new RemoteControl(),
                Fire1 = new RemoteControl(),
                Fire2 = new RemoteControl(),
                Extra = new RemoteControl()
            }, connectionId)
        {
        }

        private Player(AssaultWingCore game, string name, CanonicalString shipTypeName, CanonicalString weapon2Name,
            CanonicalString extraDeviceName, PlayerControls controls, int connectionId)
            : base(game, controls, connectionId)
        {
            KillsWithoutDying = 0;
            Name = name;
            ShipName = shipTypeName;
            Weapon2Name = weapon2Name;
            ExtraDeviceName = extraDeviceName;
            Messages = new List<PlayerMessageEntry>();
            _lives = 3;
            _shipSpawnTime = new TimeSpan(1);
            _relativeShakeDamage = 0;
            PlayerColor = Color.Gray;
            _shakeCurve = new Curve();
            _shakeCurve.PreLoop = CurveLoopType.Constant;
            _shakeCurve.PostLoop = CurveLoopType.Constant;
            _shakeCurve.Keys.Add(new CurveKey(0, 0));
            _shakeCurve.Keys.Add(new CurveKey(0.15f, 0.0f * MathHelper.PiOver4));
            _shakeCurve.Keys.Add(new CurveKey(0.3f, 0.4f * MathHelper.PiOver4));
            _shakeCurve.Keys.Add(new CurveKey(0.6f, 0.6f * MathHelper.PiOver4));
            _shakeCurve.Keys.Add(new CurveKey(1, MathHelper.PiOver4));
            _shakeCurve.ComputeTangents(CurveTangent.Linear);
            _shakeAttenuationCurve = new Curve();
            _shakeAttenuationCurve.PreLoop = CurveLoopType.Constant;
            _shakeAttenuationCurve.PostLoop = CurveLoopType.Linear;
            _shakeAttenuationCurve.Keys.Add(new CurveKey(0, 0));
            _shakeAttenuationCurve.Keys.Add(new CurveKey(0.05f, 0.01f));
            _shakeAttenuationCurve.Keys.Add(new CurveKey(1.0f, 1));
            _shakeAttenuationCurve.ComputeTangents(CurveTangent.Linear);
            _shakeAttenuationInverseCurve = new Curve();
            _shakeAttenuationInverseCurve.PreLoop = CurveLoopType.Constant;
            _shakeAttenuationInverseCurve.PostLoop = CurveLoopType.Linear;
            foreach (CurveKey key in _shakeAttenuationCurve.Keys)
                _shakeAttenuationInverseCurve.Keys.Add(new CurveKey(key.Value, key.Position));
            _shakeAttenuationInverseCurve.ComputeTangents(CurveTangent.Linear);
            BonusActions = new GameActionCollection(this);
            PostprocessEffectNames = new PostprocessEffectNameContainer(this);
        }

        #endregion Constructors

        #region General public methods

        public void RemoveGobTrackerItem(GobTrackerItem item)
        {
            if (item == null) throw new ArgumentNullException("Trying to remove NULL GobTrackerItem from the GobTrackerList");
            if (_gobTrackerItems.Contains(item)) _gobTrackerItems.Remove(item);
        }

        public void AddGobTrackerItem(GobTrackerItem item)
        {
            if (item == null) throw new ArgumentNullException("Trying to add NULL GobTrackerItem to the GobTrackerList");
            if (!_gobTrackerItems.Contains(item)) _gobTrackerItems.Add(item);
        }

        public override void Update()
        {
            foreach (var action in BonusActions)
            {
                action.Update();
                if (action.EndTime <= Game.DataEngine.ArenaTotalTime)
                    BonusActions.RemoveLater(action);
            }
            BonusActions.CommitRemoves();

            if (Game.NetworkMode != NetworkMode.Client)
            {
                if (Ship == null && _lives != 0 && _shipSpawnTime <= Game.DataEngine.ArenaTotalTime)
                    CreateShip();
                ApplyControlsToShip();
            }
            else // otherwise we are a game client
            {
                if (!IsRemote) ApplyControlsToShip();
            }
        }

        private void CreateSuicideMessage(Player perpetrator, Vector2 pos)
        {
            CreateDeathMessage(perpetrator, pos, "b_icon_take_life");
        }

        private void CreateKillMessage(Player perpetrator, Vector2 pos)
        {
            CreateDeathMessage(perpetrator, pos, "b_icon_add_kill");
        }

        private void CreateDeathMessage(Player perpetrator, Vector2 Pos, string iconName)
        {
            Gob.CreateGob<ArenaMessage>(Game, (CanonicalString)"deathmessage", gob =>
            {
                gob.ResetPos(Pos, Vector2.Zero, Gob.DEFAULT_ROTATION);
                gob.Message = perpetrator.Name;
                gob.IconName = iconName;
                gob.DrawColor = perpetrator.PlayerColor;
                Game.DataEngine.Arena.Gobs.Add(gob);
            });
        }

        /// <summary>
        /// Performs necessary operations when the player's ship dies.
        /// </summary>
        /// <param name="cause">The cause of death of the player's ship</param>
        public void Die(DeathCause cause)
        {
            Die_HandleCounters(cause);
            Die_SendMessages(cause);
            _shipSpawnTime = Game.DataEngine.ArenaTotalTime + TimeSpan.FromSeconds(MOURNING_DELAY);
            if (Game.NetworkMode == NetworkMode.Server) MustUpdateToClients = true;
        }

        public override AW2.Graphics.AWViewport CreateViewport(Rectangle onScreen)
        {
            return new AW2.Graphics.PlayerViewport(this, onScreen, () => PostprocessEffectNames);
        }

        /// <summary>
        /// Initialises the player for a game session, that is, for the first arena.
        /// </summary>
        public override void InitializeForGameSession()
        {
            Kills = Suicides = 0;
        }

        /// <summary>
        /// Resets the player's internal state for a new arena.
        /// </summary>
        public override void ResetForArena()
        {
            base.ResetForArena();
            _shipSpawnTime = TimeSpan.Zero;
            _shakeUpdateTime = TimeSpan.Zero;
            _relativeShakeDamage = 0;
            KillsWithoutDying = 0;
            Lives = Game.DataEngine.GameplayMode.StartLives;
            BonusActions.Clear();
            Messages.Clear();
            Ship = null;
        }

        public void SendMessage(string message)
        {
            SendMessage(new PlayerMessage(message, DEFAULT_COLOR));
        }

        /// <summary>
        /// Sends a message to the player. The message will be displayed on the player's screen.
        /// </summary>
        public void SendMessage(PlayerMessage message)
        {
            Messages.Add(new PlayerMessageEntry(message, Game.DataEngine.ArenaTotalTime));
            if (Messages.Count >= 2 * MESSAGE_KEEP_COUNT) Messages.RemoveRange(0, Messages.Count - MESSAGE_KEEP_COUNT);
            if (NewMessage != null) NewMessage(message);
        }

        public override void Dispose()
        {
            base.Dispose();
            if (Ship != null) Ship.Die(new DeathCause(Ship, DeathCauseType.Unspecified));
        }

        #endregion General public methods

        #region Methods related to serialisation

        /// <summary>
        /// Serialises the gob to a binary writer.
        /// </summary>
        /// Subclasses should call the base implementation
        /// before performing their own serialisation.
        /// <param name="writer">The writer where to write the serialised data.</param>
        /// <param name="mode">Which parts of the gob to serialise.</param>
        public override void Serialize(NetworkBinaryWriter writer, SerializationModeFlags mode)
        {
            base.Serialize(writer, mode);
            if ((mode & SerializationModeFlags.ConstantData) != 0)
            {
                writer.Write((CanonicalString)ShipName);
                writer.Write((CanonicalString)Weapon2Name);
                writer.Write((CanonicalString)ExtraDeviceName);
                writer.Write((Color)PlayerColor);
            }
            if ((mode & SerializationModeFlags.VaryingData) != 0)
            {
                writer.Write((short)_lives);
                writer.Write((short)_kills);
                writer.Write((short)_suicides);
                writer.Write((byte)PostprocessEffectNames.Count);
                foreach (var effectName in PostprocessEffectNames)
                    writer.Write((CanonicalString)effectName);
                BonusActions.Serialize(writer, mode);
            }
        }

        public override void Deserialize(NetworkBinaryReader reader, SerializationModeFlags mode, int framesAgo)
        {
            base.Deserialize(reader, mode, framesAgo);
            if ((mode & SerializationModeFlags.ConstantData) != 0)
            {
                ShipName = reader.ReadCanonicalString();
                Weapon2Name = reader.ReadCanonicalString();
                ExtraDeviceName = reader.ReadCanonicalString();
                PlayerColor = reader.ReadColor();
            }
            if ((mode & SerializationModeFlags.VaryingData) != 0)
            {
                _lives = reader.ReadInt16();
                _kills = reader.ReadInt16();
                _suicides = reader.ReadInt16();
                int effectNameCount = reader.ReadByte();
                PostprocessEffectNames.Clear();
                for (int i = 0; i < effectNameCount; ++i)
                    PostprocessEffectNames.Add(reader.ReadCanonicalString());
                BonusActions.Deserialize(reader, mode, framesAgo);
            }
        }

        #endregion Methods related to serialisation

        #region Private methods

        private void Die_HandleCounters(DeathCause cause)
        {
            if (cause.IsSuicide) ++_suicides;
            if (cause.IsKill)
            {
                ++cause.Killer.Owner._kills;
                if (Game.NetworkMode == NetworkMode.Server)
                    cause.Killer.Owner.MustUpdateToClients = true;
            }
            --_lives;
            KillsWithoutDying = 0;
            BonusActions.Clear();
        }

        private void Die_SendMessages(DeathCause cause)
        {
            if (cause.IsKill)
                CreateKillMessage(cause.Killer.Owner, Ship.Pos);
            if (cause.Killer != null && cause.Killer.Owner != null)
                cause.Killer.Owner.SendMessage(new PlayerMessage(cause.KillMessage, KILL_COLOR));
            if (cause.IsSuicide)
                CreateSuicideMessage(this, Ship.Pos);
            if (cause.IsSpecial)
            {
                var specialMessage = new PlayerMessage(cause.SpecialMessage, SPECIAL_KILL_COLOR);
                foreach (var plr in Game.DataEngine.Players) plr.SendMessage(specialMessage);
            }
            var bystanderMessage = new PlayerMessage(cause.BystanderMessage, KILL_COLOR);
            foreach (var plr in cause.GetBystanders(Game.DataEngine.Players)) plr.SendMessage(bystanderMessage);
            SendMessage(cause.DeathMessage);
            Ship = null;
        }

        /// <summary>
        /// Applies the player's controls to his ship, if there is any.
        /// </summary>
        private void ApplyControlsToShip()
        {
            if (Ship == null || Ship.IsDisposed) return;
            if (Ship.LocationPredicter != null)
            {
                Ship.LocationPredicter.StoreControlStates(Controls.GetStates(), Game.GameTime.TotalGameTime);
            }
            if (Controls.Thrust.Force > 0)
                Ship.Thrust(Controls.Thrust.Force, Game.GameTime.ElapsedGameTime, Ship.Rotation);
            if (Controls.Left.Force > 0)
                Ship.TurnLeft(Controls.Left.Force, Game.GameTime.ElapsedGameTime);
            if (Controls.Right.Force > 0)
                Ship.TurnRight(Controls.Right.Force, Game.GameTime.ElapsedGameTime);
            if (Controls.Fire1.Pulse || Controls.Fire1.Force > 0)
                Ship.Weapon1.Fire(Controls.Fire1.State);
            if (Controls.Fire2.Pulse || Controls.Fire2.Force > 0)
                Ship.Weapon2.Fire(Controls.Fire2.State);
            if (Controls.Extra.Pulse || Controls.Extra.Force > 0)
                Ship.ExtraDevice.Fire(Controls.Extra.State);
        }

        /// <summary>
        /// Creates a ship for the player.
        /// </summary>
        private void CreateShip()
        {
            // Gain ownership over the ship only after its position has been set.
            // This way the ship won't be affecting its own spawn position.
            Ship = null;
            Gob.CreateGob<Ship>(Game, ShipName, newShip =>
            {
                newShip.Owner = this;
                newShip.SetDeviceType(ShipDevice.OwnerHandleType.PrimaryWeapon, Weapon1Name);
                newShip.SetDeviceType(ShipDevice.OwnerHandleType.SecondaryWeapon, Weapon2Name);
                newShip.SetDeviceType(ShipDevice.OwnerHandleType.ExtraDevice, ExtraDeviceName);
                PositionNewShip(newShip);
                Game.DataEngine.Arena.Gobs.Add(newShip);
                Ship = newShip;
            });
        }

        private void PositionNewShip(Ship ship)
        {
            var arena = Game.DataEngine.Arena;

            // Use player spawn areas if there's any. Otherwise just randomise a position.
            var spawns =
                from g in arena.Gobs
                let spawn = g as SpawnPlayer
                where spawn != null
                let threat = spawn.GetThreat(this)
                orderby threat ascending
                select spawn;
            var bestSpawn = spawns.FirstOrDefault();
            if (bestSpawn == null)
            {
                var newShipPos = arena.GetFreePosition(ship,
                    new AW2.Helpers.Geometric.Rectangle(Vector2.Zero, arena.Dimensions));
                ship.ResetPos(newShipPos, Vector2.Zero, Gob.DEFAULT_ROTATION);
            }
            else
                bestSpawn.Spawn(ship);
        }

        /// <summary>
        /// Attenuates the player's viewport shake for passed time.
        /// </summary>
        /// This method should be called regularly. It decreases <c>relativeShakeDamage</c>.
        /// <param name="seconds">Passed time in seconds.</param>
        private void AttenuateShake(float seconds)
        {
            // Attenuation is done along a steepening curve;
            // the higher the shake damage the faster the attenuation.
            // 'relativeShakeDamage' is thought of as the value of the curve
            // for some parameter x which represents time to wait for the shake to stop.
            // In effect, this ensures that it won't take too long for
            // even very big shakes to stop.
            float shakeTime = _shakeAttenuationInverseCurve.Evaluate(_relativeShakeDamage);
            shakeTime = Math.Max(0, shakeTime - seconds);
            _relativeShakeDamage = _shakeAttenuationCurve.Evaluate(shakeTime);
        }

        #endregion Private methods
    }
}
