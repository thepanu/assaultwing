using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using AW2.Helpers;
using AW2.Game.Pengs;

namespace AW2.Game.Gobs
{
    /// <summary>
    /// Particle engine, i.e. a peng.
    /// </summary>
    /// Peng creates particles and gobs. Particles are sprites that are fully
    /// managed by the peng. Created gobs are independent of the peng and are
    /// managed by general game logic. Peng has its own coordinate system
    /// in which its particles reside. Peng's coordinate system's origin is
    /// at <c>Pos</c> in game coordinates and it is turned <c>Rotation</c> 
    /// radians from game coordinate's orientation.
    [LimitedSerialization]
    public class Peng : Gob, IConsistencyCheckable
    {
        /// <summary>
        /// Type of coordinate system to use with particles.
        /// </summary>
        public enum CoordinateSystem
        {
            /// <summary>
            /// Peng's own coordinate system.
            /// </summary>
            Peng,

            /// <summary>
            /// Game world coordinate system.
            /// </summary>
            Game,
        }

        #region Peng fields

        [TypeParameter]
        ParticleEmitter emitter;
        [TypeParameter]
        ParticleUpdater updater;

        /// <summary>
        /// Particle draw order relative to other pengs.
        /// 0 is front, 1 is back.
        /// Use at most two-decimal precision (e.g. 0.14, 0.50, 1.00).
        /// </summary>
        [TypeParameter]
        float depthLayer;

        /// <summary>
        /// The coordinate system in which to interpret our particles' <c>pos</c> field.
        /// </summary>
        [TypeParameter]
        CoordinateSystem coordinateSystem;

        /// <summary>
        /// External input argument of the peng, between 0 and 1.
        /// </summary>
        /// This value can be set by anyone and it may affect the behaviour
        /// of the peng's emitter and updater.
        [RuntimeState]
        float input;

        /// <summary>
        /// Currently active particles of this peng.
        /// </summary>
        [RuntimeState]
        List<Particle> particles;

        /// <summary>
        /// <c>null</c> or the gob that determines the origin of the peng's coordinate system.
        /// </summary>
        /// Idiom: follow the leader.
        /// <seealso cref="leaderBone"/>
        Gob leader;

        /// <summary>
        /// The index of the bone on the leader gob that is the origin of the peng's coordinate system,
        /// or -1 if the leader's center is the origin.
        /// </summary>
        /// If <c>leader==null</c> then this field has no effect.
        /// <seealso cref="leader"/>
        int leaderBone;

        /// <summary>
        /// Position of the peng in the previous frame, or NaN if unspecified.
        /// </summary>
        Vector2 oldPos;

        /// <summary>
        /// Last known value of <c>Pos</c> after a frame update, or NaN if unspecified.
        /// </summary>
        Vector2 prevPos;

        /// <summary>
        /// Time of last update to field <c>oldPos</c>.
        /// </summary>
        TimeSpan oldPosTimestamp;

        #endregion Peng fields

        #region Peng properties

        /// <summary>
        /// Names of all textures that this gob type will ever use.
        /// </summary>
        public override List<string> TextureNames
        {
            get
            {
                List<string> names = base.TextureNames;
                names.AddRange(emitter.TextureNames);
                return names;
            }
        }

        /// <summary>
        /// Position of the peng and the origin of its coordinate system.
        /// </summary>
        public override Vector2 Pos
        {
            get
            {
                if (leader == null) return base.Pos;
                if (leaderBone == -1)
                    return leader.Pos;
                else
                    return leader.GetNamedPosition(leaderBone);
            }
        }

        /// <summary>
        /// Position of the peng before movement in the current frame.
        /// </summary>
        public Vector2 OldPos
        {
            get
            {
                // TODO: Update oldPos from pos when AssaultWing.Instance.GameTime.TotalGameTime has advanced
                if (float.IsNaN(oldPos.X)) return pos;
                return oldPos;
            }
        }

        /// <summary>
        /// Movement vector of the peng.
        /// </summary>
        public override Vector2 Move
        {
            get
            {
                if (leader == null) return base.Move;
                return leader.Move;
            }
        }

        /// <summary>
        /// Rotation of the peng around the Z-axis, i.e. the direction of the
        /// peng's coordinate system's X axis in game coordinates.
        /// </summary>
        public override float Rotation
        {
            get
            {
                if (leader == null) return base.Rotation;
                return leader.Rotation;
            }
        }

        /// <summary>
        /// External input argument of the peng, between 0 and 1.
        /// </summary>
        /// This value can be set by anyone and it may affect the behaviour
        /// of the peng's emitter and updater.
        public float Input { get { return input; } set { input = value; } }

        /// <summary>
        /// The coordinate system in which to interpret the <c>pos</c> field of
        /// the particles of this peng.
        /// </summary>
        public CoordinateSystem ParticleCoordinates { get { return coordinateSystem; } }

        /// <summary>
        /// <c>null</c> or the gob that determines the origin of the peng's coordinate system.
        /// </summary>
        /// Idiom: follow the leader.
        /// <seealso cref="LeaderBone"/>
        public Gob Leader { get { return leader; } set { leader = value; } }

        /// <summary>
        /// The index of the bone on the leader gob that is the origin of the peng's coordinate system,
        /// or -1 if the leader's center is the origin.
        /// </summary>
        /// If <c>Leader == null</c> then this field has no effect.
        /// <seealso cref="Leader"/>
        public int LeaderBone { get { return leaderBone; } set { leaderBone = value; } }

