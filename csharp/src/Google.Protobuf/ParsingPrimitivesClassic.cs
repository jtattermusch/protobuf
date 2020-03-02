#region Copyright notice and license
// Protocol Buffers - Google's data interchange format
// Copyright 2008 Google Inc.  All rights reserved.
// https://developers.google.com/protocol-buffers/
//
// Redistribution and use in source and binary forms, with or without
// modification, are permitted provided that the following conditions are
// met:
//
//     * Redistributions of source code must retain the above copyright
// notice, this list of conditions and the following disclaimer.
//     * Redistributions in binary form must reproduce the above
// copyright notice, this list of conditions and the following disclaimer
// in the documentation and/or other materials provided with the
// distribution.
//     * Neither the name of Google Inc. nor the names of its
// contributors may be used to endorse or promote products derived from
// this software without specific prior written permission.
//
// THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS
// "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT
// LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR
// A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT
// OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL,
// SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT
// LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE,
// DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY
// THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
// (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
// OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
#endregion

using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using Google.Protobuf.Collections;

namespace Google.Protobuf
{
    /// <summary>
    /// Fast parsing primitives
    /// </summary>
    internal static class ParsingPrimitivesClassic
    {

        // TODO: read basic types

        // TODO: move zigzag decode methods

        public static int ParseLength(ref ReadOnlySpan<byte> buffer, ref ParserInternalState state)
        {
            return (int)ParseRawVarint32(ref buffer, ref state);
        }

        public static ulong ParseRawVarint64(ref ReadOnlySpan<byte> buffer, ref ParserInternalState state)
        {
            if (state.bufferPos + 10 > state.bufferSize)
            {
                return ParseRawVarint64SlowPath(ref buffer, ref state);
            }

            ulong result = buffer[state.bufferPos++];
            if (result < 128)
            {
                return result;
            }
            result &= 0x7f;
            int shift = 7;
            do
            {
                byte b = buffer[state.bufferPos++];
                result |= (ulong)(b & 0x7F) << shift;
                if (b < 0x80)
                {
                    return result;
                }
                shift += 7;
            }
            while (shift < 64);

            throw InvalidProtocolBufferException.MalformedVarint();
        }

        private static ulong ParseRawVarint64SlowPath(ref ReadOnlySpan<byte> buffer, ref ParserInternalState state)
        {
            int shift = 0;
            ulong result = 0;
            do
            {
                byte b = ReadRawByte(ref buffer, ref state);
                result |= (ulong)(b & 0x7F) << shift;
                if (b < 0x80)
                {
                    return result;
                }
                shift += 7;
            }
            while (shift < 64);

            throw InvalidProtocolBufferException.MalformedVarint();
        }

        public static uint ParseRawVarint32(ref ReadOnlySpan<byte> buffer, ref ParserInternalState state)
        {
            if (state.bufferPos + 5 > state.bufferSize)
            {
                return ReadRawVarint32SlowPath(ref buffer, ref state);
            }

            int tmp = buffer[state.bufferPos++];
            if (tmp < 128)
            {
                return (uint)tmp;
            }
            int result = tmp & 0x7f;
            if ((tmp = buffer[state.bufferPos++]) < 128)
            {
                result |= tmp << 7;
            }
            else
            {
                result |= (tmp & 0x7f) << 7;
                if ((tmp = buffer[state.bufferPos++]) < 128)
                {
                    result |= tmp << 14;
                }
                else
                {
                    result |= (tmp & 0x7f) << 14;
                    if ((tmp = buffer[state.bufferPos++]) < 128)
                    {
                        result |= tmp << 21;
                    }
                    else
                    {
                        result |= (tmp & 0x7f) << 21;
                        result |= (tmp = buffer[state.bufferPos++]) << 28;
                        if (tmp >= 128)
                        {
                            // Discard upper 32 bits.
                            // Note that this has to use ReadRawByte() as we only ensure we've
                            // got at least 5 bytes at the start of the method. This lets us
                            // use the fast path in more cases, and we rarely hit this section of code.
                            for (int i = 0; i < 5; i++)
                            {
                                if (ReadRawByte(ref buffer, ref state) < 128)
                                {
                                    return (uint) result;
                                }
                            }
                            throw InvalidProtocolBufferException.MalformedVarint();
                        }
                    }
                }
            }
            return (uint)result;
        }

