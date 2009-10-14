#if !DEBUG
#define OPTIMIZED_CODE // replace some function calls with fast elementary operations
#endif
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using AW2.Game.Particles;
using AW2.Helpers;
using AW2.UI;

namespace AW2.Game.Gobs
{
    /// <summary>
    /// A player's ship.
    /// </summary>
    public class Ship : Gob
    {
        #region Ship fields related to flying

        /// <summary>
        /// Maximum force of thrust of the ship, measured in Newtons.
        /// </summary>
        [TypeParameter]
        float thrustForce;

        /// <summary>
        /// Maximum turning speed of the ship, measured in radians per second.
        /// </summary>
        [TypeParameter]
        float turnSpeed;

        /// <summary>
        /// Ship's maximum speed reachable by thrust, measured in meters per second.
        /// </summary>
        [TypeParameter]
        float maxSpeed;

        #endregion Ship fields related to flying

        #region Ship fields related to weapons

        static readonly CanonicalString WEAPON_DEFAULT_NAME = (CanonicalString)"no weapon";

        /// <summary>
        /// Primary weapons of the ship.
        /// </summary>
        [RuntimeState]
        Weapon weapon1;

        /// <summary>
        /// Secondary weapons of the ship.
        /// </summary>
        [RuntimeState]
        Weapon weapon2;

        // Names of weapons to create when possible.
        CanonicalString weapon1Name, weapon2Name;

        /// <summary>
        /// Maximum amount of charge for primary weapons.
        /// </summary>
        [TypeParameter]
        float weapon1ChargeMax;

        /// <summary>
        /// Maximum amount of charge for secondary weapons.
        /// </summary>
        [TypeParameter]
        float weapon2ChargeMax;

        /// <summary>
        /// Speed of charging for primary weapon charge,
        /// measured in charge units per second.
        /// </summary>
        [TypeParameter]
        float weapon1ChargeSpeed;

        /// <summary>
        /// Speed of charging for secondary weapon charge,
        /// measured in charge units per second.
        /// </summary>
        [TypeParameter]
        float weapon2ChargeSpeed;

        /// <summary>
        /// Amount of charge for primary weapons,
        /// between <b>0</b> and <b>weapon1ChargeMax</b>.
        /// </summary>
        [RuntimeState]
        float weapon1Charge;

        /// <summary>
        /// Amount of charge for secondary weapons,
        /// between <b>0</b> and <b>weapon2ChargeMax</b>.
        /// </summary>
        [RuntimeState]
        float weapon2Charge;

        #endregion Ship fields related to weapons

        #region Ship fields related to rolling

        /// <summary>
        /// Current rotation of the ship around its tail-to-head axis.
        /// </summary>
        InterpolatingValue rollAngle;

        /// <summary>
        /// True iff <b>rollAngleGoal</b> has been set by ship turning this frame.
        /// </summary>
        bool rollAngleGoalUpdated;

        /// <summary>
        /// Maximum roll angle.
        /// </summary>
        /// Minimum roll angle will be the additive inverse.
        [TypeParameter]
        float rollMax;

        /// <summary>
        /// Roll angle change speed in radians per second.
        /// </summary>
        [TypeParameter]
        float rollSpeed;

        #endregion Ship fields related to rolling

        #region Ship fields related to coughing

        /// <summary>
        /// Names of cough engine types.
        /// </summary>
        [TypeParameter, ShallowCopy]
        CanonicalString[] coughEngineNames;

        /// <summary>
        /// Particle engines that manage coughing.
        /// </summary>
        Gob[] coughEngines;

        #endregion Ship fields related to coughing

        #region Ship fields related to other things

        /// <summary>
        /// Armour of the ship as a function that maps
        /// the amount of damage delivered to the ship
        /// to the amount of damage the ship actually receives.
        /// </summary>
        [TypeParameter, ShallowCopy]
        Curve armour;

        /// <summary>
        /// Alpha of the ship as a function that maps the age of the
        /// ship (in seconds) to the alpha value to draw the ship with.
        /// </summary>
        /// Use this to implement alpha flashing on ship birth.
        [TypeParameter, ShallowCopy]
        Curve birthAlpha;

