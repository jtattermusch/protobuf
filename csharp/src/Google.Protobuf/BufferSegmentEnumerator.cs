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
    // a general enumerator of buffer segments
    // for now it can enumerate a ReadOnlySequence's segments only, but it can
    // be extended to read from e.g. plain old Stream etc.
    internal struct BufferSegmentEnumerator
    {
        private ReadOnlySequence<byte>.Enumerator enumerator;
        private Stream inputStream;
        private byte[] inputStreamBuffer;
        private ReadOnlyMemory<byte> current;

        // TODO: add enumerator that can be filled from CodedInputStream
        public BufferSegmentEnumerator(ReadOnlySequence<byte> sequence)
        {
            enumerator = sequence.GetEnumerator();
            inputStream = null;
            inputStreamBuffer = null;
            current = default(Memory<byte>);
        }

        public BufferSegmentEnumerator(Stream inputStream, byte[] inputStreamBuffer)
        {
            enumerator = default(ReadOnlySequence<byte>.Enumerator);
            this.inputStream = inputStream;
            this.inputStreamBuffer = inputStreamBuffer;
            current = default(Memory<byte>);
        }
        
        public ReadOnlyMemory<byte> Current => current;
        
        public bool MoveNext()
        {
            if (inputStream == null)
            {
                var result = enumerator.MoveNext();
                current = enumerator.Current;
                return result;
            }

            int bytesRead = inputStream.Read(inputStreamBuffer, 0, inputStreamBuffer.Length);
            if (bytesRead < 0)
            {
                throw new InvalidOperationException("Stream.Read returned a negative count");
            }
            current = new Memory<byte>(inputStreamBuffer, 0, bytesRead);
            return true;
        }
    }
}