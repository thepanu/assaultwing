﻿using System;

namespace AW2.Net
{
    /// <summary>
    /// Specifies which parts of an entity to serialise over a network.
    /// </summary>
    [Flags]
    public enum SerializationModeFlags
    {
        /// <summary>
        /// Serialise nothing.
        /// </summary>
        None = 0x00,

        /// <summary>
        /// Serialise data that is constant after initialisation.
        /// </summary>
        ConstantData = 0x01,

        /// <summary>
        /// Serialise data that varies even after initialisation.
        /// </summary>
        VaryingData = 0x02,

        /// <summary>
        /// Serialise all data.
        /// </summary>
        All = ConstantData | VaryingData,
    }
}