        /// <summary>
        /// Name of the ship's icon in the equip menu main display.
        /// </summary>
        [TypeParameter]
        CanonicalString iconEquipName;

        /// <summary>
        /// True iff the amount of exhaust output has been set by ship thrusting this frame.
        /// </summary>
        bool exhaustAmountUpdated;

        /// <summary>
        /// Gobs that we have temporarily disabled while we move through them.
        /// </summary>
        List<Gob> temporarilyDisabledGobs;

        #endregion Ship fields related to other things

        #region Ship fields for signalling visual things over the network

        float visualThrustForce;
        bool visualWeapon1Fired;
        bool visualWeapon2Fired;

        #endregion Ship fields for signalling visual things over the network

        #region Ship properties

        /// <summary>
        /// Sets <see cref="Pos"/>, <see cref="Move"/> and <see cref="Rotation"/>
        /// as if the gob appeared there instantaneously
        /// as opposed to moving there in a continuous fashion.
        /// </summary>
        public override void ResetPos(Vector2 pos, Vector2 move, float rotation)
        {
            base.ResetPos(pos, move, rotation);
            if (LocationPredicter != null) LocationPredicter.ForgetOldShipLocations();
        }

        /// <summary>
        /// Returns the world matrix of the gob, i.e., the translation from
        /// game object coordinates to game world coordinates.
        /// </summary>
        public override Matrix WorldMatrix
        {
            get
            {
#if OPTIMIZED_CODE
                float scale = Scale;
                float rotation = Rotation;
                float scaledCosRoll = scale * (float)Math.Cos(rollAngle.Current);
                float scaledSinRoll = scale * (float)Math.Sin(rollAngle.Current);
                float cosRota = (float)Math.Cos(rotation);
                float sinRota = (float)Math.Sin(rotation);
                return new Matrix(
                    scale*cosRota, scale*sinRota, 0, 0,
                    -scaledCosRoll*sinRota, scaledCosRoll*cosRota, scaledSinRoll, 0,
                    scaledSinRoll*sinRota, -scaledSinRoll*cosRota, scaledCosRoll, 0,
                    pos.X, pos.Y, 0, 1);
#else
                return Matrix.CreateScale(Scale)
                     * Matrix.CreateRotationX(rollAngle.Current)
                     * Matrix.CreateRotationZ(Rotation)
                     * Matrix.CreateTranslation(new Vector3(Pos, 0));
#endif
            }
        }

        public float TurnSpeed { get { return turnSpeed; } }

        public float ThrustForce { get { return thrustForce; } }

        /// <summary>
        /// The primary weapon of the ship.
        /// </summary>
        public Weapon Weapon1 { get { return weapon1; } }

        /// <summary>
        /// The secondary weapon of the ship.
        /// </summary>
        public Weapon Weapon2 { get { return weapon2; } }

        /// <summary>
        /// Name of the type of main weapon the ship is using.
        /// </summary>
        public CanonicalString Weapon1Name
        {
            get { return weapon1 == null ? WEAPON_DEFAULT_NAME : weapon1.TypeName; }
            set
            {
                // Null weapon means we're not yet activated. Then create weapon later.
                if (weapon1 != null)
                {
                    AssaultWing.Instance.DataEngine.Weapons.Remove(weapon1);
                    weapon1.Dispose();
                    weapon1 = CreateWeapons(value, 1);
                }
                else
                    weapon1Name = value;
            }
        }

        /// <summary>
        /// Name of the type of secondary weapon the ship is using.
        /// </summary>
        public CanonicalString Weapon2Name
        {
            get { return weapon2 == null ? WEAPON_DEFAULT_NAME : weapon2.TypeName; }
            set
            {
                // Null weapon means we're not yet activated. Then create weapon later.
                if (weapon2 != null)
                {
                    AssaultWing.Instance.DataEngine.Weapons.Remove(weapon2);
                    weapon2.Dispose();
                    weapon2 = CreateWeapons(value, 2);
                }
                else
                    weapon2Name = value;
            }
        }