        /// <summary>
        /// The world matrix of the peng, i.e. the transformation from
        /// peng coordinates to game coordinates.
        /// </summary>
        public override Matrix WorldMatrix
        {
            get
            {
                return Matrix.CreateRotationZ(Rotation)
                     * Matrix.CreateTranslation(new Vector3(Pos, 0));
            }
        }

        #endregion Peng properties

        /// <summary>
        /// Creates an uninitialised peng.
        /// </summary>
        /// This constructor is only for serialisation.
        public Peng()
        {
            emitter = new SprayEmitter();
            updater = new PhysicalUpdater();
            depthLayer = 0.5f;
            coordinateSystem = CoordinateSystem.Game;
            particles = new List<Particle>();

            // Remove default collision areas set by class Gob so that we don't need to explicitly state
            // in each peng's XML definition that there are no collision areas.
            collisionAreas = new CollisionArea[0];
        }

        /// <summary>
        /// Creates a peng.
        /// </summary>
        /// <param name="typeName">The type of the peng.</param>
        public Peng(string typeName)
            : base(typeName)
        {
            input = 0;
            leader = null;
            leaderBone = -1;
            oldPos = new Vector2(Single.NaN);
            prevPos = new Vector2(Single.NaN);
            oldPosTimestamp = TimeSpan.Zero;
            particles = new List<Particle>();

            // Gain ownership of our emitter.
            emitter.Peng = this;
        }

        #region Methods related to gobs' functionality in the game world

        /// <summary>
        /// Updates the gob according to physical laws.
        /// </summary>
        public override void Update()
        {
            base.Update();
            UpdateOldPos();

            // Create particles.
            ICollection<Particle> newParticles = emitter.Emit();
            if (newParticles != null) 
                particles.AddRange(newParticles);

            // Update and kill particles.
            for (int i = 0; i < particles.Count; )
                if (updater.Update(particles[i]))
                    particles.RemoveAt(i);
                else
                    ++i;

            // Die by our leader.
            if (leader != null && leader.Dead)
            {
                Die(new DeathCause());
                leader = null;
            }

            // Die if we're finished.
            if (particles.Count == 0 && emitter.Finished)
                Die(new DeathCause());
        }

        /// <summary>
        /// Draws the gob's 3D graphics.
        /// </summary>
        /// <param name="view">The view matrix.</param>
        /// <param name="projection">The projection matrix.</param>
        public override void Draw(Matrix view, Matrix projection)
        {
            // Peng has no 3D graphics.
        }

        /// <summary>
        /// Draws the gob's 2D graphics.
        /// </summary>
        /// Assumes that the sprite batch has been Begun already and will be
        /// Ended later by someone else.
        /// <param name="gameToScreen">Transformation from game coordinates 
        /// to screen coordinates (pixels).</param>
        /// <param name="spriteBatch">The sprite batch to draw sprites with.</param>
        /// <param name="scale">Scale of graphics.</param>
        public override void Draw2D(Matrix gameToScreen, SpriteBatch spriteBatch, float scale)
        {
            DataEngine data = (DataEngine)AssaultWing.Instance.Services.GetService(typeof(DataEngine));
            Viewport gfxViewport = AssaultWing.Instance.GraphicsDevice.Viewport;
            Vector3 viewportSize = new Vector3(gfxViewport.Width, gfxViewport.Height, gfxViewport.MaxDepth - gfxViewport.MinDepth);
            Matrix pengToGame = WorldMatrix;

            foreach (Particle particle in particles)
            {
                // Find out particle's center's position on screen.
                Vector2 posCenter = particle.pos;
                if (coordinateSystem == CoordinateSystem.Peng)
                    posCenter = Vector2.Transform(posCenter, pengToGame);
                Vector2 screenCenter = Vector2.Transform(posCenter, gameToScreen);

                // Sprite depth will be our given depth layer slightly adjusted by
                // particle's position in its lifespan.
                // TODO: Scale particle as told in 'view' -- transform quad corners by view*projection
                float layerDepth = MathHelper.Clamp(depthLayer * 0.99f + 0.0098f * particle.layerDepth, 0, 1);
                Texture2D texture = data.GetTexture(particle.textureName);
                float drawRotation = coordinateSystem == CoordinateSystem.Game
                    ? particle.rotation
                    : particle.rotation + Rotation;
                drawRotation = -drawRotation; // negated, because screen Y coordinates are reversed
                spriteBatch.Draw(texture, screenCenter, null,
                    new Color(new Vector4(1, 1, 1, particle.alpha)), drawRotation,
                    new Vector2(texture.Width, texture.Height) / 2, particle.scale * scale,
                    SpriteEffects.None, layerDepth);
            }
        }

        #endregion Methods related to gobs' functionality in the game world

        /// <summary>
        /// Maintains a valid state of field <c>oldPos</c>.
        /// </summary>
        /// Call this method after frame update has finished.
        /// Calling this method more than once after one frame update
        /// has no further effects.
        void UpdateOldPos()
        {
            if (oldPosTimestamp >= AssaultWing.Instance.GameTime.TotalGameTime) return;
            if (float.IsNaN(prevPos.X))
                oldPos = Pos;
            else
                oldPos = prevPos;
            prevPos = Pos;
            oldPosTimestamp = AssaultWing.Instance.GameTime.TotalGameTime;
        }
    }
}