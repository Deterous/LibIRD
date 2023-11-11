using LibIRD;
using System;
using System.IO;

namespace PrintParams
{
    internal class Program
    {
        static void Main()
        {
            string filename = "./PS3_DISC.SFB";
            if (File.Exists(filename))
            {
                PS3_DiscSFB ps3_DiscSFB = new(filename);
                Console.WriteLine("PS3_DISC.SFB for: " + ps3_DiscSFB.Field["TITLE_ID"]);
                ps3_DiscSFB.Print();
            }
            else
            {
                Console.WriteLine(filename + " not found");
            }

            filename = "./PARAM.SFO";
            if (File.Exists(filename))
            {
                ParamSFO paramSFO = new(filename);
                Console.WriteLine("PARAM.SFO for: " + paramSFO["TITLE_ID"]);
                paramSFO.Print();
            }
            else
            {
                Console.WriteLine(filename + " not found");
            }
        }
    }
}