        /// <summary>
        /// Is any of the primary weapons loaded.
        /// </summary>
        public bool Weapon1Loaded
        {
            get
            {
                return weapon1.Loaded;
            }
        }

        /// <summary>
        /// Is any of the secondary weapons loaded.
        /// </summary>
        public bool Weapon2Loaded
        {
            get
            {
                return weapon2.Loaded;
            }
        }

        /// <summary>
        /// Amount of charge for primary weapons,
        /// between <b>0</b> and <b>Weapon1ChargeMax</b>.
        /// </summary>
        public float Weapon1Charge { 
            get { return weapon1Charge; }
            set { weapon1Charge = MathHelper.Clamp(value, 0, weapon1ChargeMax); }
        }

        /// <summary>
        /// Maximum amount of charge for primary weapons.
        /// </summary>
        public float Weapon1ChargeMax { get { return weapon1ChargeMax; } }

        /// <summary>
        /// Amount of charge for secondary weapons,
        /// between <b>0</b> and <b>Weapon2ChargeMax</b>.
        /// </summary>
        public float Weapon2Charge { 
            get { return weapon2Charge; }
            set { weapon2Charge = MathHelper.Clamp(value, 0, weapon2ChargeMax); }
        }

        /// <summary>
        /// Maximum amount of charge for secondary weapons.
        /// </summary>
        public float Weapon2ChargeMax { get { return weapon2ChargeMax; } }

        /// <summary>
        /// Name of the ship's icon in the equip menu main display.
        /// </summary>
        public CanonicalString IconEquipName { get { return iconEquipName; } set { iconEquipName = value; } }

        /// <summary>
        /// Names of all textures that this gob type will ever use.
        /// </summary>
        public override IEnumerable<CanonicalString> TextureNames
        {
            get { return base.TextureNames.Union(new CanonicalString[] { iconEquipName }); }
        }

        #endregion Ship properties

        #region Ship constructors

        /// <summary>
        /// Creates an uninitialised ship.
        /// </summary>
        /// This constructor is only for serialisation.
        public Ship()
            : base()
        {
            this.thrustForce = 100;
            this.turnSpeed = 3;
            this.maxSpeed = 200;
            this.rollMax = (float)MathHelper.PiOver4;
            this.rollSpeed = (float)(MathHelper.TwoPi / 2.0);
            this.weapon1ChargeMax = 5000;
            this.weapon1ChargeSpeed = 500;
            this.weapon1Charge = this.weapon1ChargeMax;
            this.weapon2ChargeMax = 5000;
            this.weapon2ChargeSpeed = 500;
            this.weapon2Charge = this.weapon2ChargeMax;
            this.armour = new Curve();
            this.armour.PreLoop = CurveLoopType.Linear;
            this.armour.PostLoop = CurveLoopType.Linear;
            this.armour.Keys.Add(new CurveKey(-500, -500, 1, 500 * 1, CurveContinuity.Smooth));
            this.armour.Keys.Add(new CurveKey(0, 0, 500 * 1, 10 * 0.3f, CurveContinuity.Smooth));
            this.armour.Keys.Add(new CurveKey(10, 7, 10 * 1, 40 * 1, CurveContinuity.Smooth));
            this.armour.Keys.Add(new CurveKey(50, 50, 40 * 1, 450 * 1, CurveContinuity.Smooth));
            this.armour.Keys.Add(new CurveKey(500, 500, 450 * 1, 1, CurveContinuity.Smooth));
            this.birthAlpha = new Curve();
            this.birthAlpha.PreLoop = CurveLoopType.Constant;
            this.birthAlpha.PostLoop = CurveLoopType.Constant;
            for (float age = 0; age + 0.2f < 2; age += 0.4f)
            {
                this.birthAlpha.Keys.Add(new CurveKey(age, 0.2f));
                this.birthAlpha.Keys.Add(new CurveKey(age + 0.2f, 0.8f));
            }
            this.birthAlpha.Keys.Add(new CurveKey(2, 1));
            this.birthAlpha.ComputeTangents(CurveTangent.Flat);
            this.coughEngineNames = new CanonicalString[] { (CanonicalString)"dummyparticleengine", };
            this.temporarilyDisabledGobs = new List<Gob>();
            this.iconEquipName = (CanonicalString)"dummytexture";
        }

