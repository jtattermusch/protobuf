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

using Google.Protobuf.Collections;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security;

namespace Google.Protobuf
{
    /// <summary>
    /// Reads and decodes protocol message fields.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class is generally used by generated code to read appropriate
    /// primitives from the stream. It effectively encapsulates the lowest
    /// levels of protocol buffer format.
    /// </para>
    /// <para>
    /// Repeated fields and map fields are not handled by this class; use <see cref="RepeatedField{T}"/>
    /// and <see cref="MapField{TKey, TValue}"/> to serialize such fields.
    /// </para>
    /// </remarks>
    public sealed class CodedInputStream : IDisposable
    {
        /// <summary>
        /// Whether to leave the underlying stream open when disposing of this stream.
        /// This is always true when there's no stream.
        /// </summary>
        private readonly bool leaveOpen;

        /// <summary>
        /// Buffer of data read from the stream or provided at construction time.
        /// </summary>
        private readonly byte[] buffer;

        private ParserInternalState state;

        // /// <summary>
        // /// The index of the buffer at which we need to refill from the stream (if there is one).
        // /// </summary>
        // private int bufferSize;

        // private int bufferSizeAfterLimit = 0;
        // /// <summary>
        // /// The position within the current buffer (i.e. the next byte to read)
        // /// </summary>
        // private int bufferPos = 0;

        /// <summary>
        /// The stream to read further input from, or null if the byte array buffer was provided
        /// directly on construction, with no further data available.
        /// </summary>
        private readonly Stream input;

        // /// <summary>
        // /// The last tag we read. 0 indicates we've read to the end of the stream
        // /// (or haven't read anything yet).
        // /// </summary>
        // private uint lastTag = 0;

        // /// <summary>
        // /// The next tag, used to store the value read by PeekTag.
        // /// </summary>
        // private uint nextTag = 0;
        // private bool hasNextTag = false;

        internal const int DefaultRecursionLimit = 100;
        internal const int DefaultSizeLimit = Int32.MaxValue;
        internal const int BufferSize = 4096;

        // /// <summary>
        // /// The total number of bytes read before the current buffer. The
        // /// total bytes read up to the current position can be computed as
        // /// totalBytesRetired + bufferPos.
        // /// </summary>
        // private int totalBytesRetired = 0;

        // /// <summary>
        // /// The absolute position of the end of the current message.
        // /// </summary> 
        // private int currentLimit = int.MaxValue;

        // private int recursionDepth = 0;

        // private readonly int recursionLimit;
        // private readonly int sizeLimit;

        #region Construction
        // Note that the checks are performed such that we don't end up checking obviously-valid things
        // like non-null references for arrays we've just created.

        /// <summary>
        /// Creates a new CodedInputStream reading data from the given byte array.
        /// </summary>
        public CodedInputStream(byte[] buffer) : this(null, ProtoPreconditions.CheckNotNull(buffer, "buffer"), 0, buffer.Length, true)
        {            
        }

        /// <summary>
        /// Creates a new <see cref="CodedInputStream"/> that reads from the given byte array slice.
        /// </summary>
        public CodedInputStream(byte[] buffer, int offset, int length)
            : this(null, ProtoPreconditions.CheckNotNull(buffer, "buffer"), offset, offset + length, true)
        {            
            if (offset < 0 || offset > buffer.Length)
            {
                throw new ArgumentOutOfRangeException("offset", "Offset must be within the buffer");
            }
            if (length < 0 || offset + length > buffer.Length)
            {
                throw new ArgumentOutOfRangeException("length", "Length must be non-negative and within the buffer");
            }
        }

        /// <summary>
        /// Creates a new <see cref="CodedInputStream"/> reading data from the given stream, which will be disposed
        /// when the returned object is disposed.
        /// </summary>
        /// <param name="input">The stream to read from.</param>
        public CodedInputStream(Stream input) : this(input, false)
        {
        }

        /// <summary>
        /// Creates a new <see cref="CodedInputStream"/> reading data from the given stream.
        /// </summary>
        /// <param name="input">The stream to read from.</param>
        /// <param name="leaveOpen"><c>true</c> to leave <paramref name="input"/> open when the returned
        /// <c cref="CodedInputStream"/> is disposed; <c>false</c> to dispose of the given stream when the
        /// returned object is disposed.</param>
        public CodedInputStream(Stream input, bool leaveOpen)
            : this(ProtoPreconditions.CheckNotNull(input, "input"), new byte[BufferSize], 0, 0, leaveOpen)
        {
        }
        
