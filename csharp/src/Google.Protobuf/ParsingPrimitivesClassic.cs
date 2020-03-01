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
        // TODO: readTag

        // TODO: readLength

        // TODO: read basic types

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

        public static unsafe float ParseFloatSlow(ref ReadOnlySpan<byte> buffer, ref ParserInternalState state)
        {
            const int length = sizeof(float);
            byte* stackBuffer = stackalloc byte[length];
            Span<byte> tempSpan = new Span<byte>(stackBuffer, length);
            for (int i = 0; i < length; i++)
            {
                tempSpan[length - i - 1] = ReadRawByte(ref buffer, ref state);
            }
            return Unsafe.ReadUnaligned<float>(ref MemoryMarshal.GetReference(tempSpan));
        }
        
        private static byte ReadRawByte(ref ReadOnlySpan<byte> buffer, ref ParserInternalState state)
        {
            if (state.bufferPos == state.bufferSize)
            {
                state.refillBufferDelegate(ref buffer, ref state);
            }
            return buffer[state.bufferPos++];
        }

        // private static bool RefillBuffer(ref ReadOnlySpan<byte> buffer, ref ParserInternalState state, bool mustSucceed)
        // {
        //     if (state.bufferPos < state.bufferSize)
        //     {
        //         throw new InvalidOperationException("RefillBuffer() called when buffer wasn't empty.");
        //     }

        //     if (state.totalBytesRetired + state.bufferSize == state.currentLimit)
        //     {
        //         // Oops, we hit a limit.
        //         if (mustSucceed)
        //         {
        //             throw InvalidProtocolBufferException.TruncatedMessage();
        //         }
        //         else
        //         {
        //             return false;
        //         }
        //     }

        //     state.totalBytesRetired += state.bufferSize;

        //     state.bufferPos = 0;
            
        //     if (state.segmentEnumerator.MoveNext())
        //     {
        //         buffer = state.segmentEnumerator.Current.Span;
        //         state.bufferSize = buffer.Length;
        //     }
        //     else
        //     {
        //         buffer = default(Span<byte>);
        //         state.bufferSize = 0;
        //     }

        //     if (state.bufferSize == 0)
        //     {
        //         if (mustSucceed)
        //         {
        //             throw InvalidProtocolBufferException.TruncatedMessage();
        //         }
        //         else
        //         {
        //             return false;
        //         }
        //     }
        //     else
        //     {
        //         RecomputeBufferSizeAfterLimit(ref state);
        //         int totalBytesRead =
        //             state.totalBytesRetired + state.bufferSize + state.bufferSizeAfterLimit;
        //         if (totalBytesRead < 0 || totalBytesRead > state.sizeLimit)
        //         {
        //             throw InvalidProtocolBufferException.SizeLimitExceeded();
        //         }
        //         return true;
        //     }
        // }

        // private static void RecomputeBufferSizeAfterLimit(ref ParserInternalState state)
        // {
        //     state.bufferSize += state.bufferSizeAfterLimit;
        //     int bufferEnd = state.totalBytesRetired + state.bufferSize;
        //     if (bufferEnd > state.currentLimit)
        //     {
        //         // Limit is in current buffer.
        //         state.bufferSizeAfterLimit = bufferEnd - state.currentLimit;
        //         state.bufferSize -= state.bufferSizeAfterLimit;
        //     }
        //     else
        //     {
        //         state.bufferSizeAfterLimit = 0;
        //     }
        // }
    }
}