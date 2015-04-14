# generate using the new protogen
../../../../../protoc -I. google/protobuf/unittest.proto --csharp_out=new_output

vimdiff new_output/Unittest.cs old_output/Unittest.cs -c 'set diffopt+=iwhite'
