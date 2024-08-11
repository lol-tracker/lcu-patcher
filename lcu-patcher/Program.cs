using lcu_patcher;
using System.Diagnostics;

if (args.Length < 1)
{
    Console.WriteLine("Please specify league client path!");
    return 1;
}

// There are multiple places where we need to patch league client to think that swagger api argument is set.
// But we can patch it only once when API object is copied.
//
// There is this huge object that i call api object, which is responsible for a lot of api stuff.
// Inside it there is a member, that controls wether or not expose swagger and openapi apis.
//
// "boolOffset" points to cmp;jz instructions that compare boolean of said member.
// Thats where we get our "target" offset.
//
// To find that place in case if pattern breaks you can use this pattern:
// C7 45 ? 72 69 6F 74 C6 45 ? 00 4C 8D 0D
// ^ is: mov dword ptr [rbp+???], 746F6972h ; "riot"
//       mov byte ptr [rbp+???], 0 ; '\0'
//       lea r9, "swagger"
//
// You will land exactly in a block that is only executed if flag is set,
// so go a bit up and look for cmp;jz instructions.
//
// Now onto object copy method.
// Find it is easy, just look for "Creating Modules" string.
// Right before that there is a call, which is a copy method for huge _API object_.
// The problem is that our bool flag is inside a smaller object inside of bigger API object.
// Luckily the very first call _inside API object's copy method_ is exactly copy method of that smaller object.
// The only issue is that it is passed with an offset, so we have to account for that.
// So "copyObjOffset" pattern is just that call, which is currently something like:
//  lea     rcx, [r14+78h]
//  call    sub_140216050
//  nop
//  xorps   xmm0, xmm0
//
// Now as you can see there is an offset that we have to account for.
// This is exact offset that we later use to calculate offset within copy method.
// 
// Then we look for something like:
//  movzx   eax, byte ptr [rdi+XXX]
//  mov     [rbx+XXX], al
//
// Where XXX is patchOffset (meaning offset based on an object inside of API object)
// This is part of object copy method.
//
// And then we just patch it to:
//  mov byte ptr [rbx+XXX], 1
//
// Das it.
//

var LCU_EXE = args[0];
var stopwatch = new Stopwatch();
stopwatch.Start();

byte[] buffer;
try
{
    buffer = File.ReadAllBytes(LCU_EXE);
}
catch
{
    Console.WriteLine("Failed to read league client file!");
    return 2;
}

using var ms = new MemoryStream(buffer);
using var bw = new BinaryWriter(ms);
using var br = new BinaryReader(ms);

// First
var boolOffsetPos = Utils.FindPattern(buffer, "80 BF ? ? ? ? ? 0F 84 ? ? ? ? 48 8D 8F ? ? ? ? 0F 57 C0", 0x400);
if (boolOffsetPos == -1)
{
    Console.WriteLine("Failed to find pattern 1!");
    return 3;
}

ms.Seek(boolOffsetPos + 2, SeekOrigin.Begin);
var boolOffset = br.ReadUInt32();

var copyObjOffsetPos = Utils.FindPattern(buffer, "49 8D 4E ? E8 ? ? ? ? 90 0F 57 C0", 0x400);
if (copyObjOffsetPos == -1)
{
    Console.WriteLine("Failed to find pattern 2!");
    return 4;
}

ms.Seek(copyObjOffsetPos + 3, SeekOrigin.Begin);
var copyObjOffset = br.ReadByte();

var patchPatternBuffer = new byte[]
{
    0x0F, 0xB6, 0x87, 0x00, 0x00, 0x00, 0x00,  // movzx   eax, byte ptr [rdi+????]
    0x88, 0x83, 0x00, 0x00, 0x00, 0x00         // mov     [rbx+????], al
};
using var patchPatternWriter = new BinaryWriter(new MemoryStream(patchPatternBuffer));
var patchOffset = boolOffset - copyObjOffset;
patchPatternWriter.Seek(3, SeekOrigin.Begin); patchPatternWriter.Write((UInt32)patchOffset);
patchPatternWriter.Seek(9, SeekOrigin.Begin); patchPatternWriter.Write((UInt32)patchOffset);

var patchPos = Utils.FindPattern(buffer, patchPatternBuffer, Enumerable.Repeat<byte>(0xFF, patchPatternBuffer.Length).ToArray(), 0x400);
if (patchPos == -1)
{
    Console.WriteLine("Failed to find pattern 3!");
    return 5;
}

var patchBuffer = Enumerable.Repeat<byte>(0x90, patchPatternBuffer.Length).ToArray();
using var patchBufferWriter = new BinaryWriter(new MemoryStream(patchBuffer));

patchBufferWriter.Seek(0, SeekOrigin.Begin);
patchBufferWriter.Write(new byte[] { 0xc6, 0x83, 0x00, 0x00, 0x00, 0x00, 0x01 });

patchBufferWriter.Seek(2, SeekOrigin.Begin);
patchBufferWriter.Write((UInt32)patchOffset);

ms.Seek(patchPos, SeekOrigin.Begin);
ms.Write(patchBuffer);

File.Move(LCU_EXE, Path.ChangeExtension(LCU_EXE, "old"), true);
File.WriteAllBytes(LCU_EXE, buffer);

stopwatch.Stop();
Console.WriteLine($"Done in {stopwatch.Elapsed.TotalMilliseconds}ms!");
return 0;