        /// <summary>
        /// Creates a new CodedInputStream reading data from the given
        /// stream and buffer, using the default limits.
        /// </summary>
        internal CodedInputStream(Stream input, byte[] buffer, int bufferPos, int bufferSize, bool leaveOpen)
        {
            this.input = input;
            this.buffer = buffer;
            this.state.bufferPos = bufferPos;
            this.state.bufferSize = bufferSize;
            this.state.sizeLimit = DefaultSizeLimit;
            this.state.recursionLimit = DefaultRecursionLimit;
            this.state.refillBufferHelper = new RefillBufferHelper(input, buffer);
            this.state.codedInputStream = this;
            this.leaveOpen = leaveOpen;

            this.state.currentLimit = int.MaxValue;
        }

        /// <summary>
        /// Creates a new CodedInputStream reading data from the given
        /// stream and buffer, using the specified limits.
        /// </summary>
        /// <remarks>
        /// This chains to the version with the default limits instead of vice versa to avoid
        /// having to check that the default values are valid every time.
        /// </remarks>
        internal CodedInputStream(Stream input, byte[] buffer, int bufferPos, int bufferSize, int sizeLimit, int recursionLimit, bool leaveOpen)
            : this(input, buffer, bufferPos, bufferSize, leaveOpen)
        {
            if (sizeLimit <= 0)
            {
                throw new ArgumentOutOfRangeException("sizeLimit", "Size limit must be positive");
            }
            if (recursionLimit <= 0)
            {
                throw new ArgumentOutOfRangeException("recursionLimit!", "Recursion limit must be positive");
            }
            this.state.sizeLimit = sizeLimit;
            this.state.recursionLimit = recursionLimit;
        }
        #endregion

        /// <summary>
        /// Creates a <see cref="CodedInputStream"/> with the specified size and recursion limits, reading
        /// from an input stream.
        /// </summary>
        /// <remarks>
        /// This method exists separately from the constructor to reduce the number of constructor overloads.
        /// It is likely to be used considerably less frequently than the constructors, as the default limits
        /// are suitable for most use cases.
        /// </remarks>
        /// <param name="input">The input stream to read from</param>
        /// <param name="sizeLimit">The total limit of data to read from the stream.</param>
        /// <param name="recursionLimit">The maximum recursion depth to allow while reading.</param>
        /// <returns>A <c>CodedInputStream</c> reading from <paramref name="input"/> with the specified size
        /// and recursion limits.</returns>
        public static CodedInputStream CreateWithLimits(Stream input, int sizeLimit, int recursionLimit)
        {
            // Note: we may want an overload accepting leaveOpen
            return new CodedInputStream(input, new byte[BufferSize], 0, 0, sizeLimit, recursionLimit, false);
        }

        /// <summary>
        /// Returns the current position in the input stream, or the position in the input buffer
        /// </summary>
        public long Position 
        {
            get
            {
                if (input != null)
                {
                    return input.Position - ((state.bufferSize + state.bufferSizeAfterLimit) - state.bufferPos);
                }
                return state.bufferPos;
            }
        }

        /// <summary>
        /// Returns the last tag read, or 0 if no tags have been read or we've read beyond
        /// the end of the stream.
        /// </summary>
        internal uint LastTag { get { return state.lastTag; } }

        /// <summary>
        /// Returns the size limit for this stream.
        /// </summary>
        /// <remarks>
        /// This limit is applied when reading from the underlying stream, as a sanity check. It is
        /// not applied when reading from a byte array data source without an underlying stream.
        /// The default value is Int32.MaxValue.
        /// </remarks>
        /// <value>
        /// The size limit.
        /// </value>
        public int SizeLimit { get { return state.sizeLimit; } }

        /// <summary>
        /// Returns the recursion limit for this stream. This limit is applied whilst reading messages,
        /// to avoid maliciously-recursive data.
        /// </summary>
        /// <remarks>
        /// The default limit is 100.
        /// </remarks>
        /// <value>
        /// The recursion limit for this stream.
        /// </value>
        public int RecursionLimit { get { return state.recursionLimit; } }

        /// <summary>
        /// Internal-only property; when set to true, unknown fields will be discarded while parsing.
        /// </summary>
        internal bool DiscardUnknownFields
        {
            get { return state.DiscardUnknownFields; }
            set { state.DiscardUnknownFields = value; }
        }

