#region Copyright notice and license
// Protocol Buffers - Google's data interchange format
// Copyright 2019 Google Inc.  All rights reserved.
// https://github.com/protocolbuffers/protobuf
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

using BenchmarkDotNet.Attributes;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using pb = global::Google.Protobuf;
using pbc = global::Google.Protobuf.Collections;
using pbr = global::Google.Protobuf.Reflection;
using scg = global::System.Collections.Generic;

namespace Google.Protobuf.Benchmarks
{
    /// <summary>
    /// Benchmark that tests serialization/deserialization of wrapper fields.
    /// </summary>
    [MemoryDiagnoser]
    public class CppLikeBenchmark
    {
        const int IterationCount = 1000;
        byte[] manyPrimitiveFieldsByteArray;
        ReadOnlySequence<byte> manyPrimitiveFieldsReadOnlySequence;
        
        [GlobalSetup]
        public void GlobalSetup()
        {
            manyPrimitiveFieldsByteArray = CreateManyPrimitiveFieldsMessage().ToByteArray();

            MemoryStream memoryStream = new MemoryStream();
            for (int i = 0; i < IterationCount; i++)
            {
                foreach (byte b in manyPrimitiveFieldsByteArray)
                {
                    memoryStream.WriteByte(b);
                }
            }

            manyPrimitiveFieldsReadOnlySequence = new ReadOnlySequence<byte>(memoryStream.ToArray());
        }

        [Benchmark]
        public ManyPrimitiveFieldsMessage Parse_CurrentApproach()
        {
            var input = new CodedInputReader(manyPrimitiveFieldsReadOnlySequence);
            var msg = new ManyPrimitiveFieldsMessage();
            for (int i = 0; i < IterationCount; i++)
            {
                msg.MergeFrom(ref input);
            }
            return msg;
        }

        [Benchmark]
        public ManyPrimitiveFieldsMessage Parse_StateInLocalVariablesApproach()
        {
            var input = new CodedInputReader(manyPrimitiveFieldsReadOnlySequence);
            var msg = new ManyPrimitiveFieldsMessage();
            for (int i = 0; i < IterationCount; i++)
            {
                msg.MergeFrom(ref input);
            }
            return msg;
        }

        

        private static ManyPrimitiveFieldsMessage CreateManyPrimitiveFieldsMessage()
        {
            // Example data match data of an internal benchmarks
            return new ManyPrimitiveFieldsMessage()
            {
                Int64Field19 = 123,
                Int64Field37 = 1000032,
                Int64Field26 = 3453524500,
                DoubleField79 = 1.2,
                DoubleField25 = 234,
                DoubleField9 = 123.3,
                DoubleField28 = 23,
                DoubleField7 = 234,
                DoubleField50 = 2.45
            };
        }
    }




    public sealed partial class ManyPrimitiveFieldsMessage {

