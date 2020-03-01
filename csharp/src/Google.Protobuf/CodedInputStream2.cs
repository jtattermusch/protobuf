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
using System.Buffers;

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

        /// <summary>
        /// The stream to read further input from, or null if the byte array buffer was provided
        /// directly on construction, with no further data available.
        /// </summary>
        private readonly Stream input;

        internal const int DefaultRecursionLimit = 100;
        internal const int DefaultSizeLimit = Int32.MaxValue;
        internal const int BufferSize = 4096;

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
            this.state.refillBufferDelegate = new ParserInternalState.RefillBufferDelegate(RefillBufferHandler);
            this.leaveOpen = leaveOpen;
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
            if (state.lastTag != 0)
            {
                throw InvalidProtocolBufferException.MoreDataAvailable();
            }
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
                        var span = new ReadOnlySpan<byte>(buffer);
                        state.lastTag = ParsingPrimitivesClassic.ParseRawVarint32(ref span, ref state);
                    }
                }
            }
            else
            {
                if (IsAtEnd)
                {
                    state.lastTag = 0;
                    return 0;
                }
                
                var span = new ReadOnlySpan<byte>(buffer);
                state.lastTag = ParsingPrimitivesClassic.ParseRawVarint32(ref span, ref state);
            }
            if (WireFormat.GetTagFieldNumber(state.lastTag) == 0)
            {
                // If we actually read a tag with a field of 0, that's not a valid tag.
                throw InvalidProtocolBufferException.InvalidTag();
            }
            if (ReachedLimit)
            {
                return 0;
            }
            return state.lastTag;
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
            if (state.lastTag == 0)
            {
                throw new InvalidOperationException("SkipLastField cannot be called at the end of a stream");
            }
            switch (WireFormat.GetTagWireType(state.lastTag))
            {
                case WireFormat.WireType.StartGroup:
                    SkipGroup(state.lastTag);
                    break;
                case WireFormat.WireType.EndGroup:
                    throw new InvalidProtocolBufferException(
                        "SkipLastField called on an end-group tag, indicating that the corresponding start-group was missing");
                case WireFormat.WireType.Fixed32:
                    ReadFixed32();
                    break;
                case WireFormat.WireType.Fixed64:
                    ReadFixed64();
                    break;
                case WireFormat.WireType.LengthDelimited:
                    var length = ReadLength();
                    SkipRawBytes(length);
                    break;
                case WireFormat.WireType.Varint:
                    ReadInt64();
                    break;
            }
        }

        /// <summary>
        /// Skip a group.
        /// </summary>
        internal void SkipGroup(uint startGroupTag)
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
                tag = ReadTag();
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
                SkipLastField();
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
            var span = new ReadOnlySpan<byte>(buffer);
            return ParsingPrimitivesClassic.ParseRawVarint64(ref span, ref state);
        }

        /// <summary>
        /// Reads an int64 field from the stream.
        /// </summary>
        public long ReadInt64()
        {
            var span = new ReadOnlySpan<byte>(buffer);
            return (long) ParsingPrimitivesClassic.ParseRawVarint64(ref span, ref state);
        }

        /// <summary>
        /// Reads an int32 field from the stream.
        /// </summary>
        public int ReadInt32()
        {
            var span = new ReadOnlySpan<byte>(buffer);
            return (int) ParsingPrimitivesClassic.ParseRawVarint32(ref span, ref state);
        }

        /// <summary>
        /// Reads a fixed64 field from the stream.
        /// </summary>
        public ulong ReadFixed64()
        {
            var span = new ReadOnlySpan<byte>(buffer);
            return ParsingPrimitivesClassic.ParseRawLittleEndian64(ref span, ref state);
        }

        /// <summary>
        /// Reads a fixed32 field from the stream.
        /// </summary>
        public uint ReadFixed32()
        {
            var span = new ReadOnlySpan<byte>(buffer);
            return ParsingPrimitivesClassic.ParseRawLittleEndian32(ref span, ref state);
        }

        /// <summary>
        /// Reads a bool field from the stream.
        /// </summary>
        public bool ReadBool()
        {
            var span = new ReadOnlySpan<byte>(buffer);
            return ParsingPrimitivesClassic.ParseRawVarint64(ref span, ref state) != 0;
        }

        /// <summary>
        /// Reads a string field from the stream.
        /// </summary>
        public string ReadString()
        {
            int length = ReadLength();
            // No need to read any data for an empty string.
            if (length == 0)
            {
                return "";
            }
            if (length <= state.bufferSize - state.bufferPos && length > 0)
            {
                // Fast path:  We already have the bytes in a contiguous buffer, so
                //   just copy directly from it.
                String result = CodedOutputStream.Utf8Encoding.GetString(buffer, state.bufferPos, length);
                state.bufferPos += length;
                return result;
            }
            // Slow path: Build a byte array first then copy it.
            return CodedOutputStream.Utf8Encoding.GetString(ReadRawBytes(length), 0, length);
        }

        /// <summary>
        /// Reads an embedded message field value from the stream.
        /// </summary>
        public void ReadMessage(IMessage builder)
        {
            int length = ReadLength();
            if (state.recursionDepth >= state.recursionLimit)
            {
                throw InvalidProtocolBufferException.RecursionLimitExceeded();
            }
            int oldLimit = PushLimit(length);
            ++state.recursionDepth;
            builder.MergeFrom(this);
            CheckReadEndOfStreamTag();
            // Check that we've read exactly as much data as expected.
            if (!ReachedLimit)
            {
                throw InvalidProtocolBufferException.TruncatedMessage();
            }
            --state.recursionDepth;
            PopLimit(oldLimit);
        }

        /// <summary>
        /// Reads an embedded group field from the stream.
        /// </summary>
        public void ReadGroup(IMessage builder)
        {
            if (state.recursionDepth >= state.recursionLimit)
            {
                throw InvalidProtocolBufferException.RecursionLimitExceeded();
            }
            ++state.recursionDepth;
            builder.MergeFrom(this);
            --state.recursionDepth;
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
            var span = new ReadOnlySpan<byte>(buffer);
            return ParsingPrimitivesClassic.ParseRawVarint32(ref span, ref state);
        }

        /// <summary>
        /// Reads an enum field value from the stream.
        /// </summary>   
        public int ReadEnum()
        {
            // Currently just a pass-through, but it's nice to separate it logically from WriteInt32.
            var span = new ReadOnlySpan<byte>(buffer);
            return (int) ParsingPrimitivesClassic.ParseRawVarint32(ref span, ref state);
        }

        /// <summary>
        /// Reads an sfixed32 field value from the stream.
        /// </summary>   
        public int ReadSFixed32()
        {
            var span = new ReadOnlySpan<byte>(buffer);
            return (int) ParsingPrimitivesClassic.ParseRawLittleEndian32(ref span, ref state);
        }

        /// <summary>
        /// Reads an sfixed64 field value from the stream.
        /// </summary>   
        public long ReadSFixed64()
        {
            var span = new ReadOnlySpan<byte>(buffer);
            return (long) ParsingPrimitivesClassic.ParseRawLittleEndian64(ref span, ref state);
        }

        /// <summary>
        /// Reads an sint32 field value from the stream.
        /// </summary>   
        public int ReadSInt32()
        {
            var span = new ReadOnlySpan<byte>(buffer);
            return DecodeZigZag32(ParsingPrimitivesClassic.ParseRawVarint32(ref span, ref state));
        }

        /// <summary>
        /// Reads an sint64 field value from the stream.
        /// </summary>   
        public long ReadSInt64()
        {
            var span = new ReadOnlySpan<byte>(buffer);
            return DecodeZigZag64(ParsingPrimitivesClassic.ParseRawVarint64(ref span, ref state));
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
            var span = new ReadOnlySpan<byte>(buffer);
            return (int) ParsingPrimitivesClassic.ParseRawVarint32(ref span, ref state);
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

        internal static float? ReadFloatWrapperLittleEndian(CodedInputStream input)
        {
            // length:1 + tag:1 + value:4 = 6 bytes
            if (input.state.bufferPos + 6 <= input.state.bufferSize)
            {
                // The entire wrapper message is already contained in `buffer`.
                int length = input.buffer[input.state.bufferPos];
                if (length == 0)
                {
                    input.state.bufferPos++;
                    return 0F;
                }
                // tag:1 + value:4 = length of 5 bytes
                // field=1, type=32-bit = tag of 13
                if (length != 5 || input.buffer[input.state.bufferPos + 1] != 13)
                {
                    return ReadFloatWrapperSlow(input);
                }
                var result = BitConverter.ToSingle(input.buffer, input.state.bufferPos + 2);
                input.state.bufferPos += 6;
                return result;
            }
            else
            {
                return ReadFloatWrapperSlow(input);
            }
        }

        internal static float? ReadFloatWrapperSlow(CodedInputStream input)
        {
            int length = input.ReadLength();
            if (length == 0)
            {
                return 0F;
            }
            int finalBufferPos = input.state.totalBytesRetired + input.state.bufferPos + length;
            float result = 0F;
            do
            {
                // field=1, type=32-bit = tag of 13
                if (input.ReadTag() == 13)
                {
                    result = input.ReadFloat();
                }
                else
                {
                    input.SkipLastField();
                }
            }
            while (input.state.totalBytesRetired + input.state.bufferPos < finalBufferPos);
            return result;
        }

        internal static double? ReadDoubleWrapperLittleEndian(CodedInputStream input)
        {
            // length:1 + tag:1 + value:8 = 10 bytes
            if (input.state.bufferPos + 10 <= input.state.bufferSize)
            {
                // The entire wrapper message is already contained in `buffer`.
                int length = input.buffer[input.state.bufferPos];
                if (length == 0)
                {
                    input.state.bufferPos++;
                    return 0D;
                }
                // tag:1 + value:8 = length of 9 bytes
                // field=1, type=64-bit = tag of 9
                if (length != 9 || input.buffer[input.state.bufferPos + 1] != 9)
                {
                    return ReadDoubleWrapperSlow(input);
                }
                var result = BitConverter.ToDouble(input.buffer, input.state.bufferPos + 2);
                input.state.bufferPos += 10;
                return result;
            }
            else
            {
                return ReadDoubleWrapperSlow(input);
            }
        }

        internal static double? ReadDoubleWrapperSlow(CodedInputStream input)
        {
            int length = input.ReadLength();
            if (length == 0)
            {
                return 0D;
            }
            int finalBufferPos = input.state.totalBytesRetired + input.state.bufferPos + length;
            double result = 0D;
            do
            {
                // field=1, type=64-bit = tag of 9
                if (input.ReadTag() == 9)
                {
                    result = input.ReadDouble();
                }
                else
                {
                    input.SkipLastField();
                }
            }
            while (input.state.totalBytesRetired + input.state.bufferPos < finalBufferPos);
            return result;
        }

        internal static bool? ReadBoolWrapper(CodedInputStream input)
        {
            return ReadUInt64Wrapper(input) != 0;
        }

        internal static uint? ReadUInt32Wrapper(CodedInputStream input)
        {
            // length:1 + tag:1 + value:5(varint32-max) = 7 bytes
            if (input.state.bufferPos + 7 <= input.state.bufferSize)
            {
                // The entire wrapper message is already contained in `buffer`.
                int pos0 = input.state.bufferPos;
                int length = input.buffer[input.state.bufferPos++];
                if (length == 0)
                {
                    return 0;
                }
                // Length will always fit in a single byte.
                if (length >= 128)
                {
                    input.state.bufferPos = pos0;
                    return ReadUInt32WrapperSlow(input);
                }
                int finalBufferPos = input.state.bufferPos + length;
                // field=1, type=varint = tag of 8
                if (input.buffer[input.state.bufferPos++] != 8)
                {
                    input.state.bufferPos = pos0;
                    return ReadUInt32WrapperSlow(input);
                }
                var result = input.ReadUInt32();
                // Verify this message only contained a single field.
                if (input.state.bufferPos != finalBufferPos)
                {
                    input.state.bufferPos = pos0;
                    return ReadUInt32WrapperSlow(input);
                }
                return result;
            }
            else
            {
                return ReadUInt32WrapperSlow(input);
            }
        }

        private static uint? ReadUInt32WrapperSlow(CodedInputStream input)
        {
            int length = input.ReadLength();
            if (length == 0)
            {
                return 0;
            }
            int finalBufferPos = input.state.totalBytesRetired + input.state.bufferPos + length;
            uint result = 0;
            do
            {
                // field=1, type=varint = tag of 8
                if (input.ReadTag() == 8)
                {
                    result = input.ReadUInt32();
                }
                else
                {
                    input.SkipLastField();
                }
            }
            while (input.state.totalBytesRetired + input.state.bufferPos < finalBufferPos);
            return result;
        }

        internal static int? ReadInt32Wrapper(CodedInputStream input)
        {
            return (int?)ReadUInt32Wrapper(input);
        }

        internal static ulong? ReadUInt64Wrapper(CodedInputStream input)
        {
            // field=1, type=varint = tag of 8
            const int expectedTag = 8;
            // length:1 + tag:1 + value:10(varint64-max) = 12 bytes
            if (input.state.bufferPos + 12 <= input.state.bufferSize)
            {
                // The entire wrapper message is already contained in `buffer`.
                int pos0 = input.state.bufferPos;
                int length = input.buffer[input.state.bufferPos++];
                if (length == 0)
                {
                    return 0L;
                }
                // Length will always fit in a single byte.
                if (length >= 128)
                {
                    input.state.bufferPos = pos0;
                    return ReadUInt64WrapperSlow(input);
                }
                int finalBufferPos = input.state.bufferPos + length;
                if (input.buffer[input.state.bufferPos++] != expectedTag)
                {
                    input.state.bufferPos = pos0;
                    return ReadUInt64WrapperSlow(input);
                }
                var result = input.ReadUInt64();
                // Verify this message only contained a single field.
                if (input.state.bufferPos != finalBufferPos)
                {
                    input.state.bufferPos = pos0;
                    return ReadUInt64WrapperSlow(input);
                }
                return result;
            }
            else
            {
                return ReadUInt64WrapperSlow(input);
            }
        }

        internal static ulong? ReadUInt64WrapperSlow(CodedInputStream input)
        {
            // field=1, type=varint = tag of 8
            const int expectedTag = 8;
            int length = input.ReadLength();
            if (length == 0)
            {
                return 0L;
            }
            int finalBufferPos = input.state.totalBytesRetired + input.state.bufferPos + length;
            ulong result = 0L;
            do
            {
                if (input.ReadTag() == expectedTag)
                {
                    result = input.ReadUInt64();
                }
                else
                {
                    input.SkipLastField();
                }
            }
            while (input.state.totalBytesRetired + input.state.bufferPos < finalBufferPos);
            return result;
        }

        internal static long? ReadInt64Wrapper(CodedInputStream input)
        {
            return (long?)ReadUInt64Wrapper(input);
        }

#endregion

        #region Underlying reading primitives

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

            RecomputeBufferSizeAfterLimit();

            return oldLimit;
        }

        private void RecomputeBufferSizeAfterLimit()
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

        /// <summary>
        /// Discards the current limit, returning the previous limit.
        /// </summary>
        internal void PopLimit(int oldLimit)
        {
            state.currentLimit = oldLimit;
            RecomputeBufferSizeAfterLimit();
        }

        /// <summary>
        /// Returns whether or not all the data before the limit has been read.
        /// </summary>
        /// <returns></returns>
        internal bool ReachedLimit
        {
            get
            {
                if (state.currentLimit == int.MaxValue)
                {
                    return false;
                }
                int currentAbsolutePosition = state.totalBytesRetired + state.bufferPos;
                return currentAbsolutePosition >= state.currentLimit;
            }
        }

        /// <summary>
        /// Returns true if the stream has reached the end of the input. This is the
        /// case if either the end of the underlying input source has been reached or
        /// the stream has reached a limit created using PushLimit.
        /// </summary>
        public bool IsAtEnd
        {
            get { return state.bufferPos == state.bufferSize && !RefillBuffer(false); }
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
            state.bufferSize = (input == null) ? 0 : input.Read(buffer, 0, buffer.Length);
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
                RecomputeBufferSizeAfterLimit();
                int totalBytesRead =
                    state.totalBytesRetired + state.bufferSize + state.bufferSizeAfterLimit;
                if (totalBytesRead < 0 || totalBytesRead > state.sizeLimit)
                {
                    throw InvalidProtocolBufferException.SizeLimitExceeded();
                }
                return true;
            }
        }

        /// <summary>
        /// Read one byte from the input.
        /// </summary>
        /// <exception cref="InvalidProtocolBufferException">
        /// the end of the stream or the current limit was reached
        /// </exception>
        internal byte ReadRawByte()
        {
            if (state.bufferPos == state.bufferSize)
            {
                RefillBuffer(true);
            }
            return buffer[state.bufferPos++];
        }

        /// <summary>
        /// Reads a fixed size of bytes from the input.
        /// </summary>
        /// <exception cref="InvalidProtocolBufferException">
        /// the end of the stream or the current limit was reached
        /// </exception>
        internal byte[] ReadRawBytes(int size)
        {
            if (size < 0)
            {
                throw InvalidProtocolBufferException.NegativeSize();
            }

            if (state.totalBytesRetired + state.bufferPos + size > state.currentLimit)
            {
                // Read to the end of the stream (up to the current limit) anyway.
                SkipRawBytes(state.currentLimit - state.totalBytesRetired - state.bufferPos);
                // Then fail.
                throw InvalidProtocolBufferException.TruncatedMessage();
            }

            if (size <= state.bufferSize - state.bufferPos)
            {
                // We have all the bytes we need already.
                byte[] bytes = new byte[size];
                ByteArray.Copy(buffer, state.bufferPos, bytes, 0, size);
                state.bufferPos += size;
                return bytes;
            }
            else if (size < buffer.Length)
            {
                // Reading more bytes than are in the buffer, but not an excessive number
                // of bytes.  We can safely allocate the resulting array ahead of time.

                // First copy what we have.
                byte[] bytes = new byte[size];
                int pos = state.bufferSize - state.bufferPos;
                ByteArray.Copy(buffer, state.bufferPos, bytes, 0, pos);
                state.bufferPos = state.bufferSize;

                // We want to use RefillBuffer() and then copy from the buffer into our
                // byte array rather than reading directly into our byte array because
                // the input may be unbuffered.
                RefillBuffer(true);

                while (size - pos > state.bufferSize)
                {
                    Buffer.BlockCopy(buffer, 0, bytes, pos, state.bufferSize);
                    pos += state.bufferSize;
                    state.bufferPos = state.bufferSize;
                    RefillBuffer(true);
                }

                ByteArray.Copy(buffer, 0, bytes, pos, size - pos);
                state.bufferPos = size - pos;

                return bytes;
            }
            else
            {
                // The size is very large.  For security reasons, we can't allocate the
                // entire byte array yet.  The size comes directly from the input, so a
                // maliciously-crafted message could provide a bogus very large size in
                // order to trick the app into allocating a lot of memory.  We avoid this
                // by allocating and reading only a small chunk at a time, so that the
                // malicious message must actually *be* extremely large to cause
                // problems.  Meanwhile, we limit the allowed size of a message elsewhere.

                // Remember the buffer markers since we'll have to copy the bytes out of
                // it later.
                int originalBufferPos = state.bufferPos;
                int originalBufferSize = state.bufferSize;

                // Mark the current buffer consumed.
                state.totalBytesRetired += state.bufferSize;
                state.bufferPos = 0;
                state.bufferSize = 0;

                // Read all the rest of the bytes we need.
                int sizeLeft = size - (originalBufferSize - originalBufferPos);
                List<byte[]> chunks = new List<byte[]>();

                while (sizeLeft > 0)
                {
                    byte[] chunk = new byte[Math.Min(sizeLeft, buffer.Length)];
                    int pos = 0;
                    while (pos < chunk.Length)
                    {
                        int n = (input == null) ? -1 : input.Read(chunk, pos, chunk.Length - pos);
                        if (n <= 0)
                        {
                            throw InvalidProtocolBufferException.TruncatedMessage();
                        }
                        state.totalBytesRetired += n;
                        pos += n;
                    }
                    sizeLeft -= chunk.Length;
                    chunks.Add(chunk);
                }

                // OK, got everything.  Now concatenate it all into one buffer.
                byte[] bytes = new byte[size];

                // Start by copying the leftover bytes from this.buffer.
                int newPos = originalBufferSize - originalBufferPos;
                ByteArray.Copy(buffer, originalBufferPos, bytes, 0, newPos);

                // And now all the chunks.
                foreach (byte[] chunk in chunks)
                {
                    Buffer.BlockCopy(chunk, 0, bytes, newPos, chunk.Length);
                    newPos += chunk.Length;
                }

                // Done.
                return bytes;
            }
        }

        /// <summary>
        /// Reads and discards <paramref name="size"/> bytes.
        /// </summary>
        /// <exception cref="InvalidProtocolBufferException">the end of the stream
        /// or the current limit was reached</exception>
        private void SkipRawBytes(int size)
        {
            if (size < 0)
            {
                throw InvalidProtocolBufferException.NegativeSize();
            }

            if (state.totalBytesRetired + state.bufferPos + size > state.currentLimit)
            {
                // Read to the end of the stream anyway.
                SkipRawBytes(state.currentLimit - state.totalBytesRetired - state.bufferPos);
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
                // Skipping more bytes than are in the buffer.  First skip what we have.
                int pos = state.bufferSize - state.bufferPos;

                // ROK 5/7/2013 Issue #54: should retire all bytes in buffer (bufferSize)
                // totalBytesRetired += pos;
                state.totalBytesRetired += state.bufferSize;
                
                state.bufferPos = 0;
                state.bufferSize = 0;

                // Then skip directly from the InputStream for the rest.
                if (pos < size)
                {
                    if (input == null)
                    {
                        throw InvalidProtocolBufferException.TruncatedMessage();
                    }
                    SkipImpl(size - pos);
                    state.totalBytesRetired += size - pos;
                }
            }
        }

        /// <summary>
        /// Abstraction of skipping to cope with streams which can't really skip.
        /// </summary>
        private void SkipImpl(int amountToSkip)
        {
            if (input.CanSeek)
            {
                long previousPosition = input.Position;
                input.Position += amountToSkip;
                if (input.Position != previousPosition + amountToSkip)
                {
                    throw InvalidProtocolBufferException.TruncatedMessage();
                }
            }
            else
            {
                byte[] skipBuffer = new byte[Math.Min(1024, amountToSkip)];
                while (amountToSkip > 0)
                {
                    int bytesRead = input.Read(skipBuffer, 0, Math.Min(skipBuffer.Length, amountToSkip));
                    if (bytesRead <= 0)
                    {
                        throw InvalidProtocolBufferException.TruncatedMessage();
                    }
                    amountToSkip -= bytesRead;
                }
            }
        }

        private void RefillBufferHandler(ref ReadOnlySpan<byte> spanBuffer, ref ParserInternalState state)
        {
            RefillBuffer(true);
            spanBuffer = new ReadOnlySpan<byte>(this.buffer);
        }
#endregion
    }
}
