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
                Console.WriteLine("IRD created using .key:");

                // Read IRD and print details to console
                IRD ird = IRD.Read("./test1.ird");
                Console.WriteLine("IRD Version: " + ird.Version);
                Console.WriteLine("Title ID: " + ird.TitleID);
                Console.WriteLine("Title: " + ird.Title);
                Console.WriteLine("System Version: " + ird.SystemVersion);
                Console.WriteLine("Game Version: " + ird.GameVersion);
                Console.WriteLine("App Version: " + ird.AppVersion);
            }
            catch (FileNotFoundException e)
            {
                Console.WriteLine("File not found: " + e.FileName);
            }

            // Create new reproducible redump-style IRD with a GetKey log
            try
            {
                IRD ird2 = new ReIRD("./game.iso", "./log.getkey.log");
                ird2.Write("./test2.ird");
                Console.WriteLine("IRD created using .getkey.log:");

                // Read IRD and print details to console
                IRD ird = IRD.Read("./test2.ird");
                Console.WriteLine("IRD Version: " + ird.Version);
                Console.WriteLine("Title ID: " + ird.TitleID);
                Console.WriteLine("Title: " + ird.Title);
                Console.WriteLine("System Version: " + ird.SystemVersion);
                Console.WriteLine("Game Version: " + ird.GameVersion);
                Console.WriteLine("App Version: " + ird.AppVersion);
            }
            catch (FileNotFoundException e)
            {
                Console.WriteLine("File not found: " + e.FileName);
            }
        }
    }
}