        /// <summary>
        /// Creates a new ship.
        /// </summary>
        /// <param name="typeName">Type of the ship.</param>
        public Ship(CanonicalString typeName)
            : base(typeName)
        {
            this.weapon1 = null;
            this.weapon2 = null;
            this.temporarilyDisabledGobs = new List<Gob>();
            LocationPredicter = new ShipLocationPredicter(this);
        }

        #endregion Ship constructors

        #region Protected methods

        /// <summary>
        /// Called when the ship is thrusting.
        /// </summary>
        /// <param name="thrustForce">Thrusting force</param>
        protected virtual void Thrusting(float thrustForce) { }

        /// <summary>
        /// Called when the ship is turning.
        /// </summary>
        protected virtual void Turning(float turnAngle) { }

        #endregion Protected methods

        #region Private methods

        /// <summary>
        /// Creates a new instance of a named weapon type so that all
        /// gun barrels on the ship are covered.
        /// </summary>
        /// <param name="weaponName">Name of the weapon type.</param>
        /// <param name="ownerHandle">A handle for identifying the weapon at the owner.
        /// Use <b>1</b> for primary weapons and <b>2</b> for secondary weapons.</param>
        /// <returns>The created weapon.</returns>
        private Weapon CreateWeapons(CanonicalString weaponName, int ownerHandle)
        {
            KeyValuePair<string, int>[] boneIs = GetNamedPositions("Gun");
            if (boneIs.Length == 0)
                Log.Write("Warning: Ship found no gun barrels in its 3D model");
            int[] boneIndices = Array.ConvertAll<KeyValuePair<string, int>, int>(boneIs, pair => pair.Value);
            Weapon weapon = Weapon.CreateWeapon(weaponName, this, ownerHandle, boneIndices);
            AssaultWing.Instance.DataEngine.Weapons.Add(weapon);
            return weapon;
        }

        /// <summary>
        /// Creates cough engines for the ship.
        /// </summary>
        private void CreateCoughEngines()
        {
            List<Gob> coughEngineList = new List<Gob>();
            for (int i = 0; i < coughEngineNames.Length; ++i)
            {
                Gob.CreateGob(coughEngineNames[i], gob =>
                {
                    if (gob is ParticleEngine)
                    {
                        ParticleEngine peng = (ParticleEngine)gob;
                        peng.Loop = true;
                        peng.IsAlive = false;
                        peng.Leader = this;
                    }
                    else if (gob is Peng)
                    {
                        Peng peng = (Peng)gob;
                        peng.Paused = true;
                        peng.Leader = this;
                    }
                    Arena.Gobs.Add(gob);
                    coughEngineList.Add(gob);
                });
            }
            coughEngines = coughEngineList.ToArray();
        }

        private void UpdateWeaponCharges(TimeSpan elapsedGameTime)
        {
            weapon1Charge += weapon1ChargeSpeed * (float)elapsedGameTime.TotalSeconds;
            weapon1Charge = MathHelper.Clamp(weapon1Charge, 0, weapon1ChargeMax);
            weapon2Charge += weapon2ChargeSpeed * (float)elapsedGameTime.TotalSeconds;
            weapon2Charge = MathHelper.Clamp(weapon2Charge, 0, weapon2ChargeMax);
        }

        #endregion Private methods

        #region Methods related to gobs' functionality in the game world

        /// <summary>
        /// Activates the gob, i.e. performs an initialisation rite.
        /// </summary>
        public override void Activate()
        {
            base.Activate();
            if (weapon1Name != CanonicalString.Null) weapon1 = CreateWeapons(weapon1Name, 1);
            if (weapon2Name != CanonicalString.Null) weapon2 = CreateWeapons(weapon2Name, 2);
            weapon1Charge = weapon1ChargeMax;
            weapon2Charge = weapon2ChargeMax;
            SwitchExhaustEngines(false);
            exhaustAmountUpdated = false;
            CreateCoughEngines();
        }