        private static uint ReadRawVarint32SlowPath(ref ReadOnlySpan<byte> buffer, ref ParserInternalState state)
        {
            int tmp = ReadRawByte(ref buffer, ref state);
            if (tmp < 128)
            {
                return (uint) tmp;
            }
            int result = tmp & 0x7f;
            if ((tmp = ReadRawByte(ref buffer, ref state)) < 128)
            {
                result |= tmp << 7;
            }
            else
            {
                result |= (tmp & 0x7f) << 7;
                if ((tmp = ReadRawByte(ref buffer, ref state)) < 128)
                {
                    result |= tmp << 14;
                }
                else
                {
                    result |= (tmp & 0x7f) << 14;
                    if ((tmp = ReadRawByte(ref buffer, ref state)) < 128)
                    {
                        result |= tmp << 21;
                    }
                    else
                    {
                        result |= (tmp & 0x7f) << 21;
                        result |= (tmp = ReadRawByte(ref buffer, ref state)) << 28;
                        if (tmp >= 128)
                        {
                            // Discard upper 32 bits.
                            for (int i = 0; i < 5; i++)
                            {
                                if (ReadRawByte(ref buffer, ref state) < 128)
                                {
                                    return (uint) result;
                                }
                            }
                            throw InvalidProtocolBufferException.MalformedVarint();
                        }
                    }
                }
            }
            return (uint) result;
        }

        public static uint ParseRawLittleEndian32(ref ReadOnlySpan<byte> buffer, ref ParserInternalState state)
        {
            const int length = sizeof(uint);
            if (state.bufferPos + length > state.bufferSize)
            {
                return ParseRawLittleEndian32SlowPath(ref buffer, ref state);
            }
            uint result = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(state.bufferPos, length));
            state.bufferPos += length;
            return result;
        }

        private static uint ParseRawLittleEndian32SlowPath(ref ReadOnlySpan<byte> buffer, ref ParserInternalState state)
        {
            uint b1 = ReadRawByte(ref buffer, ref state);
            uint b2 = ReadRawByte(ref buffer, ref state);
            uint b3 = ReadRawByte(ref buffer, ref state);
            uint b4 = ReadRawByte(ref buffer, ref state);
            return b1 | (b2 << 8) | (b3 << 16) | (b4 << 24);
        }

