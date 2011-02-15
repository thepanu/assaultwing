using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using AW2.Helpers.Serialization;

namespace AW2.Helpers.Geometric
{
    /// <summary>
    /// The whole two-dimensional space.
    /// </summary>
    public class Everything : IGeomPrimitive
    {
        #region IGeomPrimitive Members

        /// <summary>
        /// A rectangle that contains the geometric primitive.
        /// </summary>
        public Rectangle BoundingBox
        {
            get
            {
                return new Rectangle(float.MinValue, float.MinValue, 
                                     float.MaxValue, float.MaxValue);
            }
        }

        /// <summary>
        /// Transforms the geometric primitive by a transformation matrix.
        /// </summary>
        /// <param name="transformation">The transformation matrix.</param>
        /// <returns>The transformed geometric primitive.</returns>
        public IGeomPrimitive Transform(Matrix transformation)
        {
            // It's not a _copy_ but it shouldn't matter because this
            // object has no state.
            return this;
        }

        /// <summary>
        /// Returns the shortest distance between the geometric primitive
        /// and a point.
        /// </summary>
        /// <param name="point">The point.</param>
        /// <returns>The shortest distance between the geometric primitive
        /// and the point.</returns>
        public float DistanceTo(Vector2 point)
        {
            return 0;
        }

        #endregion

        #region INetworkSerializable Members

        /// <summary>
        /// Serialises the object to a binary writer.
        /// </summary>
        public void Serialize(NetworkBinaryWriter writer, SerializationModeFlags mode) { }

        /// <summary>
        /// Deserialises the object from a binary writer.
        /// </summary>
        public void Deserialize(NetworkBinaryReader reader, SerializationModeFlags mode, int framesAgo) { }

        #endregion
    }
}