using LibIRD;
using System;
using System.Text;

namespace BuildIRD
{
    internal class Program
    {
        static void Main()
        {
            Console.OutputEncoding = Encoding.UTF8;

            // Create new reproducible redump-style IRD with a key
            byte[] discKey = new byte[] { 0x1A, 0x2D, 0x88, 0xFC, 0x19, 0x37, 0x27, 0x44, 0x11, 0x5E, 0xE9, 0x83, 0xA0, 0x47, 0xE2, 0xD5 };
            IRD ird1 = new ReIRD("./game.iso", discKey);
            ird1.Write("./test1.ird");
            Console.WriteLine("IRD for " + ird1.Title + " created");

            // Create new reproducible redump-style IRD with a GetKey log
            string logPath = "./log.getkey.log";
            IRD ird2 = new ReIRD("./game.iso", logPath);
            ird2.Write("./test2.ird");
            Console.WriteLine("IRD for " + ird2.Title + " created");
        }
    }
}
