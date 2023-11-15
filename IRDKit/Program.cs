using LibIRD;
using System;
using System.Collections.Generic;
using CommandLine;
using System.Text;
using System.IO;

namespace IRDKit
{
    internal class Program
    {
        // IRD Creation
        [Verb("create")]
        public class CreateOptions
        {
            [Value(0, Required = true, HelpText = "Path to an ISO file, or directory of ISO files")]
            public string ISOPath { get; set; }

            [Value(1, HelpText = "Path to the IRD file to be created")]
            public string IRDPath { get; set; }

            [Option('r', "recurse", HelpText = "Recurse through all subdirectories and generate IRDs for all ISOs")]
            public bool Recurse { get; set; }

            [Option('k', "key", HelpText = "Hexadecimal representation of the disc key")]
            public string Key { get; set; }

            [Option("key-file", HelpText = "Path to a redump .key file")]
            public string KeyFile { get; set; }

            [Option('l', "getkey-log", HelpText = "Path to a .getkey.log file")]
            public string GetKeyLog { get; set; }
        }

        // IRD Info
        public class InfoOptions
        {
            [Value(0, Required = true, HelpText = "Path to the IRD file to be printed")]
            public string IRDPath { get; set; }
        }

        public static void Main(string[] args)
        {
            // Parse command line arguments
            var result = Parser.Default.ParseArguments<CreateOptions, InfoOptions>(args)
                .WithParsed(Run)
                .WithNotParsed(HandleParseError);
        }

        private static void HandleParseError(IEnumerable<Error> errs)
        {
            if (errs.IsVersion())
            {
                // --version
            }
            else if (errs.IsHelp())
            {
                // --help
            }
            else
            {
                // Parsing error
            }
            return;
        }

        private static void Run(object obj)
        {
            switch (obj)
            {
                case CreateOptions c:
                    Console.OutputEncoding = Encoding.UTF8;

                    // Create new reproducible redump-style IRD with a key file
                    if (c.KeyFile != null)
                    {
                        try
                        {
                            // Read key from .key file
                            byte[] discKey = File.ReadAllBytes(c.KeyFile);

                            IRD ird1 = new ReIRD(c.ISOPath, discKey);
                            ird1.Write(c.IRDPath);
                            ird1.Print();
                        }
                        catch (FileNotFoundException)
                        {
                            Console.WriteLine("ERROR: File not found");
                        }
                    }

                    // Create new reproducible redump-style IRD with a GetKey log
                    if (c.GetKeyLog != null)
                    {
                        try
                        {
                            IRD ird2 = new ReIRD(c.ISOPath, c.GetKeyLog);
                            ird2.Write(c.IRDPath);
                            ird2.Print();
                        }
                        catch (FileNotFoundException)
                        {
                            Console.WriteLine("ERROR: File not found");
                        }
                    }
                    break;
                case InfoOptions info:
                    //
                    break;
            }
        }
    }
}