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

            // Create new reproducible redump-style IRD with a key
            byte[] discKey = new byte[] { 0x1A, 0x2D, 0x88, 0xFC, 0x19, 0x37, 0x27, 0x44, 0x11, 0x5E, 0xE9, 0x83, 0xA0, 0x47, 0xE2, 0xD5 };
            string fileName = "./game.iso";
            try
            {
                IRD ird1 = new ReIRD(fileName, discKey);
                ird1.Write("./test1.ird");
                Console.WriteLine("IRD for " + fileName + " created");

                // Create new reproducible redump-style IRD with a GetKey log
                string logPath = "./log.getkey.log";
                try
                {
                    IRD ird2 = new ReIRD(fileName, logPath);
                    ird2.Write("./test2.ird");
                    Console.WriteLine("IRD for " + fileName + " created");
                }
                catch (FileNotFoundException)
                {
                    Console.WriteLine("File not found: " + logPath);
                }

                // Read IRD and print details to console
                IRD ird = IRD.Read("./test1.ird");
                Console.WriteLine("IRD Version: " + ird.Version);
                Console.WriteLine("Title ID: " + ird.TitleID);
                Console.WriteLine("Title: " + ird.Title);
                Console.WriteLine("System Version: " + ird.SystemVersion);
                Console.WriteLine("Game Version: " + ird.GameVersion);
                Console.WriteLine("App Version: " + ird.AppVersion);
            }
            catch (FileNotFoundException)
            {
                Console.WriteLine("File not found: " + fileName);
            }
        }
    }
}
