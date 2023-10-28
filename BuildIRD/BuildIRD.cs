using LibIRD;
using System;

namespace BuildIRD
{
    internal class Program
    {
        static void Main()
        {
            byte[] discKey = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
            IRD ird = new ReIRD("./game.iso", discKey);
            ird.Write("./test.ird");
            Console.WriteLine(ird.HeaderLength);
        }
    }
}