        /// <summary>
        /// Internal-only property; provides extension identifiers to compatible messages while parsing.
        /// </summary>
        internal ExtensionRegistry ExtensionRegistry
        {
            get { return state.ExtensionRegistry; }
            set { state.ExtensionRegistry = value; }
        }

        /// <summary>
        /// Disposes of this instance, potentially closing any underlying stream.
        /// </summary>
        /// <remarks>
        /// As there is no flushing to perform here, disposing of a <see cref="CodedInputStream"/> which
        /// was constructed with the <c>leaveOpen</c> option parameter set to <c>true</c> (or one which
        /// was constructed to read from a byte array) has no effect.
        /// </remarks>
        public void Dispose()
        {
            if (!leaveOpen)
            {
                input.Dispose();
            }
        }

        #region Validation
        /// <summary>
        /// Verifies that the last call to ReadTag() returned tag 0 - in other words,
        /// we've reached the end of the stream when we expected to.
        /// </summary>
        /// <exception cref="InvalidProtocolBufferException">The 
        /// tag read was not the one specified</exception>
        internal void CheckReadEndOfStreamTag()
        {
            RefillBufferHelper.CheckReadEndOfStreamTag(ref state);
        }
        #endregion

        #region Reading of tags etc

        /// <summary>
        /// Peeks at the next field tag. This is like calling <see cref="ReadTag"/>, but the
        /// tag is not consumed. (So a subsequent call to <see cref="ReadTag"/> will return the
        /// same value.)
        /// </summary>
        public uint PeekTag()
        {
            if (state.hasNextTag)
            {
                return state.nextTag;
            }

            uint savedLast = state.lastTag;
            state.nextTag = ReadTag();
            state.hasNextTag = true;
            state.lastTag = savedLast; // Undo the side effect of ReadTag
            return state.nextTag;
        }

        /// <summary>
        /// Reads a field tag, returning the tag of 0 for "end of stream".
        /// </summary>
        /// <remarks>
        /// If this method returns 0, it doesn't necessarily mean the end of all
        /// the data in this CodedInputStream; it may be the end of the logical stream
        /// for an embedded message, for example.
        /// </remarks>
        /// <returns>The next field tag, or 0 for end of stream. (0 is never a valid tag.)</returns>
        public uint ReadTag()
        {
            var span = new ReadOnlySpan<byte>(buffer);
            return RefillBufferHelper.ParseTag(ref span, ref state);
        }

        /// <summary>
        /// Skips the data for the field with the tag we've just read.
        /// This should be called directly after <see cref="ReadTag"/>, when
        /// the caller wishes to skip an unknown field.
        /// </summary>
        /// <remarks>
        /// This method throws <see cref="InvalidProtocolBufferException"/> if the last-read tag was an end-group tag.
        /// If a caller wishes to skip a group, they should skip the whole group, by calling this method after reading the
        /// start-group tag. This behavior allows callers to call this method on any field they don't understand, correctly
        /// resulting in an error if an end-group tag has not been paired with an earlier start-group tag.
        /// </remarks>
        /// <exception cref="InvalidProtocolBufferException">The last tag was an end-group tag</exception>
        /// <exception cref="InvalidOperationException">The last read operation read to the end of the logical stream</exception>
        public void SkipLastField()
        {
            var span = new ReadOnlySpan<byte>(buffer);
            RefillBufferHelper.SkipLastField(ref span, ref state);
        }

        /// <summary>
        /// Skip a group.
        /// </summary>
        internal void SkipGroup(uint startGroupTag)
        {
            var span = new ReadOnlySpan<byte>(buffer);
            RefillBufferHelper.SkipGroup(ref span, ref state, startGroupTag);
        }

        /// <summary>
        /// Reads a double field from the stream.
        /// </summary>
        public double ReadDouble()
        {
            var span = new ReadOnlySpan<byte>(buffer);
            return ParsingPrimitivesClassic.ParseDouble(ref span, ref state);
        }

        /// <summary>
        /// Reads a float field from the stream.
        /// </summary>
        public float ReadFloat()
        {
            var span = new ReadOnlySpan<byte>(buffer);
            return ParsingPrimitivesClassic.ParseFloat(ref span, ref state);
        }

        /// <summary>
        /// Reads a uint64 field from the stream.
        /// </summary>
        public ulong ReadUInt64()
        {
            return ReadRawVarint64();
        }

