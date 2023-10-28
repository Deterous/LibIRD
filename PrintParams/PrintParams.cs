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
                ParamSFO paramSFO = new ParamSFO("./PARAM.SFO");
                Console.WriteLine(paramSFO["TITLE_ID"]);
                paramSFO.Print();
            }
            else
            {
                Console.WriteLine(filename + " not found");
            }
        }
    }
}
