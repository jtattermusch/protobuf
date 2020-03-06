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
    internal struct RefillBufferHelper
    {
        private delegate bool RefillBufferDelegate(ref RefillBufferHelper helper, ref ReadOnlySpan<byte> buffer, ref ParserInternalState state, bool mustSucceed);

        private RefillBufferDelegate refillBufferDelegate;
        private int? totalLength;
        private ReadOnlySequence<byte>.Enumerator readOnlySequenceEnumerator;
        private Stream inputStream;
        private byte[] inputStreamBuffer;
        public RefillBufferHelper(ReadOnlySequence<byte> sequence)
        {
            refillBufferDelegate = RefillFromReadOnlySequence;
            totalLength = (int) sequence.Length;
            readOnlySequenceEnumerator = sequence.GetEnumerator();
            inputStream = null;
            inputStreamBuffer = null;
        }

        public RefillBufferHelper(Stream inputStream, byte[] inputStreamBuffer)
        {
            // TODO: if inputStream == null, use a different simplified approach for refilling.
            refillBufferDelegate = RefillFromStream;
            totalLength = inputStream == null ? (int?)inputStreamBuffer.Length : null;
            readOnlySequenceEnumerator = default;
            this.inputStream = inputStream;
            this.inputStreamBuffer = inputStreamBuffer;
        }
        
        public bool RefillBuffer(ref ReadOnlySpan<byte> buffer, ref ParserInternalState state, bool mustSucceed)
        {
            return refillBufferDelegate(ref this, ref buffer, ref state, mustSucceed);
        }

        public int? TotalLength => totalLength;

        /// <summary>
        /// Sets currentLimit to (current position) + byteLimit. This is called
        /// when descending into a length-delimited embedded message. The previous
        /// limit is returned.
        /// </summary>
        /// <returns>The old limit.</returns>
        public static int PushLimit(ref ParserInternalState state, int byteLimit)
        {
            if (byteLimit < 0)
            {
                throw InvalidProtocolBufferException.NegativeSize();
            }
            byteLimit += state.totalBytesRetired + state.bufferPos;
            int oldLimit = state.currentLimit;
            if (byteLimit > oldLimit)
            {
                throw InvalidProtocolBufferException.TruncatedMessage();
            }
            state.currentLimit = byteLimit;

            RecomputeBufferSizeAfterLimit(ref state);

            return oldLimit;
        }

        /// <summary>
        /// Discards the current limit, returning the previous limit.
        /// </summary>
        public static void PopLimit(ref ParserInternalState state, int oldLimit)
        {
            state.currentLimit = oldLimit;
            RecomputeBufferSizeAfterLimit(ref state);
        }

        // TODO: this method doesn't quite belong here and it's not very hermetic
        public static uint ParseTag(ref ReadOnlySpan<byte> buffer, ref ParserInternalState state)
        {
            // TODO: move the parsing logic elsewhere
            if (state.hasNextTag)
            {
                state.lastTag = state.nextTag;
                state.hasNextTag = false;
                return state.lastTag;
            }

            // Optimize for the incredibly common case of having at least two bytes left in the buffer,
            // and those two bytes being enough to get the tag. This will be true for fields up to 4095.
            if (state.bufferPos + 2 <= state.bufferSize)
            {
                int tmp = buffer[state.bufferPos++];
                if (tmp < 128)
                {
                    state.lastTag = (uint)tmp;
                }
                else
                {
                    int result = tmp & 0x7f;
                    if ((tmp = buffer[state.bufferPos++]) < 128)
                    {
                        result |= tmp << 7;
                        state.lastTag = (uint) result;
                    }
                    else
                    {
                        // Nope, rewind and go the potentially slow route.
                        state.bufferPos -= 2;
                        state.lastTag = ParsingPrimitivesClassic.ParseRawVarint32(ref buffer, ref state);
                    }
                }
            }
            else
            {
                if (IsAtEnd(ref buffer, ref state))
                {
                    state.lastTag = 0;
                    return 0;
                }

                state.lastTag = ParsingPrimitivesClassic.ParseRawVarint32(ref buffer, ref state);
            }
            if (WireFormat.GetTagFieldNumber(state.lastTag) == 0)
            {
                // If we actually read a tag with a field of 0, that's not a valid tag.
                throw InvalidProtocolBufferException.InvalidTag();
            }

            // TODO: this might actually be a bug (reading tag just before the limit returns 0)
            // which is technically not correct?
            if (IsReachedLimit(ref state))
            {
                return 0;
            }
            return state.lastTag;
        }

        // TODO: move to a better place
        public static void SkipLastField(ref ReadOnlySpan<byte> buffer, ref ParserInternalState state)
        {
            if (state.lastTag == 0)
            {
                throw new InvalidOperationException("SkipLastField cannot be called at the end of a stream");
            }
            switch (WireFormat.GetTagWireType(state.lastTag))
            {
                case WireFormat.WireType.StartGroup:
                    SkipGroup(ref buffer, ref state, state.lastTag);
                    break;
                case WireFormat.WireType.EndGroup:
                    throw new InvalidProtocolBufferException(
                        "SkipLastField called on an end-group tag, indicating that the corresponding start-group was missing");
                case WireFormat.WireType.Fixed32:
                    ParsingPrimitivesClassic.ParseRawLittleEndian32(ref buffer, ref state);
                    break;
                case WireFormat.WireType.Fixed64:
                    ParsingPrimitivesClassic.ParseRawLittleEndian64(ref buffer, ref state);
                    break;
                case WireFormat.WireType.LengthDelimited:
                    var length = ParsingPrimitivesClassic.ParseLength(ref buffer, ref state);
                    ParsingPrimitivesClassic.SkipRawBytes(ref buffer, ref state, length);
                    break;
                case WireFormat.WireType.Varint:
                    ParsingPrimitivesClassic.ParseRawVarint32(ref buffer, ref state);
                    break;
            }
        }

        /// <summary>
        /// Skip a group.
        /// </summary>
        public static void SkipGroup(ref ReadOnlySpan<byte> buffer, ref ParserInternalState state, uint startGroupTag)
        {
            // Note: Currently we expect this to be the way that groups are read. We could put the recursion
            // depth changes into the ReadTag method instead, potentially...
            state.recursionDepth++;
            if (state.recursionDepth >= state.recursionLimit)
            {
                throw InvalidProtocolBufferException.RecursionLimitExceeded();
            }
            uint tag;
            while (true)
            {
                tag = ParseTag(ref buffer, ref state);
                if (tag == 0)
                {
                    throw InvalidProtocolBufferException.TruncatedMessage();
                }
                // Can't call SkipLastField for this case- that would throw.
                if (WireFormat.GetTagWireType(tag) == WireFormat.WireType.EndGroup)
                {
                    break;
                }
                // This recursion will allow us to handle nested groups.
                SkipLastField(ref buffer, ref state);
            }
            int startField = WireFormat.GetTagFieldNumber(startGroupTag);
            int endField = WireFormat.GetTagFieldNumber(tag);
            if (startField != endField)
            {
                throw new InvalidProtocolBufferException(
                    $"Mismatched end-group tag. Started with field {startField}; ended with field {endField}");
            }
            state.recursionDepth--;
        }

        public static void ReadMessage(ref ReadOnlySpan<byte> buffer, ref ParserInternalState state, IMessage message)
        {
            int length = ParsingPrimitivesClassic.ParseLength(ref buffer, ref state);
            if (state.recursionDepth >= state.recursionLimit)
            {
                throw InvalidProtocolBufferException.RecursionLimitExceeded();
            }
            int oldLimit = PushLimit(ref state, length);
            ++state.recursionDepth;

            // TODO: choose method to invoke based on message type...
            //if (message is IBufferMessage)
            //{
            //    // TODO: call internal parse...
            //}
            //else
            {
                if (state.codedInputStream == null)
                {
                    // TODO: improve the msg
                    throw new InvalidProtocolBufferException("Cannot parse message with current parse context. Do you need to regenerate the code?");
                }
                message.MergeFrom(state.codedInputStream);
            }

            CheckReadEndOfStreamTag(ref state);
            // Check that we've read exactly as much data as expected.
            if (!IsReachedLimit(ref state))
            {
                throw InvalidProtocolBufferException.TruncatedMessage();
            }
            --state.recursionDepth;
            PopLimit(ref state, oldLimit);
        }

        public static void ReadGroup(ref ReadOnlySpan<byte> buffer, ref ParserInternalState state, IMessage message)
        {
            if (state.recursionDepth >= state.recursionLimit)
            {
                throw InvalidProtocolBufferException.RecursionLimitExceeded();
            }
            ++state.recursionDepth;
            
            // TODO: choose method to invoke based on message type...
            //if (message is IBufferMessage)
            //{
            //    // TODO: call internal parse...
            //}
            //else
            {
                if (state.codedInputStream == null)
                {
                    // TODO: improve the msg
                    throw new InvalidProtocolBufferException("Cannot parse message with current parse context. Do you need to regenerate the code?");
                }
                message.MergeFrom(state.codedInputStream);
            }

            --state.recursionDepth;
        }

        /// <summary>
        /// Returns whether or not all the data before the limit has been read.
        /// </summary>
        /// <returns></returns>
        public static bool IsReachedLimit(ref ParserInternalState state)
        {
            if (state.currentLimit == int.MaxValue)
            {
                return false;
            }
            int currentAbsolutePosition = state.totalBytesRetired + state.bufferPos;
            return currentAbsolutePosition >= state.currentLimit;
        }

        /// <summary>
        /// Returns true if the stream has reached the end of the input. This is the
        /// case if either the end of the underlying input source has been reached or
        /// the stream has reached a limit created using PushLimit.
        /// </summary>
        public static bool IsAtEnd(ref ReadOnlySpan<byte> buffer, ref ParserInternalState state)
        {
            return state.bufferPos == state.bufferSize && !state.refillBufferHelper.RefillBuffer(ref buffer, ref state, false);
        }

        /// <summary>
        /// Verifies that the last call to ReadTag() returned tag 0 - in other words,
        /// we've reached the end of the stream when we expected to.
        /// </summary>
        /// <exception cref="InvalidProtocolBufferException">The 
        /// tag read was not the one specified</exception>
        public static void CheckReadEndOfStreamTag(ref ParserInternalState state)
        {
            if (state.lastTag != 0)
            {
                throw InvalidProtocolBufferException.MoreDataAvailable();
            }
        }

        private static bool RefillFromReadOnlySequenceImpl(ref RefillBufferHelper helper, ref ReadOnlySpan<byte> buffer, ref ParserInternalState state, bool mustSucceed)
        {
            // TODO: remove duplication between FromReadOnlySequence and FromStream
            if (state.bufferPos < state.bufferSize)
            {
                throw new InvalidOperationException("RefillBuffer() called when buffer wasn't empty.");
            }

            if (state.totalBytesRetired + state.bufferSize == state.currentLimit)
            {
                // Oops, we hit a limit.
                if (mustSucceed)
                {
                    throw InvalidProtocolBufferException.TruncatedMessage();
                }
                else
                {
                    return false;
                }
            }

            state.totalBytesRetired += state.bufferSize;

            state.bufferPos = 0;
            state.bufferSize = 0;
            while (helper.readOnlySequenceEnumerator.MoveNext())
            {
                
                buffer = helper.readOnlySequenceEnumerator.Current.Span;
                state.bufferSize = buffer.Length;
                if (buffer.Length != 0)
                {
                    break;
                }
            }

            if (state.bufferSize == 0)
            {
                if (mustSucceed)
                {
                    throw InvalidProtocolBufferException.TruncatedMessage();
                }
                else
                {
                    return false;
                }
            }
            else
            {
                RecomputeBufferSizeAfterLimit(ref state);
                int totalBytesRead =
                    state.totalBytesRetired + state.bufferSize + state.bufferSizeAfterLimit;
                if (totalBytesRead < 0 || totalBytesRead > state.sizeLimit)
                {
                    throw InvalidProtocolBufferException.SizeLimitExceeded();
                }
                return true;
            }
        }

        private static RefillBufferDelegate RefillFromReadOnlySequence = new RefillBufferDelegate(RefillFromReadOnlySequenceImpl);

        private static bool RefillFromStreamImpl(ref RefillBufferHelper helper, ref ReadOnlySpan<byte> buffer, ref ParserInternalState state, bool mustSucceed)
        {
            Stream input = helper.inputStream;

            if (state.bufferPos < state.bufferSize)
            {
                throw new InvalidOperationException("RefillBuffer() called when buffer wasn't empty.");
            }

            if (state.totalBytesRetired + state.bufferSize == state.currentLimit)
            {
                // Oops, we hit a limit.
                if (mustSucceed)
                {
                    throw InvalidProtocolBufferException.TruncatedMessage();
                }
                else
                {
                    return false;
                }
            }

            state.totalBytesRetired += state.bufferSize;

            state.bufferPos = 0;
            state.bufferSize = (input == null) ? 0 : input.Read(helper.inputStreamBuffer, 0, buffer.Length);
            if (state.bufferSize < 0)
            {
                throw new InvalidOperationException("Stream.Read returned a negative count");
            }
            if (state.bufferSize == 0)
            {
                if (mustSucceed)
                {
                    throw InvalidProtocolBufferException.TruncatedMessage();
                }
                else
                {
                    return false;
                }
            }
            else
            {
                RecomputeBufferSizeAfterLimit(ref state);
                int totalBytesRead =
                    state.totalBytesRetired + state.bufferSize + state.bufferSizeAfterLimit;
                if (totalBytesRead < 0 || totalBytesRead > state.sizeLimit)
                {
                    throw InvalidProtocolBufferException.SizeLimitExceeded();
                }
                return true;
            }
        }

        private static RefillBufferDelegate RefillFromStream = new RefillBufferDelegate(RefillFromStreamImpl);

        private static void RecomputeBufferSizeAfterLimit(ref ParserInternalState state)
        {
            state.bufferSize += state.bufferSizeAfterLimit;
            int bufferEnd = state.totalBytesRetired + state.bufferSize;
            if (bufferEnd > state.currentLimit)
            {
                // Limit is in current buffer.
                state.bufferSizeAfterLimit = bufferEnd - state.currentLimit;
                state.bufferSize -= state.bufferSizeAfterLimit;
            }
            else
            {
                state.bufferSizeAfterLimit = 0;
            }
        }        
    }
}