        /// <summary>
        /// Reads an int64 field from the stream.
        /// </summary>
        public long ReadInt64()
        {
            return (long) ReadRawVarint64();
        }

        /// <summary>
        /// Reads an int32 field from the stream.
        /// </summary>
        public int ReadInt32()
        {
            return (int) ReadRawVarint32();
        }

        /// <summary>
        /// Reads a fixed64 field from the stream.
        /// </summary>
        public ulong ReadFixed64()
        {
            return ReadRawLittleEndian64();
        }

        /// <summary>
        /// Reads a fixed32 field from the stream.
        /// </summary>
        public uint ReadFixed32()
        {
            return ReadRawLittleEndian32();
        }

        /// <summary>
        /// Reads a bool field from the stream.
        /// </summary>
        public bool ReadBool()
        {
            return ReadRawVarint64() != 0;
        }

        /// <summary>
        /// Reads a string field from the stream.
        /// </summary>
        public string ReadString()
        {
            int length = ReadLength();
            var span = new ReadOnlySpan<byte>(buffer);
            return ParsingPrimitivesClassic.ReadRawString(ref span, ref state, length);
        }

        /// <summary>
        /// Reads an embedded message field value from the stream.
        /// </summary>
        public void ReadMessage(IMessage builder)
        {
            var span = new ReadOnlySpan<byte>(buffer);
            RefillBufferHelper.ReadMessage(ref span, ref state, builder);
        }

        /// <summary>
        /// Reads an embedded group field from the stream.
        /// </summary>
        public void ReadGroup(IMessage builder)
        {
            var span = new ReadOnlySpan<byte>(buffer);
            RefillBufferHelper.ReadGroup(ref span, ref state, builder);
        }

        /// <summary>
        /// Reads a bytes field value from the stream.
        /// </summary>   
        public ByteString ReadBytes()
        {
            int length = ReadLength();
            if (length <= state.bufferSize - state.bufferPos && length > 0)
            {
                // Fast path:  We already have the bytes in a contiguous buffer, so
                //   just copy directly from it.
                ByteString result = ByteString.CopyFrom(buffer, state.bufferPos, length);
                state.bufferPos += length;
                return result;
            }
            else
            {
                // Slow path:  Build a byte array and attach it to a new ByteString.
                return ByteString.AttachBytes(ReadRawBytes(length));
            }
        }

        /// <summary>
        /// Reads a uint32 field value from the stream.
        /// </summary>   
        public uint ReadUInt32()
        {
            return ReadRawVarint32();
        }

        /// <summary>
        /// Reads an enum field value from the stream.
        /// </summary>   
        public int ReadEnum()
        {
            // Currently just a pass-through, but it's nice to separate it logically from WriteInt32.
            return (int) ReadRawVarint32();
        }

        /// <summary>
        /// Reads an sfixed32 field value from the stream.
        /// </summary>   
        public int ReadSFixed32()
        {
            return (int) ReadRawLittleEndian32();
        }

        /// <summary>
        /// Reads an sfixed64 field value from the stream.
        /// </summary>   
        public long ReadSFixed64()
        {
            return (long) ReadRawLittleEndian64();
        }

        /// <summary>
        /// Reads an sint32 field value from the stream.
        /// </summary>   
        public int ReadSInt32()
        {
            return DecodeZigZag32(ReadRawVarint32());
        }

        /// <summary>
        /// Reads an sint64 field value from the stream.
        /// </summary>   
        public long ReadSInt64()
        {
            return DecodeZigZag64(ReadRawVarint64());
        }

        /// <summary>
        /// Reads a length for length-delimited data.
        /// </summary>
        /// <remarks>
        /// This is internally just reading a varint, but this method exists
        /// to make the calling code clearer.
        /// </remarks>
        public int ReadLength()
        {
            return (int) ReadRawVarint32();
        }

        /// <summary>
        /// Peeks at the next tag in the stream. If it matches <paramref name="tag"/>,
        /// the tag is consumed and the method returns <c>true</c>; otherwise, the
        /// stream is left in the original position and the method returns <c>false</c>.
        /// </summary>
        public bool MaybeConsumeTag(uint tag)
        {
            if (PeekTag() == tag)
            {
                state.hasNextTag = false;
                return true;
            }
            return false;
        }