        /// <summary>
        /// Updates the ship's internal state.
        /// </summary>
        public override void Update()
        {
            // Manage turn-related rolling.
            rollAngle.Step = AssaultWing.Instance.PhysicsEngine.ApplyChange(rollSpeed, AssaultWing.Instance.GameTime.ElapsedGameTime);
            rollAngle.Advance();
            if (!rollAngleGoalUpdated)
                rollAngle.Target = 0;
            rollAngleGoalUpdated = false;

            LocationPredicter.StoreOldShipLocation(new ShipLocationEntry
            {
                GameTime = AssaultWing.Instance.GameTime.TotalGameTime - AssaultWing.Instance.GameTime.ElapsedGameTime,
                Pos = Pos,
                Move = Move,
                Rotation = Rotation
            });
            base.Update();
            
            // Re-enable temporarily disabled gobs.
            foreach (Gob gob in temporarilyDisabledGobs)
                gob.Disabled = false;
            temporarilyDisabledGobs.Clear();

            // Manage exhaust engines.
            if (!exhaustAmountUpdated)
                SwitchExhaustEngines(false);
            exhaustAmountUpdated = false;

            // Manage cough engines.
            float coughArgument = (DamageLevel / MaxDamageLevel - 0.8f) / 0.2f;
            coughArgument = MathHelper.Clamp(coughArgument, 0, 1);
            foreach (Gob coughEngine in coughEngines)
                if (coughEngine is ParticleEngine)
                {
                    ((ParticleEngine)coughEngine).Argument = coughArgument;
                    ((ParticleEngine)coughEngine).IsAlive = coughArgument > 0;
                }
                else if (coughEngine is Peng)
                {
                    ((Peng)coughEngine).Input = coughArgument;
                    ((Peng)coughEngine).Paused = coughArgument == 0;
                }

            UpdateWeaponCharges(AssaultWing.Instance.GameTime.ElapsedGameTime);

            // Flash and be disabled if we're just born.
            float age = birthTime.SecondsAgo();
            Alpha = birthAlpha.Evaluate(age);
            Disabled = age < birthAlpha.Keys[birthAlpha.Keys.Count - 1].Position;
        }

        /// <summary>
        /// Kills the gob, i.e. performs a death ritual and removes the gob from the game world.
        /// </summary>
        /// <param name="cause">The cause of death.</param>
        public override void Die(DeathCause cause)
        {
            if (Dead) return;
            if (Owner != null)
                Owner.Die(cause);
            base.Die(cause);
        }

        /// <summary>
        /// Releases all resources allocated by the gob.
        /// </summary>
        public override void Dispose()
        {
            AssaultWing.Instance.DataEngine.Weapons.Remove(weapon1);
            AssaultWing.Instance.DataEngine.Weapons.Remove(weapon2);
            base.Dispose();
        }

        #endregion Methods related to gobs' functionality in the game world

        #region Methods related to serialisation

        /// <summary>
        /// Copies the gob's runtime state from another gob.
        /// </summary>
        /// <param name="runtimeState">The gob whose runtime state to imitate.</param>
        protected override void SetRuntimeState(Gob runtimeState)
        {
            base.SetRuntimeState(runtimeState);
            exhaustAmountUpdated = false;
        }

        /// <summary>
        /// Serialises the gob for to a binary writer.
        /// </summary>
        /// <param name="writer">The writer where to write the serialised data.</param>
        /// <param name="mode">Which parts of the gob to serialise.</param>
        public override void Serialize(Net.NetworkBinaryWriter writer, Net.SerializationModeFlags mode)
        {
            base.Serialize(writer, mode);
            if ((mode & AW2.Net.SerializationModeFlags.ConstantData) != 0)
            {
                writer.Write((int)weapon1.TypeName.Canonical);
                writer.Write((int)weapon2.TypeName.Canonical);
            }
            if ((mode & AW2.Net.SerializationModeFlags.VaryingData) != 0)
            {
                writer.Write((Half)weapon1Charge);
                writer.Write((Half)weapon2Charge);
                writer.Write((Half)visualThrustForce);
                byte flags = (byte)(
                    (Weapon1Loaded ? 0x01 : 0x00) |
                    (Weapon2Loaded ? 0x02 : 0x00) |
                    (visualWeapon1Fired ? 0x04 : 0x00) |
                    (visualWeapon2Fired ? 0x08 : 0x00));
                writer.Write((byte)flags);

                visualThrustForce = 0;
                visualWeapon1Fired = false;
                visualWeapon2Fired = false;
            }
        }

