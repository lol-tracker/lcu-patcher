using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace lcu_patcher
{
    public static class Utils
    {
        public static long FindPattern(byte[] buffer, byte[] pattern, byte[] mask, long offset = 0)
        {
            for (var i = offset; i < buffer.LongLength; i++)
            {
                if (buffer[i] != pattern[0]) // for performance reasons we assume mask[0] != 'x'
                    continue;

                long j;
                for (j = 0L; j < pattern.LongLength; j++)
                {
                    if (mask[j] == 0x00)
                        continue;

                    if (pattern[j] != buffer[i + j])
                        break;
                }

                if (j == pattern.LongLength)
                {
                    return i;
                }
            }

            return -1L;
        }

        public static long FindPattern(byte[] buffer, string pattern, long offset = 0)
        {
            var patternBuffer = ParsePattern(pattern, out var maskBuffer);
            return FindPattern(buffer, patternBuffer, maskBuffer, offset);
        }

        private static byte[] ParsePattern(string pattern, out byte[] mask)
        {
            var buffer = new List<byte>();
            var maskBuffer = new List<byte>();

            foreach (var b in pattern.Split(' '))
            {
                if (b == "?")
                {
                    maskBuffer.Add(0x00);
                    buffer.Add(0x00);
                }
                else
                {
                    maskBuffer.Add(0xFF);
                    buffer.Add(Convert.ToByte(b, 16));
                }
            }

            mask = maskBuffer.ToArray();
            return buffer.ToArray();
        }

        public static long FindStart(byte[] buffer, long offset, byte padding = 0xCC, int alignment = 0x10)
        {
            for (var i = (offset - (offset % alignment)); i >= alignment; i -= alignment)
            {
                if (buffer[i-1] == padding)
                    return i;
            }

            return -1L;
        }
    }
}