        internal static float? ReadFloatWrapperLittleEndian(CodedInputStream input)
        {
            var span = new ReadOnlySpan<byte>(input.buffer);
            return ParsingPrimitivesWrappers.ReadFloatWrapperLittleEndian(ref span, ref input.state);
        }

        internal static float? ReadFloatWrapperSlow(CodedInputStream input)
        {
            var span = new ReadOnlySpan<byte>(input.buffer);
            return ParsingPrimitivesWrappers.ReadFloatWrapperSlow(ref span, ref input.state);
        }

        internal static double? ReadDoubleWrapperLittleEndian(CodedInputStream input)
        {
            var span = new ReadOnlySpan<byte>(input.buffer);
            return ParsingPrimitivesWrappers.ReadDoubleWrapperLittleEndian(ref span, ref input.state);
        }

        internal static double? ReadDoubleWrapperSlow(CodedInputStream input)
        {
            var span = new ReadOnlySpan<byte>(input.buffer);
            return ParsingPrimitivesWrappers.ReadDoubleWrapperSlow(ref span, ref input.state);
        }

        internal static bool? ReadBoolWrapper(CodedInputStream input)
        {
            var span = new ReadOnlySpan<byte>(input.buffer);
            return ParsingPrimitivesWrappers.ReadBoolWrapper(ref span, ref input.state);
        }

        internal static uint? ReadUInt32Wrapper(CodedInputStream input)
        {
            var span = new ReadOnlySpan<byte>(input.buffer);
            return ParsingPrimitivesWrappers.ReadUInt32Wrapper(ref span, ref input.state);
        }

        private static uint? ReadUInt32WrapperSlow(CodedInputStream input)
        {
            var span = new ReadOnlySpan<byte>(input.buffer);
            return ParsingPrimitivesWrappers.ReadUInt32WrapperSlow(ref span, ref input.state);
        }

        internal static int? ReadInt32Wrapper(CodedInputStream input)
        {
            var span = new ReadOnlySpan<byte>(input.buffer);
            return ParsingPrimitivesWrappers.ReadInt32Wrapper(ref span, ref input.state);
        }

        internal static ulong? ReadUInt64Wrapper(CodedInputStream input)
        {
            var span = new ReadOnlySpan<byte>(input.buffer);
            return ParsingPrimitivesWrappers.ReadUInt64Wrapper(ref span, ref input.state);
        }

        internal static ulong? ReadUInt64WrapperSlow(CodedInputStream input)
        {
            var span = new ReadOnlySpan<byte>(input.buffer);
            return ParsingPrimitivesWrappers.ReadUInt64WrapperSlow(ref span, ref input.state);
        }

        internal static long? ReadInt64Wrapper(CodedInputStream input)
        {
            var span = new ReadOnlySpan<byte>(input.buffer);
            return ParsingPrimitivesWrappers.ReadInt64Wrapper(ref span, ref input.state);
        }

#endregion

        #region Underlying reading primitives

        /// <summary>
        /// Reads a raw Varint from the stream.  If larger than 32 bits, discard the upper bits.
        /// This method is optimised for the case where we've got lots of data in the buffer.
        /// That means we can check the size just once, then just read directly from the buffer
        /// without constant rechecking of the buffer length.
        /// </summary>
        internal uint ReadRawVarint32()
        {
            var span = new ReadOnlySpan<byte>(buffer);
            return ParsingPrimitivesClassic.ParseRawVarint32(ref span, ref state);
        }

        /// <summary>
        /// Reads a varint from the input one byte at a time, so that it does not
        /// read any bytes after the end of the varint. If you simply wrapped the
        /// stream in a CodedInputStream and used ReadRawVarint32(Stream)
        /// then you would probably end up reading past the end of the varint since
        /// CodedInputStream buffers its input.
        /// </summary>
        /// <param name="input"></param>
        /// <returns></returns>
        internal static uint ReadRawVarint32(Stream input)
        {
            return ParsingPrimitivesClassic.ReadRawVarint32(input);
        }

        /// <summary>
        /// Reads a raw varint from the stream.
        /// </summary>
        internal ulong ReadRawVarint64()
        {
            var span = new ReadOnlySpan<byte>(buffer);
            return ParsingPrimitivesClassic.ParseRawVarint64(ref span, ref state);
        }

        /// <summary>
        /// Reads a 32-bit little-endian integer from the stream.
        /// </summary>
        internal uint ReadRawLittleEndian32()
        {
            var span = new ReadOnlySpan<byte>(buffer);
            return ParsingPrimitivesClassic.ParseRawLittleEndian32(ref span, ref state);
        }

