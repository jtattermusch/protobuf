# generate using the new old
../../../../../protoc -I. google/protobuf/unittest.proto --include_imports --descriptor_set_out=old_output/unittest.pb
mono ~/github/protobuf-csharp-port/src/ProtoGen/bin/NET35/Debug/ProtoGen.exe old_output/unittest.pb -output_directory=old_output -ignore_google_protobuf=true