        /// <summary>
        /// Deserialises the gob from a binary writer.
        /// </summary>
        /// <param name="reader">The reader where to read the serialised data.</param>
        /// <param name="mode">Which parts of the gob to deserialise.</param>
        public override void Deserialize(Net.NetworkBinaryReader reader, Net.SerializationModeFlags mode, TimeSpan messageAge)
        {
            base.Deserialize(reader, mode, messageAge);
            if ((mode & AW2.Net.SerializationModeFlags.ConstantData) != 0)
            {
                Weapon1Name = (CanonicalString)reader.ReadInt32();
                Weapon2Name = (CanonicalString)reader.ReadInt32();
            }
            if ((mode & AW2.Net.SerializationModeFlags.VaryingData) != 0)
            {
                weapon1Charge = reader.ReadHalf();
                weapon2Charge = reader.ReadHalf();
                float thrustForce = reader.ReadHalf();
                byte flags = reader.ReadByte();
                if (Weapon1 != null) Weapon1.Loaded = (flags & 0x01) != 0;
                if (Weapon2 != null) Weapon2.Loaded = (flags & 0x02) != 0;
                bool weapon1Fired = (flags & 0x04) != 0;
                bool weapon2Fired = (flags & 0x08) != 0;

                UpdateWeaponCharges(messageAge);
                if (thrustForce > 0)
                    Thrust(thrustForce, AssaultWing.Instance.GameTime.ElapsedGameTime);
                // TODO: Fire1() and Fire2() are intended to create muzzle pengs
                // but they don't. Fix this by inheriting Weapon from Gob and serialising
                // muzzleFireEngine state over the network.
                if (weapon1Fired)
                    Fire1();
                if (weapon2Fired)
                    Fire2();
            }
        }

        #endregion Methods related to serialisation

        #region Ship public methods

        /// <summary>
        /// Thrusts the ship.
        /// </summary>
        /// <param name="force">Force of thrust; between 0 and 1.</param>
        public void Thrust(float force, TimeSpan duration)
        {
            if (Disabled) return;
            force = MathHelper.Clamp(force, 0f, 1f);
            Vector2 forceVector = AWMathHelper.GetUnitVector2(Rotation) * force * thrustForce;
            AssaultWing.Instance.PhysicsEngine.ApplyLimitedForce(this, forceVector, maxSpeed, duration);
            visualThrustForce = force;
            Thrusting(force);

            // Manage exhaust engines.
            SwitchExhaustEngines(true);
            exhaustAmountUpdated = true;
        }

        /// <summary>
        /// Turns the ship left.
        /// </summary>
        /// <param name="force">Force of turn; between 0 and 1.</param>
        public void TurnLeft(float force, TimeSpan duration)
        {
            if (Disabled) return;
            force = MathHelper.Clamp(force, 0f, 1f);
            Turn(force, duration);
        }

        /// <summary>
        /// Turns the ship right.
        /// </summary>
        /// <param name="force">Force of turn; between 0 and 1.</param>
        public void TurnRight(float force, TimeSpan duration)
        {
            if (Disabled) return;
            force = MathHelper.Clamp(force, 0f, 1f);
            Turn(-force, duration);
        }

        /// <summary>
        /// Turns the ship right or left.
        /// </summary>
        /// <param name="force">Force of turn; (0,1] for a left turn, or [-1,0) for a right turn.</param>
        private void Turn(float force, TimeSpan duration)
        {
            force = MathHelper.Clamp(force, -1f, 1f);
            float deltaRotation = AssaultWing.Instance.PhysicsEngine.ApplyChange(force * turnSpeed, duration);
            Rotation += deltaRotation;
            Turning(deltaRotation);

            Vector2 headingNormal = Vector2.Transform(Vector2.UnitX, Matrix.CreateRotationZ(Rotation));
            float moveLength = Move.Length();
            float headingFactor = // fancy roll
                moveLength == 0 ? 0 :
                moveLength <= maxSpeed ? Vector2.Dot(headingNormal, Move / maxSpeed) :
                Vector2.Dot(headingNormal, Move / moveLength);
            //float headingFactor = 1.0f; // naive roll
            rollAngle.Target = -rollMax * force * headingFactor;
            rollAngleGoalUpdated = true;
        }