    public void MergeFrom2(ref pb::CodedInputReader input) {
      uint tag;
      while ((tag = input.ReadTag()) != 0) {
        switch(tag) {
          default:
            _unknownFields = pb::UnknownFieldSet.MergeFieldFrom(_unknownFields, ref input);
            break;
          case 9: {
            DoubleField1 = input.ReadDouble();
            break;
          }
          case 16: {
            Int64Field2 = input.ReadInt64();
            break;
          }
          case 24: {
            Int64Field3 = input.ReadInt64();
            break;
          }
          case 32: {
            Int64Field4 = input.ReadInt64();
            break;
          }
          case 57: {
            DoubleField7 = input.ReadDouble();
            break;
          }
          case 65: {
            DoubleField8 = input.ReadDouble();
            break;
          }
          case 73: {
            DoubleField9 = input.ReadDouble();
            break;
          }
          case 81: {
            DoubleField10 = input.ReadDouble();
            break;
          }
          case 89: {
            DoubleField11 = input.ReadDouble();
            break;
          }
          case 113: {
            DoubleField14 = input.ReadDouble();
            break;
          }
          case 121: {
            DoubleField15 = input.ReadDouble();
            break;
          }
          case 152: {
            Int64Field19 = input.ReadInt64();
            break;
          }
          case 161: {
            DoubleField20 = input.ReadDouble();
            break;
          }
          case 169: {
            DoubleField21 = input.ReadDouble();
            break;
          }
          case 177: {
            DoubleField22 = input.ReadDouble();
            break;
          }
          case 201: {
            DoubleField25 = input.ReadDouble();
            break;
          }
          case 208: {
            Int64Field26 = input.ReadInt64();
            break;
          }
          case 225: {
            DoubleField28 = input.ReadDouble();
            break;
          }
          case 233: {
            DoubleField29 = input.ReadDouble();
            break;
          }
          case 241: {
            DoubleField30 = input.ReadDouble();
            break;
          }
          case 249: {
            DoubleField31 = input.ReadDouble();
            break;
          }
          case 256: {
            Int64Field32 = input.ReadInt64();
            break;
          }
          case 296: {
            Int64Field37 = input.ReadInt64();
            break;
          }
          case 305: {
            DoubleField38 = input.ReadDouble();
            break;
          }
          case 312: {
            Interactions = input.ReadInt64();
            break;
          }
          case 321: {
            DoubleField40 = input.ReadDouble();
            break;
          }
          case 328: {
            Int64Field41 = input.ReadInt64();
            break;
          }
          case 337: {
            DoubleField42 = input.ReadDouble();
            break;
          }
          case 344: {
            Int64Field43 = input.ReadInt64();
            break;
          }
          case 352: {
            Int64Field44 = input.ReadInt64();
            break;
          }
          case 361: {
            DoubleField45 = input.ReadDouble();
            break;
          }
          case 369: {
            DoubleField46 = input.ReadDouble();
            break;
          }
          case 377: {
            DoubleField47 = input.ReadDouble();
            break;
          }
          case 385: {
            DoubleField48 = input.ReadDouble();
            break;
          }
          case 393: {
            DoubleField49 = input.ReadDouble();
            break;
          }
          case 401: {
            DoubleField50 = input.ReadDouble();
            break;
          }
          case 409: {
            DoubleField51 = input.ReadDouble();
            break;
          }
          case 417: {
            DoubleField52 = input.ReadDouble();
            break;
          }
          case 425: {
            DoubleField53 = input.ReadDouble();
            break;
          }
          case 433: {
            DoubleField54 = input.ReadDouble();
            break;
          }
          case 441: {
            DoubleField55 = input.ReadDouble();
            break;
          }
          case 449: {
            DoubleField56 = input.ReadDouble();
            break;
          }
          case 457: {
            DoubleField57 = input.ReadDouble();
            break;
          }
          case 465: {
            DoubleField58 = input.ReadDouble();
            break;
          }
          case 472: {
            Int64Field59 = input.ReadInt64();
            break;
          }
          case 480: {
            Int64Field60 = input.ReadInt64();
            break;
          }
          case 497: {
            DoubleField62 = input.ReadDouble();
            break;
          }
          case 521: {
            DoubleField65 = input.ReadDouble();
            break;
          }
          case 529: {
            DoubleField66 = input.ReadDouble();
            break;
          }
          case 537: {
            DoubleField67 = input.ReadDouble();
            break;
          }
          case 545: {
            DoubleField68 = input.ReadDouble();
            break;
          }
          case 553: {
            DoubleField69 = input.ReadDouble();
            break;
          }
          case 561: {
            DoubleField70 = input.ReadDouble();
            break;
          }
          case 569: {
            DoubleField71 = input.ReadDouble();
            break;
          }
          case 577: {
            DoubleField72 = input.ReadDouble();
            break;
          }
          case 586: {
            StringField73 = input.ReadString();
            break;
          }
          case 594: {
            StringField74 = input.ReadString();
            break;
          }
          case 601: {
            DoubleField75 = input.ReadDouble();
            break;
          }
          case 617: {
            DoubleField77 = input.ReadDouble();
            break;
          }
          case 625: {
            DoubleField78 = input.ReadDouble();
            break;
          }
          case 633: {
            DoubleField79 = input.ReadDouble();
            break;
          }
          case 640: {
            EnumField80 = input.ReadInt32();
            break;
          }
          case 648: {
            EnumField81 = input.ReadInt32();
            break;
          }
          case 656: {
            Int64Field82 = input.ReadInt64();
            break;
          }
          case 664: {
            EnumField83 = input.ReadInt32();
            break;
          }
          case 673: {
            DoubleField84 = input.ReadDouble();
            break;
          }
          case 680: {
            Int64Field85 = input.ReadInt64();
            break;
          }
          case 688: {
            Int64Field86 = input.ReadInt64();
            break;
          }
          case 696: {
            Int64Field87 = input.ReadInt64();
            break;
          }
          case 705: {
            DoubleField88 = input.ReadDouble();
            break;
          }
          case 713: {
            DoubleField89 = input.ReadDouble();
            break;
          }
          case 721: {
            DoubleField90 = input.ReadDouble();
            break;
          }
          case 729: {
            DoubleField91 = input.ReadDouble();
            break;
          }
          case 737: {
            DoubleField92 = input.ReadDouble();
            break;
          }
          case 745: {
            DoubleField93 = input.ReadDouble();
            break;
          }
          case 753: {
            DoubleField94 = input.ReadDouble();
            break;
          }
          case 761: {
            DoubleField95 = input.ReadDouble();
            break;
          }
          case 769: {
            DoubleField96 = input.ReadDouble();
            break;
          }
          case 777: {
            DoubleField97 = input.ReadDouble();
            break;
          }
          case 785: {
            DoubleField98 = input.ReadDouble();
            break;
          }
          case 793: {
            DoubleField99 = input.ReadDouble();
            break;
          }
          case 802:
          case 800: {
            repeatedIntField100_.AddEntriesFrom(ref input, _repeated_repeatedIntField100_codec);
            break;
          }
          case 809: {
            DoubleField101 = input.ReadDouble();
            break;
          }
          case 817: {
            DoubleField102 = input.ReadDouble();
            break;
          }
          case 825: {
            DoubleField103 = input.ReadDouble();
            break;
          }
          case 833: {
            DoubleField104 = input.ReadDouble();
            break;
          }
          case 841: {
            DoubleField105 = input.ReadDouble();
            break;
          }
          case 849: {
            DoubleField106 = input.ReadDouble();
            break;
          }
          case 856: {
            Int64Field107 = input.ReadInt64();
            break;
          }
          case 865: {
            DoubleField108 = input.ReadDouble();
            break;
          }
          case 873: {
            DoubleField109 = input.ReadDouble();
            break;
          }
          case 880: {
            Int64Field110 = input.ReadInt64();
            break;
          }
          case 889: {
            DoubleField111 = input.ReadDouble();
            break;
          }
          case 896: {
            Int64Field112 = input.ReadInt64();
            break;
          }
          case 905: {
            DoubleField113 = input.ReadDouble();
            break;
          }
          case 912: {
            Int64Field114 = input.ReadInt64();
            break;
          }
          case 920: {
            Int64Field115 = input.ReadInt64();
            break;
          }
          case 929: {
            DoubleField116 = input.ReadDouble();
            break;
          }
          case 936: {
            Int64Field117 = input.ReadInt64();
            break;
          }
          case 945: {
            DoubleField118 = input.ReadDouble();
            break;
          }
          case 953: {
            DoubleField119 = input.ReadDouble();
            break;
          }
          case 961: {
            DoubleField120 = input.ReadDouble();
            break;
          }
          case 969: {
            DoubleField121 = input.ReadDouble();
            break;
          }
          case 977: {
            DoubleField122 = input.ReadDouble();
            break;
          }
          case 985: {
            DoubleField123 = input.ReadDouble();
            break;
          }
          case 993: {
            DoubleField124 = input.ReadDouble();
            break;
          }
          case 1000: {
            Int64Field125 = input.ReadInt64();
            break;
          }
          case 1008: {
            Int64Field126 = input.ReadInt64();
            break;
          }
          case 1016: {
            Int64Field127 = input.ReadInt64();
            break;
          }
          case 1025: {
            DoubleField128 = input.ReadDouble();
            break;
          }
          case 1033: {
            DoubleField129 = input.ReadDouble();
            break;
          }
        }
      }
      }
    }
}
