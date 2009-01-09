﻿#if DEBUG
using NUnit.Framework;
#endif
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Net;

namespace AW2.Net
{
    /// <summary>
    /// Writes primitive types in binary to a network stream 
    /// and supports writing strings in a specific encoding.
    /// Specially crafted for <c>NetworkEngine</c> and related
    /// classes. Takes care of byte order.
    /// </summary>
    /// <seealso>AW2.Net.Message</seealso>
    public class NetworkBinaryWriter : BinaryWriter
    {
        /// <summary>
        /// Creates a new network binary writer that writes to an output stream.
        /// </summary>
        /// <param name="output">The stream to write to.</param>
        public NetworkBinaryWriter(Stream output)
            : base(output, Encoding.UTF8)
        {
        }

        /// <summary>
        /// Writes an unsigned short.
        /// </summary>
        /// <param name="value">The value to write.</param>
        public override void Write(ushort value)
        {
            base.Write(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(unchecked((short)value))), 0, 2);
        }

        /// <summary>
        /// Writes an int.
        /// </summary>
        /// <param name="value">The value to write.</param>
        public override void Write(int value)
        {
            base.Write(BitConverter.GetBytes(IPAddress.HostToNetworkOrder(value)), 0, 4);
        }

        /// <summary>
        /// Writes a float.
        /// </summary>
        /// <param name="value">The value to write.</param>
        public override void Write(float value)
        {
            Write((int)BitConverter.ToInt32(BitConverter.GetBytes(value), 0));
        }

        /// <summary>
        /// Writes a length-prefixed string.
        /// </summary>
        /// <param name="value">The string to write.</param>
        public override void Write(string value)
        {
            throw new InvalidOperationException("Writing length-prefixed strings is not advisable.");
        }

        /// <summary>
        /// Writes a given number of 
        /// bytes containing a string and a trailing sequence of one or more zero bytes.
        /// The string is truncated to fit the byte count, and an optional exception is 
        /// thrown if this happens. The string will be written in UTF-8 encoding. 
        /// </summary>
        /// <param name="value">The string to write.</param>
        /// <param name="byteCount">The exact number of bytes to write, including the
        /// trailing zero.</param>
        /// <param name="throwOnTruncate">If <c>true</c> then an exception will be
        /// thrown if the string is too long to fit the given number of bytes.</param>
        public void Write(string value, int byteCount, bool throwOnTruncate)
        {
            if (byteCount < 1)
                throw new ArgumentException("Need at least one byte to write a string with a trailing zero");
            Encoding encoding = Encoding.UTF8;
            int bytesNeeded = encoding.GetByteCount(value);
            if (bytesNeeded + 1 > byteCount)
            {
                if (throwOnTruncate)
                    throw new ArgumentException("String too long (" + (bytesNeeded + 1) + ") to fit given byte count (" + byteCount + ")");

                // Binary search for the maximum number of chars that fit.
                char[] valueChars = value.ToCharArray();
                int goodCharCount = 0, badCharCount = valueChars.Length;
                bytesNeeded = 0;
                while (badCharCount - goodCharCount > 1)
                {
                    int charCount = (goodCharCount + badCharCount) / 2;
                    int bytesNeededNow = encoding.GetByteCount(valueChars, 0, charCount);
                    if (bytesNeededNow + 1 > byteCount)
                        badCharCount = charCount;
                    else
                    {
                        goodCharCount = charCount;
                        bytesNeeded = bytesNeededNow;
                    }
                }
                Write(encoding.GetBytes(valueChars, 0, goodCharCount), 0, bytesNeeded);
            }
            else
                Write(encoding.GetBytes(value), 0, bytesNeeded);

            // Pad with zero bytes.
            for (int i = bytesNeeded; i < byteCount; ++i)
                Write((byte)0);
        }

#if DEBUG
        #region Unit tests

        /// <summary>
        /// Tests for NetworkBinaryWriter.
        /// </summary>
        [TestFixture]
        public class NetworkBinaryWriterTest
        {
            /// <summary>
            /// Tests correctness of byte order in writing.
            /// </summary>
            [Test]
            public void TestByteOrderInt32()
            {
                int[] values = { 0x01020304, -0x01020304, -0x7ffdfbf7, 0x7ffdfbf7 };
                foreach (int value in values)
                {
                    NetworkBinaryWriter writer = new NetworkBinaryWriter(new MemoryStream());
                    writer.Write(value);
                    byte[] data = ((MemoryStream)writer.BaseStream).ToArray();
                    if (BitConverter.IsLittleEndian)
                        Array.Reverse(data);
                    Assert.AreEqual(value, BitConverter.ToInt32(data, 0));
                }
            }

            /// <summary>
            /// Tests correctness of byte order in writing.
            /// </summary>
            [Test]
            public void TestByteOrderSingle()
            {
                float[] values = { 1234.5678f, -1234.5678f, 987e20f, -987e20f, 123e-20f, -123e-20f, float.NaN, float.PositiveInfinity };
                foreach (float value in values)
                {
                    NetworkBinaryWriter writer = new NetworkBinaryWriter(new MemoryStream());
                    writer.Write(value);
                    byte[] data = ((MemoryStream)writer.BaseStream).ToArray();
                    if (BitConverter.IsLittleEndian)
                        Array.Reverse(data);
                    Assert.AreEqual(value, BitConverter.ToSingle(data, 0));
                }
            }

            /// <summary>
            /// Tests correctness of byte order in writing.
            /// </summary>
            [Test]
            public void TestByteOrderUint16()
            {
                ushort[] values = { 0x0102, 0xfffd };
                foreach (ushort value in values)
                {
                    NetworkBinaryWriter writer = new NetworkBinaryWriter(new MemoryStream());
                    writer.Write(value);
                    byte[] data = ((MemoryStream)writer.BaseStream).ToArray();
                    if (BitConverter.IsLittleEndian)
                        Array.Reverse(data);
                    Assert.AreEqual(value, BitConverter.ToUInt16(data, 0));
                }
            }
        }

        #endregion
#endif
    }
}
