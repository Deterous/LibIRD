using LibIRD;
using System;

namespace BuildIRD
{
    internal class Program
    {
        static void Main()
        {
            byte[] discKey = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
            IRD ird1 = new ReIRD("./game.iso", discKey);
            IRD ird2 = new ReIRD("./game.iso", "./log.getkey.log");
            ird1.Write("./test1.ird");
            ird2.Write("./test2.ird");
            Console.WriteLine(ird1.Title);
        }
    }
}