        /// <summary>
        /// Fires the main weapon.
        /// </summary>
        public void Fire1()
        {
            if (Disabled) return;
            weapon1.Fire();
            visualWeapon1Fired = true;
        }

        /// <summary>
        /// Fires the secondary weapon.
        /// </summary>
        public void Fire2()
        {
            if (Disabled) return;
            weapon2.Fire();
            visualWeapon2Fired = true;
        }

        /// <summary>
        /// Performs an extra function which depends on the ship state.
        /// </summary>
        public void DoExtra()
        {
            if (Disabled) return;
            // !!! not implemented
        }

        /// <summary>
        /// Returns the amount of charge available for a weapon with
        /// a certain handle.
        /// </summary>
        /// <param name="ownerHandle">The owner handle of the weapon.</param>
        /// <returns>The amount of charge available for the weapon.</returns>
        public float GetCharge(int ownerHandle)
        {
            switch (ownerHandle)
            {
                case 1: return Weapon1Charge;
                case 2: return Weapon2Charge;
                default:
                    Log.Write("Warning: Someone inquired weapon charge for owner handle "
                        + ownerHandle);
                    return 0;
            }
        }
        
        /// <summary>
        /// Uses an amount of charge available for a weapon with
        /// a certain handle.
        /// </summary>
        /// <param name="ownerHandle">The owner handle of the weapon.</param>
        /// <param name="amount">The amount of charge to use.</param>
        public void UseCharge(int ownerHandle, float amount)
        {
            switch (ownerHandle)
            {
                case 1:
                    weapon1Charge = MathHelper.Clamp(weapon1Charge - amount, 0, weapon1ChargeMax);
                    break;
                case 2:
                    weapon2Charge = MathHelper.Clamp(weapon2Charge - amount, 0, weapon2ChargeMax);
                    break;
                default:
                    Log.Write("Warning: Someone tried to use weapon charge for owner handle "
                        + ownerHandle);
                    break;
            }
        }

        #endregion Ship public methods

        /// <summary>
        /// Performs collision operations for the case when one of this gob's collision areas
        /// is overlapping one of another gob's collision areas.
        /// </summary>
        /// Called only when <b>theirArea.Type</b> matches either <b>myArea.CollidesAgainst</b> or
        /// <b>myArea.CannotOverlap</b>.
        /// <param name="myArea">The collision area of this gob.</param>
        /// <param name="theirArea">The collision area of the other gob.</param>
        /// <param name="stuck">If <b>true</b> then the gob is stuck, i.e.
        /// <b>theirArea.Type</b> matches <b>myArea.CannotOverlap</b> and it's not possible
        /// to backtrack out of the overlap. It is then up to this gob and the other gob 
        /// to resolve the overlap.</param>
        public override void Collide(CollisionArea myArea, CollisionArea theirArea, bool stuck)
        {
            if (stuck)
            {
                // Set the other gob as disabled while we move, then enable it after we finish moving.
                // This works with the assumption that there are at least two moving iterations.
                theirArea.Owner.Disabled = true;
                temporarilyDisabledGobs.Add(theirArea.Owner);
            }
        }

        /// <summary>
        /// Inflicts damage on the entity.
        /// </summary>
        /// <param name="damageAmount">If positive, amount of damage;
        /// if negative, amount of repair.</param>
        /// <param name="cause">The cause of death.</param>
        public override void InflictDamage(float damageAmount, DeathCause cause)
        {
            float realDamage = armour.Evaluate(damageAmount);
            if (Owner != null)
                Owner.IncreaseShake(realDamage);
            base.InflictDamage(realDamage, cause);
        }

        public ShipLocationPredicter LocationPredicter { get; private set; }
    }
}
