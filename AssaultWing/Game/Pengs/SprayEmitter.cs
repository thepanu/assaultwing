using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Xna.Framework;
using AW2.Game.Gobs;
using AW2.Helpers;

namespace AW2.Game.Pengs
{
    /// <summary>
    /// Particle emitter spilling stuff radially outwards from a circle sector 
    /// (or a full circle) in a radius from its center.
    /// </summary>
    /// The center is located at the origin of the peng's coordinate system.
    [LimitedSerialization]
    public class SprayEmitter : ParticleEmitter, IConsistencyCheckable
    {
        /// <summary>
        /// Type of initial particle facing.
        /// </summary>
        enum FacingType
        {
            /// <summary>
            /// Particles face the average emission direction.
            /// </summary>
            Directed,

            /// <summary>
            /// Particles face the direction where they start moving,
            /// that is, away from the emission center.
            /// </summary>
            Forward,

            /// <summary>
            /// Particles face in random directions.
            /// </summary>
            Random,
        }

        #region SprayEmitter fields

        /// <summary>
        /// Radius of emission circle.
        /// </summary>
        [TypeParameter]
        float radius;

        /// <summary>
        /// Half width of emission sector, in radians.
        /// </summary>
        /// Setting spray angle to pi (3.14159...) will spray particles
        /// in a full circle.
        [TypeParameter]
        float sprayAngle;

        /// <summary>
        /// Type of particle facing at emission.
        /// </summary>
        [TypeParameter]
        FacingType facingType;

        /// <summary>
        /// Initial magnitude of particle velocity, in meters per second.
        /// </summary>
        /// The 'age' argument of this peng parameter will always be set to zero.
        /// The direction of particle velocity will be away from the emission center.
        [TypeParameter, ShallowCopy]
        PengParameter initialVelocity;

        /// <summary>
        /// Emission frequency, in number of particles per second.
        /// </summary>
        [TypeParameter]
        float emissionFrequency;

        /// <summary>
        /// Number of particles to create, or negative for no limit.
        /// </summary>
        [TypeParameter, RuntimeState]
        int numberToCreate;

        /// <summary>
        /// Time of next particle birth, in game time.
        /// </summary>
        TimeSpan _nextBirth;

        int _numberCreated;

        #endregion SprayEmitter fields

        public override bool Paused
        {
            set
            {
                if (Paused && !value)
                {
                    // Forget about creating particles whose creation was due 
                    // while we were paused.
                    if (_nextBirth < Peng.Arena.TotalTime)
                        _nextBirth = Peng.Arena.TotalTime;
                }
                base.Paused = value;
            }
        }

        public override bool Finished { get { return numberToCreate > 0 && _numberCreated >= numberToCreate; } }

        /// This constructor is for serialisation.
        public SprayEmitter()
        {
            radius = 15;
            sprayAngle = MathHelper.PiOver4;
            facingType = FacingType.Random;
            initialVelocity = new CurveLerp();
            emissionFrequency = 10;
            numberToCreate = -1;
            _nextBirth = new TimeSpan(-1);
        }

        /// <summary>
        /// Returns created particles, adds created gobs to <c>DataEngine</c>.
        /// </summary>
        /// <returns>Created particles, or <c>null</c> if no particles were created.</returns>
        public override IEnumerable<Particle> Emit()
        {
            if (paused) return null;
            if (Finished) return null;
            List<Particle> particles = null;

            // Initialise 'nextBirth'.
            if (_nextBirth.Ticks < 0)
                _nextBirth = Peng.Arena.TotalTime;

            // Count how many to create.
            int createCount = Math.Max(0, (int)(1 + emissionFrequency * (Peng.Arena.TotalTime - _nextBirth).TotalSeconds));
            if (numberToCreate >= 0)
            {
                createCount = Math.Min(createCount, numberToCreate);
                _numberCreated += createCount;
            }
            _nextBirth += TimeSpan.FromSeconds(createCount / emissionFrequency);

            if (createCount > 0 && textureNames.Length > 0)
                particles = new List<Particle>();

            // Create the particles. They are created 
            // with an even distribution over the circle sector
            // defined by 'radius', the origin and 'sprayAngle'.

            Vector2 startPos = Peng.OldPos;
            Vector2 endPos = Peng.Pos;
            for (int i = 0; i < createCount; ++i)
            {
                // Find out type of emitted thing (which gob or particle) and create it.
                int emitType = RandomHelper.GetRandomInt(textureNames.Length + gobTypeNames.Length);

                // The emitted thing init routine must be an Action<Gob>
                // so that it can be passed to Gob.CreateGob. Particle init
                // is included in the same routine because of large similarities.
                Action<Gob> emittedThingInit = gob => GobCreation(gob, createCount, i, startPos, endPos, emitType, ref particles);
                if (emitType < textureNames.Length)
                    emittedThingInit(null);
                else
                    Gob.CreateGob(gobTypeNames[emitType - textureNames.Length], emittedThingInit);
            }
            return particles;
        }

