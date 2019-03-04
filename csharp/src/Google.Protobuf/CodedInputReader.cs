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
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
#if !NET35
using System.Threading;
using System.Threading.Tasks;
#endif
#if NET35
using Google.Protobuf.Compatibility;
#endif
using System.Buffers;

namespace Google.Protobuf
{
    // TODO: class name TBD (CodedInputReader, CodedInputContext, CodedInputByRefReader)
    public ref struct CodedInputReader
    {
        // to avoid accessing first slice from inputSequence all the time,
        // also to allow reading from single span.
        ReadOnlySpan<byte> buffer;
        bool isInputSequence;
        ReadOnlySequence<byte> inputSequence;  // struct!

        bool isFinalBlock;

        // maybe we need bufferPos to avoid incrementing totalBytesRetired all the time..
        // int bufferPos;
        
        public CodedInputReader(ReadOnlySpan<byte> buffer, bool isFinalBlock, CodedInputReaderState state)
        {
            this.buffer = buffer;
            this.isInputSequence = false;
            this.inputSequence = default;
            this.isFinalBlock = isFinalBlock;
            // TODO: initialize state
        }

        public CodedInputReader(in ReadOnlySequence<byte> input, bool isFinalBlock, CodedInputReaderState state)
        {
            this.buffer = default;
            this.isInputSequence = true;
            this.inputSequence = input;
            this.isFinalBlock = isFinalBlock;
            // TODO: initialize state
        }


        public CodedInputReaderState CurrentState => new CodedInputReaderState();

        // internal state of coded input stream
        private uint lastTag;
        private uint nextTag;
        private bool hasNextTag;
        private int totalBytesRetired;
        private int currentLimit;  // initialize to maxInt!
        private int recursionDepth;

        // parsing limits
        private readonly int recursionLimit; 
        private readonly int sizeLimit;

        // exposed methods that copy potentially large amount of data:
        // TODO: figure out an opt-in way to avoid copying and/or allocation
        // ReadBytes()
        // ReadString();
    }

    public struct CodedInputReaderState
    {
        // internal state of the reader

    }
}