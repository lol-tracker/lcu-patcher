using lcu_patcher;
using System.Diagnostics;

if (args.Length < 1)
{
    Console.WriteLine("Please specify league client path!");
    return 1;
}

var LCU_EXE = args[0];
var stopwatch = new Stopwatch();
stopwatch.Start();

byte[] buffer;
try
{
    buffer = File.ReadAllBytes(LCU_EXE);
} catch
{
    Console.WriteLine("Failed to read league client file!");
    return 2;
}

using var ms = new MemoryStream(buffer);
using var bw = new BinaryWriter(ms);
using var br = new BinaryReader(ms);

// Get actual offset here: 48 8D 9F ? ? ? ? 80 3B 00 0F 84 ? ? ? ? 0F 57 C0 0F 11 45 00 BA
// Get local patch offset here: 49 8D 4C 24 ? 48 8B D3 E8
// Patch here: 0F B6 87 ? ? ? ? 88 83 ? ? ? ?

// How to find patch offset:
// Look for "Creating Modules" string.
// Right before using it there is a call. Follow it.
// The very first call should be "Mtx_init_in_situ".
// We need the second one.

// How to find actual offset:
// Look for "swagger" string.
// Find xref that has "riot" string (its usually optimised to 746F6972h constant) a bit higher.
// Then look for something like cmp byte ptr [reg], 0; jz/je higher
// And check where that reg is written to.

// First
var actualOffsetPos = Utils.FindPattern(buffer, "48 8D 9F ? ? ? ? 80 3B 00 0F 84", 0x400);
if (actualOffsetPos == -1)
{
    Console.WriteLine("Failed to find pattern 1!");
    return 3;
}

ms.Seek(actualOffsetPos + 3, SeekOrigin.Begin);
var actualOffset = br.ReadUInt32();

// NOTE: This most likely will break soon and i will have to implement few patterns to handle this.
var localOffsetPos = Utils.FindPattern(buffer, "49 8D 4C 24 ? 48 8B D3 E8", 0x400);
if (localOffsetPos == -1)
{
    Console.WriteLine("Failed to find pattern 2!");
    return 4;
}

ms.Seek(localOffsetPos + 4, SeekOrigin.Begin);
var localOffset = br.ReadByte();

var patchPatternBuffer = new byte[]
{
    0x0F, 0xB6, 0x87, 0x00, 0x00, 0x00, 0x00,  // movzx   eax, byte ptr [rdi+????]
    0x88, 0x83, 0x00, 0x00, 0x00, 0x00         // mov     [rbx+????], al
};
using var patchPatternWriter = new BinaryWriter(new MemoryStream(patchPatternBuffer));
var patchOffset = actualOffset - localOffset;
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