        public override void Reset()
        {
            _numberCreated = 0;
        }

        #region IConsistencyCheckable Members

        /// <summary>
        /// Makes the instance consistent in respect of fields marked with a
        /// limitation attribute.
        /// </summary>
        /// <param name="limitationAttribute">Check only fields marked with 
        /// this limitation attribute.</param>
        /// <see cref="Serialization"/>
        public void MakeConsistent(Type limitationAttribute)
        {
            if (limitationAttribute == typeof(TypeParameterAttribute))
            {
                // Make sure there's no null references.
                if (initialVelocity == null)
                    throw new Exception("Serialization error: SprayEmitter initialVelocity not defined");

                if (emissionFrequency <= 0 || emissionFrequency > 100000)
                {
                    Log.Write("Correcting insane emission frequency " + emissionFrequency);
                    emissionFrequency = MathHelper.Clamp(emissionFrequency, 1, 100000);
                }
            }
            _nextBirth = new TimeSpan(-1);
        }

        #endregion

        private void GobCreation(Gob gob, int createCount, int i, Vector2 startPos, Vector2 endPos, int emitType, ref List<Particle> particles)
        {
            // Find out emission parameters.
            // We have to loop because some choices of parameters may not be wanted.
            int maxAttempts = 20;
            bool attemptOk = false;
            for (int attempt = 0; !attemptOk && attempt < maxAttempts; ++attempt)
            {
                bool lastAttempt = attempt == maxAttempts - 1;
                attemptOk = true;
                float pengInput = Peng.Input;
                int random = RandomHelper.GetRandomInt();
                float directionAngle, rotation;
                Vector2 directionUnit, pos, move;
                switch (Peng.ParticleCoordinates)
                {
                    case Peng.CoordinateSystem.Peng:
                        RandomHelper.GetRandomCirclePoint(radius, -sprayAngle, sprayAngle,
                            out pos, out directionUnit, out directionAngle);
                        move = initialVelocity.GetValue(0, pengInput, random) * directionUnit;
                        switch (facingType)
                        {
                            case FacingType.Directed: rotation = 0; break;
                            case FacingType.Forward: rotation = directionAngle; break;
                            case FacingType.Random: rotation = RandomHelper.GetRandomFloat(0, MathHelper.TwoPi); break;
                            default: throw new Exception("SprayEmitter: Unhandled particle facing type " + facingType);
                        }
                        break;
                    case Peng.CoordinateSystem.Game:
                        {
                            float posWeight = (i + 1) / (float)createCount;
                            Vector2 iPos = Vector2.Lerp(startPos, endPos, posWeight);
                            RandomHelper.GetRandomCirclePoint(radius, Peng.Rotation - sprayAngle, Peng.Rotation + sprayAngle,
                                out pos, out directionUnit, out directionAngle);
                            pos += iPos;
                            move = Peng.Move + initialVelocity.GetValue(0, pengInput, random) * directionUnit;

                            // HACK: 'move' will be added to 'pos' in PhysicalUpdater during this same frame
                            pos -= AssaultWing.Instance.PhysicsEngine.ApplyChange(move, AssaultWing.Instance.GameTime.ElapsedGameTime);

                            switch (facingType)
                            {
                                case FacingType.Directed: rotation = Peng.Rotation; break;
                                case FacingType.Forward: rotation = directionAngle; break;
                                case FacingType.Random: rotation = RandomHelper.GetRandomFloat(0, MathHelper.TwoPi); break;
                                default: throw new Exception("SprayEmitter: Unhandled particle facing type " + facingType);
                            }
                        }
                        break;
                    default:
                        throw new Exception("SprayEmitter: Unhandled peng coordinate system " + Peng.ParticleCoordinates);
                }

                // Set the thing's parameters.
                if (emitType < textureNames.Length)
                {
                    var particle = new Particle();
                    particle.alpha = 1;
                    particle.birthTime = Peng.Arena.TotalTime;
                    particle.move = move;
                    particle.pengInput = pengInput;
                    particle.pos = pos;
                    particle.random = random;
                    particle.direction = directionAngle;
                    particle.rotation = rotation;
                    particle.scale = 1;
                    particle.textureIndex = emitType;
                    particles.Add(particle);
                }
                else
                {
                    // Bail out if the position is not free for the gob.
                    if (!lastAttempt && !Peng.Arena.IsFreePosition(gob, pos))
                    {
                        attemptOk = false;
                        continue;
                    }
                    gob.Owner = Peng.Owner;
                    gob.ResetPos(pos, move, rotation);
                    Peng.Arena.Gobs.Add(gob);
                }
            }
        }
    }
}
