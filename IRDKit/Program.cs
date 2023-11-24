using LibIRD;
using System;
using System.Collections.Generic;
using CommandLine;
using System.Text;
using System.IO;
using DiscUtils.Iso9660;
using DiscUtils;

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
        [Verb("info")]
        public class InfoOptions
        {
            [Value(0, Required = true, HelpText = "Path to the IRD or ISO file to be printed")]
            public string Path { get; set; }
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
                Console.WriteLine("Help...");
            }
            else if (errs.IsHelp())
            {
                // --help
                Console.WriteLine("Version...");
            }
            else
            {
                // Parsing error
                Console.WriteLine("Parsing error");
            }
            return;
        }

        private static void Run(object obj)
        {
            switch (obj)
            {
                case CreateOptions c:
                    Console.OutputEncoding = Encoding.UTF8;

                    // Create new reproducible redump-style IRD with a hex key
                    if (c.Key != null)
                    {
                        try
                        {
                            // Get disc key from hex string
                            byte[] discKey = Convert.FromHexString(c.Key);

                            IRD ird1 = new ReIRD(c.ISOPath, discKey);
                            ird1.Write(c.IRDPath ?? Path.GetFileNameWithoutExtension(c.ISOPath) + ".ird");
                            ird1.Print();
                        }
                        catch (FileNotFoundException)
                        {
                            Console.Error.WriteLine("File not found");
                        }
                        break;
                    }

                    // Create new reproducible redump-style IRD with a key file
                    if (c.KeyFile != null)
                    {
                        try
                        {
                            // Read key from .key file
                            byte[] discKey = File.ReadAllBytes(c.KeyFile);

                            IRD ird1 = new ReIRD(c.ISOPath, discKey);
                            ird1.Write(c.IRDPath ?? Path.GetFileNameWithoutExtension(c.ISOPath) + ".ird");
                            ird1.Print();
                        }
                        catch (FileNotFoundException)
                        {
                            Console.Error.WriteLine("File not found");
                        }
                        break;
                    }

                    // Create new reproducible redump-style IRD with a GetKey log
                    if (c.GetKeyLog != null)
                    {
                        try
                        {
                            IRD ird2 = new ReIRD(c.ISOPath, c.GetKeyLog);
                            ird2.Write(c.IRDPath ?? Path.GetFileNameWithoutExtension(c.ISOPath) + ".ird");
                            ird2.Print();
                        }
                        catch (FileNotFoundException)
                        {
                            Console.Error.WriteLine("File not found");
                        }
                        break;
                    }
                    break;
                case InfoOptions info:
                    string filetype = Path.GetExtension(info.Path);

                    if (String.Compare(filetype, ".iso", StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        // Open ISO file for reading
                        using FileStream fs = new FileStream(info.Path, FileMode.Open, FileAccess.Read) ?? throw new FileNotFoundException(info.Path);
                        // Validate ISO file stream
                        if (!CDReader.Detect(fs))
                            throw new InvalidFileSystemException("Not a valid ISO file");
                        // Create new ISO reader
                        CDReader reader = new(fs, true, true);

                        using (DiscUtils.Streams.SparseStream s = reader.OpenFile("PS3_DISC.SFB", FileMode.Open, FileAccess.Read))
                        {
                            try
                            {
                                PS3_DiscSFB ps3_DiscSFB = new(s);
                                ps3_DiscSFB.Print();
                            }
                            catch
                            {
                                Console.WriteLine("PS3_DISC.SFB not found");
                            }
                        }
                            

                        using (DiscUtils.Streams.SparseStream s = reader.OpenFile("PS3_GAME\\PARAM.SFO", FileMode.Open, FileAccess.Read))
                        {
                            try
                            {
                                ParamSFO paramSFO = new(s);
                                paramSFO.Print();
                            }
                            catch
                            {
                                Console.WriteLine("./PARAM.SFO not found");
                            }
                        }
                    }
                    else // Assume it is an IRD file
                    {
                        IRD.Read(info.Path).Print();
                    }
                    break;
            }
        }
    }
}