        /// <summary>
        /// Reads a 64-bit little-endian integer from the stream.
        /// </summary>
        internal ulong ReadRawLittleEndian64()
        {
            var span = new ReadOnlySpan<byte>(buffer);
            return ParsingPrimitivesClassic.ParseRawLittleEndian64(ref span, ref state);
        }

        /// <summary>
        /// Decode a 32-bit value with ZigZag encoding.
        /// </summary>
        /// <remarks>
        /// ZigZag encodes signed integers into values that can be efficiently
        /// encoded with varint.  (Otherwise, negative values must be 
        /// sign-extended to 64 bits to be varint encoded, thus always taking
        /// 10 bytes on the wire.)
        /// </remarks>
        internal static int DecodeZigZag32(uint n)
        {
            // TODO: move to parsing primitives
            return (int)(n >> 1) ^ -(int)(n & 1);
        }

        /// <summary>
        /// Decode a 32-bit value with ZigZag encoding.
        /// </summary>
        /// <remarks>
        /// ZigZag encodes signed integers into values that can be efficiently
        /// encoded with varint.  (Otherwise, negative values must be 
        /// sign-extended to 64 bits to be varint encoded, thus always taking
        /// 10 bytes on the wire.)
        /// </remarks>
        internal static long DecodeZigZag64(ulong n)
        {
            // TODO: move to parsing primitives
            return (long)(n >> 1) ^ -(long)(n & 1);
        }
        #endregion

        #region Internal reading and buffer management

        /// <summary>
        /// Sets currentLimit to (current position) + byteLimit. This is called
        /// when descending into a length-delimited embedded message. The previous
        /// limit is returned.
        /// </summary>
        /// <returns>The old limit.</returns>
        internal int PushLimit(int byteLimit)
        {
            return RefillBufferHelper.PushLimit(ref state, byteLimit);
        }

        /// <summary>
        /// Discards the current limit, returning the previous limit.
        /// </summary>
        internal void PopLimit(int oldLimit)
        {
            RefillBufferHelper.PopLimit(ref state, oldLimit);
        }

        /// <summary>
        /// Returns whether or not all the data before the limit has been read.
        /// </summary>
        /// <returns></returns>
        internal bool ReachedLimit
        {
            get
            {
                return RefillBufferHelper.IsReachedLimit(ref state);
            }
        }

        /// <summary>
        /// Returns true if the stream has reached the end of the input. This is the
        /// case if either the end of the underlying input source has been reached or
        /// the stream has reached a limit created using PushLimit.
        /// </summary>
        public bool IsAtEnd
        {
            get
            {
                var span = new ReadOnlySpan<byte>(buffer);
                return RefillBufferHelper.IsAtEnd(ref span, ref state);
            }
        }

        /// <summary>
        /// Called when buffer is empty to read more bytes from the
        /// input.  If <paramref name="mustSucceed"/> is true, RefillBuffer() gurantees that
        /// either there will be at least one byte in the buffer when it returns
        /// or it will throw an exception.  If <paramref name="mustSucceed"/> is false,
        /// RefillBuffer() returns false if no more bytes were available.
        /// </summary>
        /// <param name="mustSucceed"></param>
        /// <returns></returns>
        private bool RefillBuffer(bool mustSucceed)
        {
            var span = new ReadOnlySpan<byte>(buffer);
            return state.refillBufferHelper.RefillBuffer(ref span, ref state, mustSucceed);
        }

        /// <summary>
        /// Reads a fixed size of bytes from the input.
        /// </summary>
        /// <exception cref="InvalidProtocolBufferException">
        /// the end of the stream or the current limit was reached
        /// </exception>
        internal byte[] ReadRawBytes(int size)
        {
            var span = new ReadOnlySpan<byte>(buffer);
            return ParsingPrimitivesClassic.ReadRawBytes(ref span, ref state, size);
        }

        /// <summary>
        /// Reads and discards <paramref name="size"/> bytes.
        /// </summary>
        /// <exception cref="InvalidProtocolBufferException">the end of the stream
        /// or the current limit was reached</exception>
        private void SkipRawBytes(int size)
        {
            var span = new ReadOnlySpan<byte>(buffer);
            ParsingPrimitivesClassic.SkipRawBytes(ref span, ref state, size);
        }
#endregion
    }
}
