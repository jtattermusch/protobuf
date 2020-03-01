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
    // type is supposed to be internal, but it is referenced from generated code, so must be public
    // users should never use it directly
    // this type is opaque
    public ref struct ParseContextSimple
    {
        // TODO: this is the same as the buffer from codedInputStream

        internal ReadOnlySpan<byte> buffer;
        internal int bufferPos;  // position within the buffer
        internal int bufferSize;   // size of the current buffer (equals buffer.Length, but is here for similarity with CodedInputStream)
        internal int bufferSizeAfterLimit;  // 

        internal int currentLimit;   // The absolute position of the end of the current message (including totalBytesRetired)

        internal int totalBytesRetired;   // bytes consumed before start of current buffer.

        internal int recursionDepth;  // current recursion depth
        
        internal BufferSegmentEnumerator segmentEnumerator;
        
        /// <summary>
        /// The last tag we read. 0 indicates we've read to the end of the stream
        /// (or haven't read anything yet).
        /// </summary>
        private uint lastTag;

        /// <summary>
        /// The next tag, used to store the value read by PeekTag.
        /// </summary>
        private uint nextTag;
        private bool hasNextTag;


        // the rest is basically configuration
        internal readonly int sizeLimit;
        internal readonly int recursionLimit;
        
        /// <summary>
        /// Internal-only property; when set to true, unknown fields will be discarded while parsing.
        /// </summary>
        internal bool DiscardUnknownFields { get; set; }

        /// <summary>
        /// Internal-only property; provides extension identifiers to compatible messages while parsing.
        /// </summary>
        internal ExtensionRegistry ExtensionRegistry { get; set; }

        //internal ParseContextSimple(ReadOnlySpan<byte> buffer, int bufferPos, int bufferSize, int bufferSizeAfterLimit,
        //    int currentLimit, int totalBytesRetired, int recursionDepth, BufferSegmentEnumerator segmentEnumerator,
        //    int sizeLimit, int recursionLimit)
        //{
            
            //this.reader = new SequenceReader<byte>(input);
            //this.lastTag = 0;
            //this.recursionDepth = 0;
            //this.recursionLimit = recursionLimit;
            //this.currentLimit = (int)this.reader.Length;
            //this.decoder = null;
        //    this.DiscardUnknownFields = false;
        //    this.ExtensionRegistry = null;  
        //}

        // TODO: create from codedInputStream's state
        // TODO: save back to codedInputStream.

        // similar to CodedInputStream.RefillBuffer
        internal bool RefillBuffer(bool mustSucceed)
        {
            if (bufferPos < bufferSize)
            {
                throw new InvalidOperationException("RefillBuffer() called when buffer wasn't empty.");
            }

            if (totalBytesRetired + bufferSize == currentLimit)
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

            totalBytesRetired += bufferSize;

            bufferPos = 0;
            
            if (segmentEnumerator.MoveNext())
            {
                buffer = segmentEnumerator.Current.Span;
                bufferSize = buffer.Length;
            }
            else
            {
                buffer = default(Span<byte>);
                bufferSize = 0;
            }

            if (bufferSize == 0)
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
                RecomputeBufferSizeAfterLimit();
                int totalBytesRead =
                    totalBytesRetired + bufferSize + bufferSizeAfterLimit;
                if (totalBytesRead < 0 || totalBytesRead > sizeLimit)
                {
                    throw InvalidProtocolBufferException.SizeLimitExceeded();
                }
                return true;
            }
        }

        private void RecomputeBufferSizeAfterLimit()
        {
            bufferSize += bufferSizeAfterLimit;
            int bufferEnd = totalBytesRetired + bufferSize;
            if (bufferEnd > currentLimit)
            {
                // Limit is in current buffer.
                bufferSizeAfterLimit = bufferEnd - currentLimit;
                bufferSize -= bufferSizeAfterLimit;
            }
            else
            {
                bufferSizeAfterLimit = 0;
            }
        }
    }
}