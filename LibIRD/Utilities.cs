using System;

namespace LibIRD
{
    internal class Utilities
    {
        // Helper function to convert a hex string to a byte array
        // Original source: https://codereview.stackexchange.com/a/53846

        private static int HexToInt(char c)
        {
            return c switch
            {
                '0' => 0,
                '1' => 1,
                '2' => 2,
                '3' => 3,
                '4' => 4,
                '5' => 5,
                '6' => 6,
                '7' => 7,
                '8' => 8,
                '9' => 9,
                'a' or 'A' => 10,
                'b' or 'B' => 11,
                'c' or 'C' => 12,
                'd' or 'D' => 13,
                'e' or 'E' => 14,
                'f' or 'F' => 15,
                _ => throw new FormatException("Unrecognized hex char " + c),
            };
        }

        private static readonly byte[,] ByteLookup = new byte[,]
        {
            // low nibble
            {0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x0e, 0x0f},
            // high nibble
            {0x00, 0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80, 0x90, 0xa0, 0xb0, 0xc0, 0xd0, 0xe0, 0xf0}
        };

        internal static byte[] HexToBytes(string input)
        {
            var result = new byte[(input.Length + 1) >> 1];
            int lastcell = result.Length - 1;
            int lastchar = input.Length - 1;
            // count up in characters, but inside the loop will
            // reference from the end of the input/output.
            for (int i = 0; i < input.Length; i++)
            {
                // i >> 1    -  (i / 2) gives the result byte offset from the end
                // i & 1     -  1 if it is high-nibble, 0 for low-nibble.
                result[lastcell - (i >> 1)] |= ByteLookup[i & 1, HexToInt(input[lastchar - i])];
            }
            return result;
        }
    }
}