        public static ulong ParseRawLittleEndian64(ref ReadOnlySpan<byte> buffer, ref ParserInternalState state)
        {
            const int length = 8;
            if (state.bufferPos + length > state.bufferSize)
            {
                return ParseRawLittleEndian64SlowPath(ref buffer, ref state);
            }
            ulong result = BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(state.bufferPos, length));
            state.bufferPos += length;
            return result;
        }

        private static ulong ParseRawLittleEndian64SlowPath(ref ReadOnlySpan<byte> buffer, ref ParserInternalState state)
        {
            ulong b1 = ReadRawByte(ref buffer, ref state);
            ulong b2 = ReadRawByte(ref buffer, ref state);
            ulong b3 = ReadRawByte(ref buffer, ref state);
            ulong b4 = ReadRawByte(ref buffer, ref state);
            ulong b5 = ReadRawByte(ref buffer, ref state);
            ulong b6 = ReadRawByte(ref buffer, ref state);
            ulong b7 = ReadRawByte(ref buffer, ref state);
            ulong b8 = ReadRawByte(ref buffer, ref state);
            return b1 | (b2 << 8) | (b3 << 16) | (b4 << 24)
                    | (b5 << 32) | (b6 << 40) | (b7 << 48) | (b8 << 56);
        }

        public static double ParseDouble(ref ReadOnlySpan<byte> buffer, ref ParserInternalState state)
        {
            // TODO(jtattermusch): how fast is Int64BitsToDouble?
            return BitConverter.Int64BitsToDouble((long)ParseRawLittleEndian64(ref buffer, ref state));
        }

        public static float ParseFloat(ref ReadOnlySpan<byte> buffer, ref ParserInternalState state)
        {
            const int length = sizeof(float);
            if (!BitConverter.IsLittleEndian || state.bufferPos + length > state.bufferSize)
            {
                return ParseFloatSlow(ref buffer, ref state);
            }
            // ReadUnaligned uses processor architecture for endianness.
            float result = Unsafe.ReadUnaligned<float>(ref MemoryMarshal.GetReference(buffer.Slice(state.bufferPos, length)));
            state.bufferPos += length;
            return result;  
        }

        private static unsafe float ParseFloatSlow(ref ReadOnlySpan<byte> buffer, ref ParserInternalState state)
        {
            const int length = sizeof(float);
            byte* stackBuffer = stackalloc byte[length];
            Span<byte> tempSpan = new Span<byte>(stackBuffer, length);
            for (int i = 0; i < length; i++)
            {
                tempSpan[i] = ReadRawByte(ref buffer, ref state);
            }

            // Content is little endian. Reverse if needed to match endianness of architecture.
            if (!BitConverter.IsLittleEndian)
            {
                tempSpan.Reverse();
            }
            return Unsafe.ReadUnaligned<float>(ref MemoryMarshal.GetReference(tempSpan));
        }

        // TODO: move to different helper class
        public static byte[] ReadRawBytes(ref ReadOnlySpan<byte> buffer, ref ParserInternalState state, int size)
        {
            if (size < 0)
            {
                throw InvalidProtocolBufferException.NegativeSize();
            }

            if (state.totalBytesRetired + state.bufferPos + size > state.currentLimit)
            {
                // Read to the end of the stream (up to the current limit) anyway.
                SkipRawBytes(ref buffer, ref state, state.currentLimit - state.totalBytesRetired - state.bufferPos);
                // Then fail.
                throw InvalidProtocolBufferException.TruncatedMessage();
            }

            if (size <= state.bufferSize - state.bufferPos)
            {
                // We have all the bytes we need already.
                byte[] bytes = new byte[size];
                buffer.Slice(state.bufferPos, size).CopyTo(bytes);
                state.bufferPos += size;
                return bytes;
            }
            else //if (size < buffer.Length)
            {
                // TODO: fix security problem!!
                // TODO: use this whenever there's known size of data and we check it's available
                // TODO>>..........

                // Reading more bytes than are in the buffer, but not an excessive number
                // of bytes.  We can safely allocate the resulting array ahead of time.

                // First copy what we have.
                byte[] bytes = new byte[size];
                var bytesSpan = new Span<byte>(bytes);
                int pos = state.bufferSize - state.bufferPos;
                buffer.Slice(state.bufferPos, pos).CopyTo(bytesSpan.Slice(0, pos));
                state.bufferPos = state.bufferSize;

                // We want to use RefillBuffer() and then copy from the buffer into our
                // byte array rather than reading directly into our byte array because
                // the input may be unbuffered.
                state.refillBufferHelper.RefillBuffer(ref buffer, ref state, true);

                while (size - pos > state.bufferSize)
                {
                    buffer.Slice(0, state.bufferSize)
                        .CopyTo(bytesSpan.Slice(pos, state.bufferSize));
                    pos += state.bufferSize;
                    state.bufferPos = state.bufferSize;
                    state.refillBufferHelper.RefillBuffer(ref buffer, ref state, true);
                }

                buffer.Slice(0, size - pos)
                        .CopyTo(bytesSpan.Slice(pos, size - pos));
                state.bufferPos = size - pos;

                return bytes;
            }
            // else
            // {
            //     // The size is very large.  For security reasons, we can't allocate the
            //     // entire byte array yet.  The size comes directly from the input, so a
            //     // maliciously-crafted message could provide a bogus very large size in
            //     // order to trick the app into allocating a lot of memory.  We avoid this
            //     // by allocating and reading only a small chunk at a time, so that the
            //     // malicious message must actually *be* extremely large to cause
            //     // problems.  Meanwhile, we limit the allowed size of a message elsewhere.

            //     // Remember the buffer markers since we'll have to copy the bytes out of
            //     // it later.
            //     int originalBufferPos = state.bufferPos;
            //     int originalBufferSize = state.bufferSize;

            //     // Mark the current buffer consumed.
            //     state.totalBytesRetired += state.bufferSize;
            //     state.bufferPos = 0;
            //     state.bufferSize = 0;

            //     // Read all the rest of the bytes we need.
            //     int sizeLeft = size - (originalBufferSize - originalBufferPos);
            //     List<byte[]> chunks = new List<byte[]>();

            //     while (sizeLeft > 0)
            //     {
            //         byte[] chunk = new byte[Math.Min(sizeLeft, buffer.Length)];
            //         int pos = 0;
            //         while (pos < chunk.Length)
            //         {
            //             int n = (input == null) ? -1 : input.Read(chunk, pos, chunk.Length - pos);
            //             if (n <= 0)
            //             {
            //                 throw InvalidProtocolBufferException.TruncatedMessage();
            //             }
            //             state.totalBytesRetired += n;
            //             pos += n;
            //         }
            //         sizeLeft -= chunk.Length;
            //         chunks.Add(chunk);
            //     }

            //     // OK, got everything.  Now concatenate it all into one buffer.
            //     byte[] bytes = new byte[size];

            //     // Start by copying the leftover bytes from this.buffer.
            //     int newPos = originalBufferSize - originalBufferPos;
            //     ByteArray.Copy(buffer, originalBufferPos, bytes, 0, newPos);

            //     // And now all the chunks.
            //     foreach (byte[] chunk in chunks)
            //     {
            //         Buffer.BlockCopy(chunk, 0, bytes, newPos, chunk.Length);
            //         newPos += chunk.Length;
            //     }

            //     // Done.
            //     return bytes;
            // }
        }

        public static void SkipRawBytes(ref ReadOnlySpan<byte> buffer, ref ParserInternalState state, int size)
        {
            if (size < 0)
            {
                throw InvalidProtocolBufferException.NegativeSize();
            }

            if (state.totalBytesRetired + state.bufferPos + size > state.currentLimit)
            {
                // Read to the end of the stream anyway.
                SkipRawBytes(ref buffer, ref state, state.currentLimit - state.totalBytesRetired - state.bufferPos);
                // Then fail.
                throw InvalidProtocolBufferException.TruncatedMessage();
            }

            if (size <= state.bufferSize - state.bufferPos)
            {
                // We have all the bytes we need already.
                state.bufferPos += size;
            }
            else
            {
                // TODO: do we need to support skipping in seekable Streams?

                // Skipping more bytes than are in the buffer.  First skip what we have.
                int pos = state.bufferSize - state.bufferPos;
                state.bufferPos = state.bufferSize;

                state.refillBufferHelper.RefillBuffer(ref buffer, ref state, true);

                while (size - pos > state.bufferSize)
                {
                    pos += state.bufferSize;
                    state.bufferPos = state.bufferSize;
                    state.refillBufferHelper.RefillBuffer(ref buffer, ref state, true);
                }

                state.bufferPos = size - pos;
            }
        }

        public static string ReadRawString(ref ReadOnlySpan<byte> buffer, ref ParserInternalState state, int length)
        {
            // No need to read any data for an empty string.
            if (length == 0)
            {
                return string.Empty;
            }

            if (length < 0)
            {
                throw InvalidProtocolBufferException.NegativeSize();
            }

            // TODO: support fast string parsing
            // with GOOGLE_PROTOBUF_SUPPORT_FAST_STRING

            //if (length <= state.bufferSize - state.bufferPos && length > 0)
            // {
            //     // Fast path:  We already have the bytes in a contiguous buffer, so
            //     //   just copy directly from it.
            //     String result = CodedOutputStream.Utf8Encoding.GetString(buffer, state.bufferPos, length);
            //     state.bufferPos += length;
            //     return result;
            // }

            // Slow path: Build a byte array first then copy it.
            return CodedOutputStream.Utf8Encoding.GetString(ReadRawBytes(ref buffer, ref state, length), 0, length);
        }
        
        private static byte ReadRawByte(ref ReadOnlySpan<byte> buffer, ref ParserInternalState state)
        {
            if (state.bufferPos == state.bufferSize)
            {
                state.refillBufferHelper.RefillBuffer(ref buffer, ref state, true);
            }
            return buffer[state.bufferPos++];
        }

        public static uint ReadRawVarint32(Stream input)
        {
            int result = 0;
            int offset = 0;
            for (; offset < 32; offset += 7)
            {
                int b = input.ReadByte();
                if (b == -1)
                {
                    throw InvalidProtocolBufferException.TruncatedMessage();
                }
                result |= (b & 0x7f) << offset;
                if ((b & 0x80) == 0)
                {
                    return (uint) result;
                }
            }
            // Keep reading up to 64 bits.
            for (; offset < 64; offset += 7)
            {
                int b = input.ReadByte();
                if (b == -1)
                {
                    throw InvalidProtocolBufferException.TruncatedMessage();
                }
                if ((b & 0x80) == 0)
                {
                    return (uint) result;
                }
            }
            throw InvalidProtocolBufferException.MalformedVarint();
        } 

        
    }
}