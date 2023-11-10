using LibIRD;
using System;
using System.IO;

namespace PrintParams
{
    internal class Program
    {
        static void Main()
        {
            string filename = "./PARAM.SFO";

            if (File.Exists(filename))
            {
                ParamSFO paramSFO = new("./PARAM.SFO");
                Console.WriteLine("PARAM.SFO for: " + paramSFO["TITLE_ID"] + '\n');
                paramSFO.Print();
            }
            else
            {
                Console.WriteLine(filename + " not found");
            }
        }
    }
}
