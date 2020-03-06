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

#if GOOGLE_PROTOBUF_SUPPORT_SYSTEM_MEMORY
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
    /// Reads and decodes protocol message fields.
    /// Note: experimental API that can change or be removed without any prior notice.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This class is generally used by generated code to read appropriate
    /// primitives from the input. It effectively encapsulates the lowest
    /// levels of protocol buffer format.
    /// </para>
    /// <para>
    /// Repeated fields and map fields are not handled by this class; use <see cref="RepeatedField{T}"/>
    /// and <see cref="MapField{TKey, TValue}"/> to serialize such fields.
    /// </para>
    /// </remarks>
    [SecuritySafeCritical]
    public ref struct CodedInputReader
    {
        internal const int DefaultRecursionLimit = 100;
        internal const int DefaultSizeLimit = Int32.MaxValue;

        // TODO: make the fields private
        internal ReadOnlySpan<byte> buffer;
        internal  ParserInternalState state;
        
        //private SequenceReader<byte> reader;
        //private uint lastTag;
        //private int recursionDepth;
        //private int currentLimit;
        //private Decoder decoder;

        //private readonly int recursionLimit;


        /// <summary>
        /// Creates a new CodedInputReader reading data from the given <see cref="ReadOnlySequence{T}"/>.
        /// </summary>
        public CodedInputReader(ReadOnlySequence<byte> input) : this(input, DefaultRecursionLimit)
        {
        }

        internal CodedInputReader(ReadOnlySequence<byte> input, int recursionLimit)
        {
            //this.reader = new SequenceReader<byte>(input);
            this.buffer = default;  // start with empty span: //TODO: that causes unnecessary slowdown....
            this.state = default;
            this.state.bufferPos = 0;
            this.state.bufferSize = 0;  // the very first step is going to be refilling the buffer?
            this.state.lastTag = 0;
            this.state.recursionDepth = 0;
            this.state.sizeLimit = DefaultSizeLimit;
            this.state.recursionLimit = recursionLimit;
            this.state.currentLimit = int.MaxValue;
            this.state.refillBufferHelper = new RefillBufferHelper(input);
            this.state.codedInputStream = null;

            //this.decoder = null;
            this.state.DiscardUnknownFields = false;
            this.state.ExtensionRegistry = null;

            // TODO: reading wrappers won't work without this.
            //this.state.skipLastFieldAction = () => { SkipLastField(); };
        }

        internal CodedInputReader(ref ReadOnlySpan<byte> buffer, ref ParserInternalState state)
        {
            this.buffer = buffer;
            this.state = state;
        }

        /// <summary>
        /// The total number of bytes processed by the reader.
        /// </summary>
        public long Position {

            get
            {
                return state.totalBytesRetired + state.bufferPos;
            }
        }

        /// <summary>
        /// Returns true if the reader has reached the end of the input. This is the
        /// case if either the end of the underlying input source has been reached or
        /// the reader has reached a limit created using PushLimit.
        /// </summary>
        public bool IsAtEnd
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get {  return RefillBufferHelper.IsAtEnd(ref buffer, ref state); }
        }

        /// <summary>
        /// Returns the last tag read, or 0 if no tags have been read or we've read beyond
        /// the end of the input.
        /// </summary>
        internal uint LastTag { get { return state.lastTag; } }

        /// <summary>
        /// Internal-only property; when set to true, unknown fields will be discarded while parsing.
        /// </summary>
        internal bool DiscardUnknownFields {
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
        /// Creates a <see cref="CodedInputReader"/> with the specified recursion limits, reading
        /// from the input.
        /// </summary>
        /// <remarks>
        /// This method exists separately from the constructor to reduce the number of constructor overloads.
        /// It is likely to be used considerably less frequently than the constructors, as the default limits
        /// are suitable for most use cases.
        /// </remarks>
        /// <param name="input">The input to read from</param>
        /// <param name="recursionLimit">The maximum recursion depth to allow while reading.</param>
        /// <returns>A <c>CodedInputReader</c> reading from <paramref name="input"/> with the specified limits.</returns>
        public static CodedInputReader CreateWithLimits(ReadOnlySequence<byte> input, int recursionLimit)
        {
            return new CodedInputReader(input, recursionLimit);
        }

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

            // uint previousTag = lastTag;
            // long consumed = reader.Consumed;

            // uint tag = ReadTag();

            // long rewindCount = reader.Consumed - consumed;
            // if (rewindCount > 0)
            // {
            //     reader.Rewind(rewindCount);
            // }
            // lastTag = previousTag;

            // return tag;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowInvalidTagException()
        {
            throw InvalidProtocolBufferException.InvalidTag();
        }

        /// <summary>
        /// Reads a field tag, returning the tag of 0 for "end of input".
        /// </summary>
        /// <remarks>
        /// If this method returns 0, it doesn't necessarily mean the end of all
        /// the data in this CodedInputReader; it may be the end of the logical input
        /// for an embedded message, for example.
        /// </remarks>
        /// <returns>The next field tag, or 0 for end of input. (0 is never a valid tag.)</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]

        public uint ReadTag()
        {
            // TODO: looks like we need a different variant of ParseTag here???

            return RefillBufferHelper.ParseTag(ref buffer, ref state);
            // if (ReachedLimit)
            // {
            //     lastTag = 0;
            //     return 0;
            // }

            // // Optimize for common case of a 2 byte tag that is in the current span
            // var current = LimitedUnreadSpan;
            // if (current.Length >= 2)
            // {
            //     int tmp = current[0];
            //     if (tmp < 128)
            //     {
            //         lastTag = (uint)tmp;
            //         reader.Advance(1);
            //     }
            //     else
            //     {
            //         int result = tmp & 0x7f;
            //         if ((tmp = current[1]) < 128)
            //         {
            //             result |= tmp << 7;
            //             lastTag = (uint)result;
            //             reader.Advance(2);
            //         }
            //         else
            //         {
            //             // Nope, go the potentially slow route.
            //             lastTag = ReadRawVarint32();
            //         }
            //     }
            // }
            // else
            // {
            //     if (IsAtEnd)
            //     {
            //         lastTag = 0;
            //         return 0;
            //     }

            //     lastTag = ReadRawVarint32();
            // }

            // if (WireFormat.GetTagFieldNumber(lastTag) == 0)
            // {
            //     // If we actually read a tag with a field of 0, that's not a valid tag.
            //     ThrowInvalidTagException();
            // }

            // return lastTag;
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
        /// <exception cref="InvalidOperationException">The last read operation read to the end of the logical input</exception>
        public void SkipLastField()
        {
            RefillBufferHelper.SkipLastField(ref buffer, ref state);
        }

        private void SkipRawBytes(int length)
        {
            ParsingPrimitivesClassic.SkipRawBytes(ref buffer, ref state, length);

            // if (length < 0)
            // {
            //     throw InvalidProtocolBufferException.NegativeSize();
            // }

            // CheckRequestedDataAvailable(length);

            // reader.Advance(length);
        }

        /// <summary>
        /// Skip a group.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SkipGroup(uint startGroupTag)
        {
            RefillBufferHelper.SkipGroup(ref buffer, ref state, startGroupTag);
        }

        /// <summary>
        /// Reads a double field from the input.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double ReadDouble()
        {
            return ParsingPrimitivesClassic.ParseDouble(ref buffer, ref state);
        }

        /// <summary>
        /// Reads a float field from the input.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float ReadFloat()
        {
            return ParsingPrimitivesClassic.ParseFloat(ref buffer, ref state);
        }

        /// <summary>
        /// Reads a uint64 field from the input.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong ReadUInt64()
        {
            return ParsingPrimitivesClassic.ParseRawVarint64(ref buffer, ref state);
        }

        /// <summary>
        /// Reads an int64 field from the input.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long ReadInt64()
        {
            return (long)ParsingPrimitivesClassic.ParseRawVarint64(ref buffer, ref state);
        }

        /// <summary>
        /// Reads an int32 field from the input.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadInt32()
        {
            return (int)ParsingPrimitivesClassic.ParseRawVarint32(ref buffer, ref state);
        }

        /// <summary>
        /// Reads a fixed64 field from the input.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong ReadFixed64()
        {
            return ParsingPrimitivesClassic.ParseRawLittleEndian64(ref buffer, ref state);
        }

        /// <summary>
        /// Reads a fixed32 field from the input.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ReadFixed32()
        {
            return ParsingPrimitivesClassic.ParseRawLittleEndian32(ref buffer, ref state);
        }

        /// <summary>
        /// Reads a bool field from the input.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ReadBool()
        {
            return ParsingPrimitivesClassic.ParseRawVarint64(ref buffer, ref state) != 0;
        }

        /// <summary>
        /// Reads a string field from the input.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public string ReadString()
        {
            int length = ReadLength();
            return ParsingPrimitivesClassic.ReadRawString(ref buffer, ref state, length);

//             int length = ReadLength();

//             if (length == 0)
//             {
//                 return string.Empty;
//             }

//             if (length < 0)
//             {
//                 throw InvalidProtocolBufferException.NegativeSize();
//             }

// #if GOOGLE_PROTOBUF_SUPPORT_FAST_STRING
//             ReadOnlySpan<byte> unreadSpan = LimitedUnreadSpan;
//             if (unreadSpan.Length >= length)
//             {
//                 // Fast path: all bytes to decode appear in the same span.
//                 ReadOnlySpan<byte> data = unreadSpan.Slice(0, length);

//                 string value;
//                 unsafe
//                 {
//                     fixed (byte* sourceBytes = &MemoryMarshal.GetReference(data))
//                     {
//                         value = CodedOutputStream.Utf8Encoding.GetString(sourceBytes, length);
//                     }
//                 }

//                 reader.Advance(length);
//                 return value;
//             }
// #endif

//             return ReadStringSlow(length);
        }

        // /// <summary>
        // /// Reads a string assuming that it is spread across multiple spans in the <see cref="ReadOnlySequence{T}"/>.
        // /// </summary>
        // /// <param name="byteLength">The length of the string to be decoded, in bytes.</param>
        // /// <returns>The decoded string.</returns>
        // private string ReadStringSlow(int byteLength)
        // {
        //     CheckRequestedDataAvailable(byteLength);

        //     if (decoder == null)
        //     {
        //         decoder = CodedOutputStream.Utf8Encoding.GetDecoder();
        //     }

        //     // We need to decode bytes incrementally across multiple spans.
        //     int maxCharLength = CodedOutputStream.Utf8Encoding.GetMaxCharCount(byteLength);
        //     char[] charArray = ArrayPool<char>.Shared.Rent(maxCharLength);

        //     try
        //     {
        //         int remainingByteLength = byteLength;
        //         int initializedChars = 0;
        //         while (remainingByteLength > 0)
        //         {
        //             var unreadSpan = LimitedUnreadSpan;
        //             int bytesRead = Math.Min(remainingByteLength, unreadSpan.Length);
        //             remainingByteLength -= bytesRead;
        //             bool flush = remainingByteLength == 0;

        //             unsafe
        //             {
        //                 fixed (byte* pUnreadSpan = &MemoryMarshal.GetReference(unreadSpan))
        //                 fixed (char* pCharArray = &charArray[initializedChars])
        //                 {
        //                     initializedChars += decoder.GetChars(pUnreadSpan, bytesRead, pCharArray, charArray.Length - initializedChars, flush);
        //                 }

        //                 reader.Advance(bytesRead);
        //             }
        //         }

        //         string value = new string(charArray, 0, initializedChars);
        //         return value;
        //     }
        //     finally
        //     {
        //         ArrayPool<char>.Shared.Return(charArray);
        //     }
        // }

        /// <summary>
        /// Reads an embedded message field value from the input.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ReadMessage(IMessage message)
        {
            // TODO: add a fallback if IMessage does not implement IBufferMessage 
            RefillBufferHelper.ReadMessage(ref this, message);


            // int length = ReadLength();
            // if (state.recursionDepth >= state.recursionLimit)
            // {
            //     throw InvalidProtocolBufferException.RecursionLimitExceeded();
            // }
            // int oldLimit = PushLimit(length);
            // ++state.recursionDepth;
            // builder.MergeFrom(ref this);
            // CheckReadEndOfInputTag();
            // // Check that we've read exactly as much data as expected.
            // if (!ReachedLimit)
            // {
            //     throw InvalidProtocolBufferException.TruncatedMessage();
            // }
            // --state.recursionDepth;
            // PopLimit(oldLimit);
        }

        /// <summary>
        /// Reads an embedded group field from the input.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ReadGroup(IMessage message)
        {
            RefillBufferHelper.ReadGroup(ref this, message);

            // if (state.recursionDepth >= state.recursionLimit)
            // {
            //     throw InvalidProtocolBufferException.RecursionLimitExceeded();
            // }
            // ++state.recursionDepth;
            // builder.MergeFrom(ref this);
            // --state.recursionDepth;
        }

        /// <summary>
        /// Reads a bytes field value from the input.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ByteString ReadBytes()
        {
            int length = ReadLength();
            return ByteString.AttachBytes(ParsingPrimitivesClassic.ReadRawBytes(ref buffer, ref state, length));

            // if (length == 0)
            // {
            //     return ByteString.Empty;
            // }

            // if (length < 0)
            // {
            //     throw InvalidProtocolBufferException.NegativeSize();
            // }

            // CheckRequestedDataAvailable(length);

            // // Avoid creating a copy of Sequence if data is on current span
            // var unreadSpan = LimitedUnreadSpan;
            // var data = (unreadSpan.Length >= length)
            //     ? unreadSpan.Slice(0, length).ToArray()
            //     : reader.Sequence.Slice(reader.Position, length).ToArray();

            // reader.Advance(length);

            
        }

        /// <summary>
        /// Reads a uint32 field value from the input.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint ReadUInt32()
        {
            return ParsingPrimitivesClassic.ParseRawVarint32(ref buffer, ref state);
        }

        /// <summary>
        /// Reads an enum field value from the input.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadEnum()
        {
            // Currently just a pass-through, but it's nice to separate it logically from WriteInt32.
            return (int)ParsingPrimitivesClassic.ParseRawVarint32(ref buffer, ref state);
        }

        /// <summary>
        /// Reads an sfixed32 field value from the input.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadSFixed32()
        {
            return (int)ParsingPrimitivesClassic.ParseRawLittleEndian32(ref buffer, ref state);
        }

        /// <summary>
        /// Reads an sfixed64 field value from the input.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long ReadSFixed64()
        {
            return (long)ParsingPrimitivesClassic.ParseRawLittleEndian64(ref buffer, ref state);
        }

        /// <summary>
        /// Reads an sint32 field value from the input.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadSInt32()
        {
            return CodedInputStream.DecodeZigZag32(ParsingPrimitivesClassic.ParseRawVarint32(ref buffer, ref state));
        }

        /// <summary>
        /// Reads an sint64 field value from the input.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long ReadSInt64()
        {
            return CodedInputStream.DecodeZigZag64(ParsingPrimitivesClassic.ParseRawVarint64(ref buffer, ref state));
        }

        /// <summary>
        /// Reads a length for length-delimited data.
        /// </summary>
        /// <remarks>
        /// This is internally just reading a varint, but this method exists
        /// to make the calling code clearer.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadLength()
        {
            return (int)ParsingPrimitivesClassic.ParseRawVarint32(ref buffer, ref state);
        }

        /// <summary>
        /// Peeks at the next tag in the input. If it matches <paramref name="tag"/>,
        /// the tag is consumed and the method returns <c>true</c>; otherwise, the
        /// input is left in the original position and the method returns <c>false</c>.
        /// </summary>
        public bool MaybeConsumeTag(uint tag)
        {
            if (PeekTag() == tag)
            {
                state.hasNextTag = false;
                return true;
            }
            return false;

            // uint previousTag = state.lastTag;
            // long consumed = reader.Consumed;

            // uint newTag = ReadTag();
            // if (newTag == tag)
            // {
            //     // Match so consume tag
            //     return true;
            // }

            // // No match so rewind
            // long rewindCount = reader.Consumed - consumed;
            // if (rewindCount > 0)
            // {
            //     reader.Rewind(rewindCount);
            // }
            // lastTag = previousTag;

            // return false;
        }

        internal static float? ReadFloatWrapperLittleEndian(ref CodedInputReader input)
        {
            return ParsingPrimitivesWrappers.ReadFloatWrapperLittleEndian(ref input.buffer, ref input.state);
        }

        internal static float? ReadFloatWrapperSlow(ref CodedInputReader input)
        {
            return ParsingPrimitivesWrappers.ReadFloatWrapperSlow(ref input.buffer, ref input.state);
        }

        internal static double? ReadDoubleWrapperLittleEndian(ref CodedInputReader input)
        {
            return ParsingPrimitivesWrappers.ReadDoubleWrapperLittleEndian(ref input.buffer, ref input.state);
        }

        internal static double? ReadDoubleWrapperSlow(ref CodedInputReader input)
        {
            return ParsingPrimitivesWrappers.ReadDoubleWrapperSlow(ref input.buffer, ref input.state);
        }

        internal static bool? ReadBoolWrapper(ref CodedInputReader input)
        {
            return ParsingPrimitivesWrappers.ReadUInt64Wrapper(ref input.buffer, ref input.state) != 0;
        }

        internal static uint? ReadUInt32Wrapper(ref CodedInputReader input)
        {
            return ParsingPrimitivesWrappers.ReadUInt32Wrapper(ref input.buffer, ref input.state);
        }

        private static uint? ReadUInt32WrapperSlow(ref CodedInputReader input)
        {
            return ParsingPrimitivesWrappers.ReadUInt32WrapperSlow(ref input.buffer, ref input.state);
        }

        internal static int? ReadInt32Wrapper(ref CodedInputReader input)
        {
            return (int?)ParsingPrimitivesWrappers.ReadUInt32Wrapper(ref input.buffer, ref input.state);
        }

        internal static ulong? ReadUInt64Wrapper(ref CodedInputReader input)
        {
            return ParsingPrimitivesWrappers.ReadUInt64Wrapper(ref input.buffer, ref input.state);
        }

        internal static ulong? ReadUInt64WrapperSlow(ref CodedInputReader input)
        {
            return ParsingPrimitivesWrappers.ReadUInt64WrapperSlow(ref input.buffer, ref input.state);
        }

        internal static long? ReadInt64Wrapper(ref CodedInputReader input)
        {
            return (long?)ParsingPrimitivesWrappers.ReadUInt64Wrapper(ref input.buffer, ref input.state);
        }

        #endregion

        #region Underlying reading primitives

        // /// <summary>
        // /// Same code as ReadRawVarint32, but read each byte from reader individually
        // /// </summary>
        // private uint SlowReadRawVarint32()
        // {
        //     byte value = ReadByteSlow();
        //     int tmp = value;
        //     if (tmp < 128)
        //     {
        //         return (uint)tmp;
        //     }
        //     int result = tmp & 0x7f;
        //     value = ReadByteSlow();
        //     tmp = value;
        //     if (tmp < 128)
        //     {
        //         result |= tmp << 7;
        //     }
        //     else
        //     {
        //         result |= (tmp & 0x7f) << 7;
        //         value = ReadByteSlow();
        //         tmp = value;
        //         if (tmp < 128)
        //         {
        //             result |= tmp << 14;
        //         }
        //         else
        //         {
        //             result |= (tmp & 0x7f) << 14;
        //             value = ReadByteSlow();
        //             tmp = value;
        //             if (tmp < 128)
        //             {
        //                 result |= tmp << 21;
        //             }
        //             else
        //             {
        //                 result |= (tmp & 0x7f) << 21;
        //                 value = ReadByteSlow();
        //                 tmp = value;
        //                 result |= tmp << 28;
        //                 if (tmp >= 128)
        //                 {
        //                     // Discard upper 32 bits.
        //                     // Note that this has to use ReadByteSlow() as we only ensure we've
        //                     // got at least 5 bytes at the start of the method. This lets us
        //                     // use the fast path in more cases, and we rarely hit this section of code.
        //                     for (int i = 0; i < 5; i++)
        //                     {
        //                         value = ReadByteSlow();
        //                         tmp = value;
        //                         if (tmp < 128)
        //                         {
        //                             return (uint)result;
        //                         }
        //                     }
        //                     throw InvalidProtocolBufferException.MalformedVarint();
        //                 }
        //             }
        //         }
        //     }
        //     return (uint)result;
        // }

        // /// <summary>
        // /// Reads a raw Varint from the input. If larger than 32 bits, discard the upper bits.
        // /// This method is optimised for the case where we've got lots of data in the buffer.
        // /// That means we can check the size just once, then just read directly from the buffer
        // /// without constant rechecking of the buffer length.
        // /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal uint ReadRawVarint32()
        {
            return ParsingPrimitivesClassic.ParseRawVarint32(ref buffer, ref state);
        }
        //     var current = LimitedUnreadSpan;

        //     if (current.Length < 5)
        //     {
        //         return SlowReadRawVarint32();
        //     }

        //     int bufferPos = 0;
        //     int tmp = current[bufferPos++];
        //     if (tmp < 128)
        //     {
        //         reader.Advance(bufferPos);
        //         return (uint)tmp;
        //     }
        //     int result = tmp & 0x7f;
        //     if ((tmp = current[bufferPos++]) < 128)
        //     {
        //         result |= tmp << 7;
        //     }
        //     else
        //     {
        //         result |= (tmp & 0x7f) << 7;
        //         if ((tmp = current[bufferPos++]) < 128)
        //         {
        //             result |= tmp << 14;
        //         }
        //         else
        //         {
        //             result |= (tmp & 0x7f) << 14;
        //             if ((tmp = current[bufferPos++]) < 128)
        //             {
        //                 result |= tmp << 21;
        //             }
        //             else
        //             {
        //                 result |= (tmp & 0x7f) << 21;
        //                 result |= (tmp = current[bufferPos++]) << 28;
        //                 if (tmp >= 128)
        //                 {
        //                     reader.Advance(bufferPos);

        //                     // Discard upper 32 bits.
        //                     // Note that this has to use ReadByteSlow() as we only ensure we've
        //                     // got at least 5 bytes at the start of the method. This lets us
        //                     // use the fast path in more cases, and we rarely hit this section of code.
        //                     for (int i = 0; i < 5; i++)
        //                     {
        //                         var value = ReadByteSlow();
        //                         tmp = value;
        //                         if (tmp < 128)
        //                         {
        //                             return (uint)result;
        //                         }
        //                     }
        //                     throw InvalidProtocolBufferException.MalformedVarint();
        //                 }
        //             }
        //         }
        //     }
        //     reader.Advance(bufferPos);
        //     return (uint)result;
        // }

        // /// <summary>
        // /// Same code as ReadRawVarint64, but read each byte from reader individually
        // /// </summary>
        // private ulong SlowReadRawVarint64()
        // {
        //     int shift = 0;
        //     ulong result = 0;
        //     while (shift < 64)
        //     {
        //         byte b = ReadByteSlow();
        //         result |= (ulong)(b & 0x7F) << shift;
        //         if ((b & 0x80) == 0)
        //         {
        //             return result;
        //         }
        //         shift += 7;
        //     }
        //     throw InvalidProtocolBufferException.MalformedVarint();
        // }

        // /// <summary>
        // /// Reads a raw varint from the input.
        // /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ulong ReadRawVarint64()
        {
            return ParsingPrimitivesClassic.ParseRawVarint64(ref buffer, ref state);
        }
        //     var current = LimitedUnreadSpan;

        //     if (current.Length < 10)
        //     {
        //         return SlowReadRawVarint64();
        //     }

        //     int bufferPos = 0;
        //     ulong result = current[bufferPos++];
        //     if (result < 128)
        //     {
        //         reader.Advance(bufferPos);
        //         return result;
        //     }
        //     result &= 0x7f;
        //     int shift = 7;
        //     do
        //     {
        //         byte b = current[bufferPos++];
        //         result |= (ulong)(b & 0x7F) << shift;
        //         if (b < 0x80)
        //         {
        //             reader.Advance(bufferPos);
        //             return result;
        //         }
        //         shift += 7;
        //     }
        //     while (shift < 64);

        //     throw InvalidProtocolBufferException.MalformedVarint();
        // }

        // /// <summary>
        // /// Reads a 32-bit little-endian integer from the input.
        // /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal uint ReadRawLittleEndian32()
        {
            return ParsingPrimitivesClassic.ParseRawLittleEndian32(ref buffer, ref state);
        }
        //     const int length = 4;

        //     ReadOnlySpan<byte> current = LimitedUnreadSpan;
        //     if (current.Length >= length)
        //     {
        //         // Fast path. All data is in the current span.
        //         reader.Advance(length);

        //         return BinaryPrimitives.ReadUInt32LittleEndian(current);
        //     }
        //     else
        //     {
        //         return ReadRawLittleEndian32Slow();
        //     }
        // }

        // private unsafe uint ReadRawLittleEndian32Slow()
        // {
        //     const int length = 4;

        //     byte* buffer = stackalloc byte[length];
        //     Span<byte> tempSpan = new Span<byte>(buffer, length);

        //     CopyToSlow(tempSpan);
        //     reader.Advance(length);

        //     return BinaryPrimitives.ReadUInt32LittleEndian(tempSpan);
        // }

        // /// <summary>
        // /// Reads a 64-bit little-endian integer from the input.
        // /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal unsafe ulong ReadRawLittleEndian64()
        {
            return ParsingPrimitivesClassic.ParseRawLittleEndian64(ref buffer, ref state);
        }
        //     const int length = 8;

        //     ReadOnlySpan<byte> current = LimitedUnreadSpan;
        //     if (current.Length >= length)
        //     {
        //         // Fast path. All data is in the current span.
        //         reader.Advance(length);

        //         return BinaryPrimitives.ReadUInt64LittleEndian(current);
        //     }
        //     else
        //     {
        //         return ReadRawLittleEndian64Slow();
        //     }
        // }

        // private unsafe ulong ReadRawLittleEndian64Slow()
        // {
        //     const int length = 8;

        //     byte* buffer = stackalloc byte[length];
        //     Span<byte> tempSpan = new Span<byte>(buffer, length);

        //     CopyToSlow(tempSpan);
        //     reader.Advance(length);

        //     return BinaryPrimitives.ReadUInt64LittleEndian(tempSpan);
        // }
        #endregion

        /// <summary>
        /// Sets currentLimit to (current position) + byteLimit. This is called
        /// when descending into a length-delimited embedded message. The previous
        /// limit is returned.
        /// </summary>
        /// <returns>The old limit.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal int PushLimit(int byteLimit)
        {
            return RefillBufferHelper.PushLimit(ref state, byteLimit);
        }

        /// <summary>
        /// Discards the current limit, returning the previous limit.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return RefillBufferHelper.IsReachedLimit(ref state);
            }
        }

        /// <summary>
        /// Verifies that the last call to ReadTag() returned tag 0 - in other words,
        /// we've reached the end of the input when we expected to.
        /// </summary>
        /// <exception cref="InvalidProtocolBufferException">The 
        /// tag read was not the one specified</exception>
        internal void CheckReadEndOfInputTag()
        {
            if (state.lastTag != 0)
            {
                throw InvalidProtocolBufferException.MoreDataAvailable();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ThrowEndOfInputIfFalse(bool condition)
        {
            if (!condition)
            {
                ThrowEndOfInput();
                return;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowEndOfInput()
        {
            throw InvalidProtocolBufferException.TruncatedMessage();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowTruncatedMessage()
        {
            throw InvalidProtocolBufferException.TruncatedMessage();
        }

        // private byte ReadByteSlow()
        // {
        //     ThrowEndOfInputIfFalse(reader.TryRead(out byte b));

        //     if (reader.Consumed > currentLimit)
        //     {
        //         ThrowTruncatedMessage();
        //     }

        //     return b;
        // }

        // private void CheckRequestedDataAvailable(int length)
        // {
        //     if (length + reader.Consumed > currentLimit)
        //     {
        //         // Read to the end of the limit.
        //         reader.Advance(Math.Min(currentLimit, reader.Remaining));
        //         // Then fail.
        //         ThrowTruncatedMessage();
        //     }
        //     if (reader.Remaining < length)
        //     {
        //         // Read to the end of the content.
        //         reader.Advance(reader.Remaining);
        //         // Then fail.
        //         ThrowEndOfInput();
        //     }
        // }

        // private void CopyToSlow(Span<byte> destination)
        // {
        //     CheckRequestedDataAvailable(destination.Length);

        //     reader.TryCopyTo(destination);
        // }

        // private ReadOnlySpan<byte> LimitedUnreadSpan
        // {
        //     [MethodImpl(MethodImplOptions.AggressiveInlining)]
        //     get
        //     {
        //         // Get the current unread span content. This content is limited to content for the current message.
        //         // When all the content we want to read is within the span's length then we can go down a fast path.
        //         return reader.CurrentSpan.Slice(reader.CurrentSpanIndex, Math.Min(currentLimit, reader.CurrentSpan.Length) - reader.CurrentSpanIndex);
        //     }
        // }
    }
}
#endif