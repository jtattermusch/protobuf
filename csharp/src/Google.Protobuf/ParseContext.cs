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
    // the idea of epsilon buffer is described here:
    // https://github.com/protocolbuffers/protobuf/blob/743a4322ba8332d0b78e30a699e1f3538f8b2093/src/google/protobuf/parse_context.h#L108

    // type is supposed to be internal, but it is referenced from generated code, so must be public
    // users should never use it directly
    // this type is opaque
    public ref struct ParseContext
    {
        // tag is max 5 bytes, varint is max. 10 bytes, so this is always enough room for tag and value 
        // (for any of the primitive values) or tag and length (for length delimited fields).
        // We always need to know we have at least 16 bytes left in front of us in the buffer.
        private const int EpsilonBufferSize = 32;
        private const int MinLookaheadWindow = EpsilonBufferSize / 2;

        internal ReadOnlySpan<byte> buffer; 
        internal int bufferPos;  // position within the buffer
        internal int bufferSize;   // size of the current buffer

        internal int currentLimit;

        internal int totalBytesRetired;   // bytes consumed before start of current buffer.

        internal int recursionDepth;  // current recursion depth
        
        internal BufferSegmentEnumerator segmentEnumerator;

        internal bool currentlyInEpsilonBuffer;  // is current buffer pointing to the eps buffer?
        internal Span<byte> epsilonBuffer;  // scratch space that always has constant size (EpsilonBufferSize)

        // if in epsilon buffer, this is the next buffer to switch to
        internal ReadOnlySpan<byte> nextBuffer;
        internal int nextBufferStartPositionInEpsilon;

        


        // TODO: figure out handling limit exactly
        //   bufferSizeAfterLimit (this is now the rest of span after "Limit" in the current buffer)
        //   totalBytesRetired (what we read before beginning of current buffer)
        //private int currentLimit = int.MaxValue;  // The absolute position of the end of the current message.

        // remembering some info about tags
        //private uint lastTag = 0;
        //private uint nextTag = 0;  // only used by PeekNextTag?
        //private bool hasNextTag = false;

        // the rest is basically configuration
        internal readonly int sizeLimit;
        internal readonly int recursionLimit;

        //internal ParseContext()
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
        
        /// <summary>
        /// Internal-only property; when set to true, unknown fields will be discarded while parsing.
        /// </summary>
        internal bool DiscardUnknownFields { get; set; }

        /// <summary>
        /// Internal-only property; provides extension identifiers to compatible messages while parsing.
        /// </summary>
        internal ExtensionRegistry ExtensionRegistry { get; set; }


        // similar to CodedInputStream.RefillBuffer
        internal bool RefillBuffer()
        {
            // we should only refill if we're close enough to the end of the buffer
            int lookaheadRemaining = buffer.Length - bufferPos;
            if (lookaheadRemaining > MinLookaheadWindow)
            {
                throw new Exception("Trying to refill too early.");
            }

            // TODO: remember if the current buffer is the last one.

            if (currentlyInEpsilonBuffer)
            {
                if (nextBuffer.Length < MinLookaheadWindow)
                {
                    // the next buffer is too small, copy the data over to epsilon buffer
                    // we know we are already past the half of epsilon buffer
                    int alreadyConsumedNextBufferBytes = bufferPos - nextBufferStartPositionInEpsilon;
                    nextBuffer.Slice(alreadyConsumedNextBufferBytes).CopyTo(epsilonBuffer.Slice(0, nextBuffer.Length - alreadyConsumedNextBufferBytes));

                    totalBytesRetired += bufferPos;
                    buffer = epsilonBuffer;
                    bufferPos = 0;  // we basically just shifted the bytes to the start of epsilon buffer
                    bufferSize = nextBuffer.Length - alreadyConsumedNextBufferBytes;
                    currentLimit = -1;
                    

                    currentlyInEpsilonBuffer = true;
                    nextBufferStartPositionInEpsilon = -1;
                    nextBuffer = default(Span<byte>);

                    // TODO: what if we get a series of small next buffers adn we need to refill again???

                    return true;
                }
                else 
                {
                    // just start using the next buffer from the right position
                    totalBytesRetired += bufferPos;
                    currentlyInEpsilonBuffer = false;
                    buffer = nextBuffer.Slice(bufferPos - nextBufferStartPositionInEpsilon);
                    bufferPos = 0;
                    bufferSize = buffer.Length;
                    currentLimit = -1; // TODO: adjust
                    nextBuffer = default(Span<byte>);
                    return true;
                }
            }
            else
            {
                int remainingBytesInOldBuffer = bufferSize - bufferPos;

                if (!segmentEnumerator.MoveNext())
                {
                    if (remainingBytesInOldBuffer == 0)
                    {
                        return false;
                    }

                    // copy remaining bytes to epsilon buffer
                    buffer.Slice(bufferPos, remainingBytesInOldBuffer).CopyTo(epsilonBuffer.Slice(0, remainingBytesInOldBuffer));

                    totalBytesRetired += bufferPos;
                    buffer = epsilonBuffer;
                    bufferPos = 0;
                    bufferSize = remainingBytesInOldBuffer;
                    currentLimit = -1;

                    currentlyInEpsilonBuffer = true;
                    nextBuffer = default(Span<byte>);
                    nextBufferStartPositionInEpsilon = -1;
                    return true;
                }
                nextBuffer = segmentEnumerator.Current.Span;

                if (nextBuffer.Length == 0)
                {
                    throw new Exception("Got an empty buffer segment from the enumerator.");
                }
                
                if (remainingBytesInOldBuffer == 0 && nextBuffer.Length >= MinLookaheadWindow)
                {
                    // we've exhausted the previous buffer completely and next buffer is big enough to switch over directly
                    totalBytesRetired += bufferPos;
                    buffer = nextBuffer;
                    bufferPos = 0;
                    bufferSize = nextBuffer.Length;
                    currentLimit = -1; // TODO: adjust
                    nextBuffer = default(Span<byte>);

                    currentlyInEpsilonBuffer = false;
                    nextBufferStartPositionInEpsilon = -1;
                    return true;
                }
                else
                {
                    if (remainingBytesInOldBuffer > 0)
                    {
                        buffer.Slice(bufferPos, remainingBytesInOldBuffer).CopyTo(epsilonBuffer.Slice(0, remainingBytesInOldBuffer));
                    }

                    int bytesFromNextBuffer = Math.Min(EpsilonBufferSize - remainingBytesInOldBuffer, nextBuffer.Length);
                    nextBuffer.Slice(0, bytesFromNextBuffer).CopyTo(epsilonBuffer.Slice(remainingBytesInOldBuffer, bytesFromNextBuffer));

                    totalBytesRetired += bufferPos;
                    buffer = epsilonBuffer;
                    bufferPos = 0;
                    bufferSize = remainingBytesInOldBuffer + bytesFromNextBuffer;
                    currentLimit = -1;

                    currentlyInEpsilonBuffer = true;
                    nextBufferStartPositionInEpsilon = remainingBytesInOldBuffer;
                    return true;
                }
            }
        }
    }
}