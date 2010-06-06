using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using AW2.Core;
using AW2.Helpers;

namespace AW2.Game.Pengs
{
    /// <summary>
    /// A particle management parameter that consists of many <c>Curve</c>s, each
    /// associated with a value of external peng input (between 0 and 1). The value
    /// of <c>CurveLerp</c> for arguments <c>(age, input, random)</c> is linearly interpolated 
    /// between the <c>Curve</c>s closest to <c>input</c>.
    /// Particle random acts as a constant shift in the function's value,
    /// and the amplitude of the shift can be determined.
    /// All values are clamped to set minimum and maximum values.
    /// 
    /// It is common that several <c>CurveLerp</c>s are evaluated with the same random
    /// value, and it would be desirable to have the <c>CurveLerp</c>s have different
    /// kinds of random despite the same random number. For example, it is not desirable
    /// that when a particle is randomised to have a large scale, it will also have
    /// a large alpha value. To avoid such a thing, <c>CurveLerp</c> has a 'random mixer'
    /// value which uses the random value as a seed in a random sequence of its own,
    /// and picks the n'th value in this sequence.
    /// </summary>
    public class CurveLerp : PengParameter
    {
        /// <summary>
        /// The least possible value of the parameter. 
        /// </summary>
        private float _min;

        /// <summary>
        /// The greatest possible value of the parameter. 
        /// </summary>
        private float _max;

        /// <summary>
        /// The maximum shift a particle's random seed can cause in the
        /// value of the parameter.
        /// </summary>
        private float _randomAmplitude;

        /// <summary>
        /// Mixing value of particle random values.
        /// </summary>
        private int _randomMixer;

        /// <summary>
        /// The curve lerp's keys.
        /// </summary>
        private CurveLerpKeyCollection _keys;

        /// <summary>
        /// This constructor is for serialisation only.
        /// </summary>
        public CurveLerp()
        {
            _min = 0;
            _max = 1;
            _randomAmplitude = 0.3f;
            _randomMixer = 1234;
            _keys = new CurveLerpKeyCollection();
            Curve curve0 = new Curve();
            curve0.Keys.Add(new CurveKey(0, 0.3f));
            curve0.Keys.Add(new CurveKey(1, 0.6f));
            _keys.Add(new CurveLerpKey(0, curve0));
            Curve curve1 = new Curve();
            curve1.Keys.Add(new CurveKey(0, 0.4f));
            curve1.Keys.Add(new CurveKey(1, 0.7f));
            _keys.Add(new CurveLerpKey(1, curve1));
        }

        public float GetValue(float age, float input, int random)
        {
            float value = float.NaN;

            // Find key or keys that are next to 'input'.
            if (input <= _keys[0].Input)
            {
                // Use the curve of least specified 'input'.
                value = _keys[0].Curve.Evaluate(age);
            }
            else if (input >= _keys[_keys.Count-1].Input)
            {
                // Use the curve of the greatest specified 'input'.
                value = _keys[_keys.Count-1].Curve.Evaluate(age);
            }
            else 
            {
                // Linear search -- 'keys' are assumed to be a small collection.
                int i = 0;
                while (_keys[i + 1].Input < input) ++i;

                // 'input' is between keys at i and i+1.
                // Linearly interpolate between them.
                float weight2 = (input - _keys[i].Input) / (_keys[i+1].Input - _keys[i].Input);
                value = MathHelper.Lerp(_keys[i].Curve.Evaluate(age), _keys[i + 1].Curve.Evaluate(age), weight2);
            }

            // Add random and clamp to limits.
            int ourRandom = RandomHelper.MixRandomInt(random, _randomMixer);
            return MathHelper.Clamp(value + _randomAmplitude * (ourRandom / (float)int.MaxValue), _min, _max);
        }
    }
}