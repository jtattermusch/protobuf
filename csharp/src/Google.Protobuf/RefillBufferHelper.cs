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
            refillBufferDelegate = RefillFromStream;
            totalLength = null;
            readOnlySequenceEnumerator = default;
            this.inputStream = inputStream;
            this.inputStreamBuffer = inputStreamBuffer;
        }
        
        public bool RefillBuffer(ref ReadOnlySpan<byte> buffer, ref ParserInternalState state, bool mustSucceed)
        {
            return refillBufferDelegate(ref this, ref buffer, ref state, mustSucceed);
        }

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
            while (!helper.readOnlySequenceEnumerator.MoveNext())
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