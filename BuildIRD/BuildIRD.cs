using LibIRD;
using System;
using System.IO;
using System.Text;

namespace BuildIRD
{
    internal class Program
    {
        static void Main()
        {
            Console.OutputEncoding = Encoding.UTF8;

            // Create new reproducible redump-style IRD with a key file
            try
            {
                // Read key from .key file
                byte[] discKey = File.ReadAllBytes("./game.key");

                IRD ird1 = new ReIRD("./game.iso", discKey);
                ird1.Write("./test1.ird");
                ird1.Print();
            }
            catch (FileNotFoundException e)
            {
                Console.WriteLine("ERROR: File not found");
            }

            // Create new reproducible redump-style IRD with a ManaGunZ log
            try
            {
                IRD ird2 = new ReIRD("./game.iso", "./log.getkey.log");
                ird2.Write("./test2.ird");
                ird2.Print();
            }
            catch (FileNotFoundException e)
            {
                Console.WriteLine("ERROR: File not found");
            }
        }
    }
}
