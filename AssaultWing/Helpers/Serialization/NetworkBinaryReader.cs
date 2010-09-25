﻿#if DEBUG
using NUnit.Framework;
#endif
using System;
using System.Linq;
using System.Text;
using System.IO;
using System.Net;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace AW2.Helpers.Serialization
{
    /// <summary>
    /// Reads primitive types in binary from a network stream 
    /// and supports reading strings in a specific encoding.
    /// Takes care of byte order.
    /// </summary>
    public class NetworkBinaryReader : BinaryReader
    {
        static char[] nullCharArray = new char[] { '\0' };

        /// <summary>
        /// Creates a new network binary reader that writes to an output stream.
        /// </summary>
        /// <param name="input">The stream to read from.</param>
        public NetworkBinaryReader(Stream input)
            : base(input, Encoding.UTF8)
        {
        }

        /// <summary>
        /// Reads an unsigned short.
        /// </summary>
        /// <returns>The read value.</returns>
        public override ushort ReadUInt16()
        {
            return unchecked((ushort)IPAddress.NetworkToHostOrder(base.ReadInt16()));
        }

        /// <summary>
        /// Reads an int.
        /// </summary>
        /// <returns>The read value.</returns>
        public override int ReadInt32()
        {
            return IPAddress.NetworkToHostOrder(base.ReadInt32());
        }

        /// <summary>
        /// Reads a float.
        /// </summary>
        /// <returns>The read value.</returns>
        public override float ReadSingle()
        {
            return Converter.IntToFloat(ReadInt32());
        }

        /// <summary>
        /// Reads a 16-bit floating point value.
        /// </summary>
        /// <returns>The read value.</returns>
        public Half ReadHalf()
        {
            short bits = ReadInt16();
            return Converter.ShortToHalf(bits);
        }

        /// <summary>
        /// Reads a length-prefixed string.
        /// </summary>
        public override string ReadString()
        {
            int length = ReadInt32();
            var chars = ReadChars(length);
            return new string(chars);
        }

        /// <summary>
        /// Reads a given number of bytes containing a zero-terminated string.
        /// The string will be read in UTF-8 encoding. 
        /// </summary>
        /// The same number of bytes will be read regardless of the length of the string.
        /// <param name="byteCount">The number of bytes to read.</param>
        /// <returns>The string.</returns>
        public string ReadString(int byteCount)
        {
            byte[] bytes = base.ReadBytes(byteCount);
            return Encoding.UTF8.GetString(bytes).TrimEnd(nullCharArray);
        }

        /// <summary>
        /// Reads a Vector2 value.
        /// </summary>
        public Vector2 ReadVector2()
        {
            return new Vector2
            {
                X = ReadSingle(),
                Y = ReadSingle()
            };
        }

        /// <summary>
        /// Reads a Vector3 value.
        /// </summary>
        public Vector3 ReadVector3()
        {
            return new Vector3
            {
                X = ReadSingle(),
                Y = ReadSingle(),
                Z = ReadSingle()
            };
        }

        /// <summary>
        /// Reads a 3D model vertex.
        /// </summary>
        public VertexPositionNormalTexture ReadVertexPositionTextureNormal()
        {
            return new VertexPositionNormalTexture
            {
                Position = ReadVector3(),
                Normal = ReadVector3(),
                TextureCoordinate = ReadVector2()
            };
        }

        public TimeSpan ReadTimeSpan()
        {
            return new TimeSpan(ReadInt64());
        }

        public Color ReadColor()
        {
            return new Color { PackedValue = ReadUInt32() };
        }

        /// <summary>
        /// Reads a Vector2 value given in half precision.
        /// </summary>
        public Vector2 ReadHalfVector2()
        {
            return new Vector2
            {
                X = ReadHalf(),
                Y = ReadHalf()
            };
        }

        /// <summary>
        /// Reads a Vector3 value given in half precision.
        /// </summary>
        public Vector3 ReadHalfVector3()
        {
            return new Vector3
            {
                X = ReadHalf(),
                Y = ReadHalf(),
                Z = ReadHalf()
            };
        }

        /// <summary>
        /// Reads a 3D model vertex given in half precision.
        /// </summary>
        public VertexPositionNormalTexture ReadHalfVertexPositionTextureNormal()
        {
            return new VertexPositionNormalTexture
            {
                Position = ReadHalfVector3(),
                Normal = ReadHalfVector3(),
                TextureCoordinate = ReadHalfVector2()
            };
        }

#if DEBUG
        #region Unit tests

        /// <summary>
        /// Tests for NetworkBinaryReader.
        /// </summary>
        [TestFixture]
        public class NetworkBinaryReaderTest
        {
            /// <summary>
            /// Tests correctness of byte order in reading.
            /// </summary>
            [Test]
            public void TestByteOrderInt32()
            {
                byte[][] datas = { new byte[] { 1, 2, 3, 4 }, new byte[] { 255, 253, 251, 247 } };
                foreach (byte[] data in datas)
                {
                    NetworkBinaryReader reader = new NetworkBinaryReader(new MemoryStream(data));
                    int value = reader.ReadInt32();
                    if (BitConverter.IsLittleEndian)
                        Array.Reverse(data);
                    Assert.AreEqual(BitConverter.ToInt32(data, 0), value);
                }
            }

            /// <summary>
            /// Tests correctness of byte order in reading.
            /// </summary>
            [Test]
            public void TestByteOrderSingle()
            {
                byte[] data = { 1, 2, 3, 4 };
                NetworkBinaryReader reader = new NetworkBinaryReader(new MemoryStream(data));
                float value = reader.ReadSingle();
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(data);
                Assert.AreEqual(BitConverter.ToSingle(data, 0), value);
            }

            /// <summary>
            /// Tests correctness of byte order in reading.
            /// </summary>
            [Test]
            public void TestByteOrderUint16()
            {
                byte[][] datas = { new byte[] { 1, 2 }, new byte[] { 253, 255 } };
                foreach (byte[] data in datas)
                {
                    NetworkBinaryReader reader = new NetworkBinaryReader(new MemoryStream(data));
                    ushort value = reader.ReadUInt16();
                    if (BitConverter.IsLittleEndian)
                        Array.Reverse(data);
                    Assert.AreEqual(BitConverter.ToUInt16(data, 0), value);
                }
            }

            /// <summary>
            /// Tests correctness of half precision float writing and reading.
            /// </summary>
            [Test]
            public void TestHalf()
            {
                float[] data = {
                    0f, -0f, 1f, -1f, 65504f, -65504f, 0.000061035156f, -0.000061035156f, 12.3359375f, -12.3359375f,
                    float.NaN, float.PositiveInfinity, float.NegativeInfinity,
                };
                var stream = new MemoryStream();
                var writer = new NetworkBinaryWriter(stream);
                foreach (float value in data)
                    writer.Write((Half)value);
                writer.Flush();
                byte[] bytes = stream.GetBuffer();
                Assert.That(bytes.Any(x => x != 0), "Something wrong with memory stream usage?");
                stream = new MemoryStream(bytes);
                var reader = new NetworkBinaryReader(stream);
                float[] result = new float[data.Length];
                for (int i = 0; i < data.Length; ++i)
                    result[i] = reader.ReadHalf();
                Assert.AreEqual(data, result);
            }
        }

        #endregion
#endif
    }
}