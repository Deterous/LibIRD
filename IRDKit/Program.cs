using CommandLine;
using DiscUtils;
using DiscUtils.Iso9660;
using LibIRD;
using SabreTools.RedumpLib.Web;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;
using System.Security.Cryptography;
using System.Text;

namespace IRDKit
{
    internal class Program
    {
        /// <summary>
        /// IRD Creation Verb
        /// </summary>
        [Verb("create", HelpText = "Create an IRD from an ISO")]
        public class CreateOptions
        {
            [Value(0, Required = true, HelpText = "Path to an ISO file, or directory of ISO files")]
            public string ISOPath { get; set; }

            [Value(1, Required = false, HelpText = "Path to the IRD file to be created")]
            public string IRDPath { get; set; }

            [Option('b', "layerbreak", HelpText = "Layerbreak value in bytes (define for BD-Video hybrid discs). Default: 12219392")]
            public long? Layerbreak {  get; set; }

            [Option('k', "key", HelpText = "Hexadecimal representation of the disc key")]
            public string Key { get; set; }

            [Option('l', "getkey-log", HelpText = "Path to a .getkey.log file")]
            public string GetKeyLog { get; set; }

            [Option('f', "key-file", HelpText = "Path to a redump .key file")]
            public string KeyFile { get; set; }

            [Option('r', "recurse", HelpText = "Recurse through all subdirectories and generate IRDs for all ISOs")]
            public bool Recurse { get; set; }
        }

        /// <summary>
        /// IRD or ISO information verb
        /// </summary>
        [Verb("info", HelpText = "Print information from an IRD or ISO")]
        public class InfoOptions
        {
            [Value(0, Required = true, HelpText = "Path to the IRD or ISO file to be printed")]
            public string InPath { get; set; }

            [Value(1, Required = false, HelpText = "Path to the text or json file to be created")]
            public string OutPath { get; set; }

            [Option('j', "json", HelpText = "Print IRD or ISO information as a JSON object")]
            public bool Json { get; set; }
        }

        /// <summary>
        /// Parse command line arguments
        /// </summary>
        /// <param name="args">Command line arguments</param>
        public static void Main(string[] args)
        {
            var result = Parser.Default.ParseArguments<CreateOptions, InfoOptions>(args).WithParsed(Run);
        }

        /// <summary>
        /// Perform 
        /// </summary>
        /// <param name="obj"></param>
        /// <exception cref="FileNotFoundException"></exception>
        /// <exception cref="InvalidFileSystemException"></exception>
        private static void Run(object obj)
        {
            switch (obj)
            {
                case CreateOptions opt:
                    Console.OutputEncoding = Encoding.UTF8;

                    // Validate ISO path
                    ArgumentNullException.ThrowIfNull(opt.ISOPath);

                    // If directory, search for all ISOs in current directory
                    if (Directory.Exists(opt.ISOPath))
                    {
                        // If recurse option enabled, search recursively
                        IEnumerable<string> isoFiles;
                        if (opt.Recurse)
                        {
                            Console.WriteLine($"Recursively searching for ISOs in {opt.ISOPath}");
                            isoFiles = Directory.EnumerateFiles(opt.ISOPath, "*.iso", SearchOption.AllDirectories);
                        }
                        else
                        {
                            Console.WriteLine($"Searching for ISOs in {opt.ISOPath}");
                            isoFiles = Directory.EnumerateFiles(opt.ISOPath, "*.iso", SearchOption.TopDirectoryOnly);
                        }
                        // Create an IRD file for all ISO files found
                        foreach (string file in isoFiles)
                            ProcessISO(opt, file);
                        break;
                    }

                    // Create a single IRD from an ISO
                    if (File.Exists(opt.ISOPath))
                    {
                        ProcessISO(opt, opt.ISOPath, opt.IRDPath);
                        break;
                    }

                    throw new ArgumentException("Not a valid ISO file or directory");

                case InfoOptions opt:
                    string filetype = Path.GetExtension(opt.InPath);

                    if (String.Compare(filetype, ".iso", StringComparison.OrdinalIgnoreCase) == 0)
                    {
                        // Open ISO file for reading
                        using FileStream fs = new FileStream(opt.InPath, FileMode.Open, FileAccess.Read) ?? throw new FileNotFoundException(opt.InPath);
                        // Validate ISO file stream
                        if (!CDReader.Detect(fs))
                            throw new InvalidFileSystemException("Not a valid ISO file");
                        // Create new ISO reader
                        CDReader reader = new(fs, true, true);

                        if (opt.Json)
                        {
                            if (opt.OutPath != null)
                                File.WriteAllText(opt.OutPath, "{\n");
                            else
                                Console.WriteLine('{');
                        }

                        // Write PS3_DISC.SFB info
                        using (DiscUtils.Streams.SparseStream s = reader.OpenFile("PS3_DISC.SFB", FileMode.Open, FileAccess.Read))
                        {
                            try
                            {
                                PS3_DiscSFB ps3_DiscSFB = new(s);
                                if (opt.Json)
                                {
                                    if (opt.OutPath != null)
                                        File.AppendAllText(opt.OutPath, "\"PS3_DISC.SFB\": ");
                                    else
                                        Console.Write("\"PS3_DISC.SFB\": ");
                                    ps3_DiscSFB.PrintJson(opt.OutPath);
                                    if (opt.OutPath != null)
                                        File.AppendAllText(opt.OutPath, ",\n");
                                    else
                                        Console.WriteLine(',');
                                }
                                else
                                    ps3_DiscSFB.Print(opt.OutPath);
                            }
                            catch
                            {
                                Console.WriteLine("PS3_DISC.SFB not found");
                            }
                        }

                        // Write PARAM.SFO info
                        using (DiscUtils.Streams.SparseStream s = reader.OpenFile("PS3_GAME\\PARAM.SFO", FileMode.Open, FileAccess.Read))
                        {
                            try
                            {
                                ParamSFO paramSFO = new(s);
                                if (opt.Json)
                                {
                                    if (opt.OutPath != null)
                                        File.AppendAllText(opt.OutPath, "\"PARAM.SFO\": ");
                                    else
                                        Console.Write("\"PARAM.SFO\": ");
                                    paramSFO.PrintJson(opt.OutPath);
                                }
                                else
                                    paramSFO.Print(opt.OutPath);
                            }
                            catch
                            {
                                Console.WriteLine("PS3_GAME\\PARAM.SFO not found");
                            }
                        }

                        if (opt.Json)
                        {
                            if (opt.OutPath != null)
                                File.AppendAllText(opt.OutPath, "\n}\n");
                            else
                                Console.WriteLine("\n}");
                        }
                    }
                    else
                    {
                        // Assume it is an IRD file
                        if (opt.Json)
                            IRD.Read(opt.InPath).PrintJson(opt.OutPath);
                        else
                            IRD.Read(opt.InPath).Print(opt.OutPath);
                    }

                    break;
            }
        }

        public static void ProcessISO(CreateOptions opt, string isoPath, string irdPath = null)
        {
            // Check file exists
            FileInfo iso = new(isoPath);
            if (!iso.Exists)
            {
                Console.WriteLine($"{nameof(isoPath)} is not a valid File or Directory");
                return;
            }

            // Determin IRD path if none given
            irdPath ??= Path.GetFileNameWithoutExtension(isoPath) + ".ird";

            // Create new reproducible redump-style IRD with a given hex key
            if (opt.Key != null)
            {
                try
                {
                    // Get disc key from hex string
                    byte[] discKey = Convert.FromHexString(opt.Key);
                    if (discKey == null || discKey.Length != 16)
                        throw new ArgumentException(opt.Key);

                    Console.WriteLine($"Creating {irdPath} with Key: {opt.Key}");
                    IRD ird1 = new ReIRD(isoPath, discKey, opt.Layerbreak);
                    ird1.Write(irdPath);
                    ird1.Print();
                    return;
                }
                catch (ArgumentException)
                {
                    Console.Error.WriteLine("Given key not valid, detecting key automatically...");
                }
                catch (FileNotFoundException)
                {
                    Console.Error.WriteLine("File not found, failed to create IRD");
                    return;
                }
            }

            // Create new reproducible redump-style IRD with a given key file
            if (opt.KeyFile != null)
            {
                try
                {
                    // Read key from .key file
                    byte[] discKey = File.ReadAllBytes(opt.KeyFile);
                    if (discKey == null || discKey.Length != 16)
                        throw new ArgumentException(opt.KeyFile);

                    
                    Console.WriteLine($"Creating {irdPath} with Key: {Convert.ToHexString(discKey)}");
                    IRD ird2 = new ReIRD(isoPath, discKey, opt.Layerbreak);
                    ird2.Write(irdPath);
                    ird2.Print();
                    return;
                }
                catch (ArgumentException)
                {
                    Console.Error.WriteLine("Given key file not valid, detecting key automatically...");
                }
                catch (FileNotFoundException)
                {
                    Console.Error.WriteLine("File not found, failed to create IRD");
                    return;
                }
            }

            // Create new reproducible redump-style IRD with a given GetKey log
            if (opt.GetKeyLog != null)
            {
                try
                {
                    Console.WriteLine($"Creating {irdPath} with key from: {opt.GetKeyLog}");
                    IRD ird3 = new ReIRD(isoPath, opt.GetKeyLog);
                    ird3.Write(irdPath);
                    ird3.Print();
                    return;
                }
                catch (FileNotFoundException)
                {
                    Console.Error.WriteLine("File not found, failed to create IRD");
                    return;
                }
            }

            // No key provided, try search for .key file
            string keyfilePath = Path.GetFileNameWithoutExtension(isoPath) + ".key";
            FileInfo keyfile = new(keyfilePath);
            if (keyfile.Exists)
            {
                // Found .key file, try use it
                try
                {
                    // Read key from .key file
                    byte[] discKey = File.ReadAllBytes(keyfilePath);
                    if (discKey == null || discKey.Length != 16)
                        throw new ArgumentException(opt.KeyFile);

                    Console.WriteLine($"Creating {irdPath} with Key: {Convert.ToHexString(discKey)}");
                    IRD ird2 = new ReIRD(isoPath, discKey, opt.Layerbreak);
                    ird2.Write(irdPath);
                    ird2.Print();
                    return;
                }
                catch (ArgumentException)
                {
                    Console.Error.WriteLine("Given key file not valid, detecting key automatically...");
                }
                catch (FileNotFoundException)
                {
                    Console.Error.WriteLine("File not found, failed to create IRD");
                    return;
                }
            }

            // No key provided, try search for .getkey.log file
            string logfilePath = Path.GetFileNameWithoutExtension(isoPath) + ".getkey.log";
            FileInfo logfile = new(logfilePath);
            if (logfile.Exists)
            {
                // Found .getkey.log file, check it is valid
                try
                {
                    Console.WriteLine($"Creating {irdPath} with key from: {logfilePath}");
                    IRD ird3 = new ReIRD(isoPath, logfilePath);
                    ird3.Write(irdPath);
                    ird3.Print();
                    return;
                }
                catch (FileNotFoundException)
                {
                    Console.Error.WriteLine("File not found, failed to create IRD");
                    return;
                }
            }

            // No key provided, try get key from redump.org
            Console.WriteLine("No key provided... Searching for key on redump.org...");

            // Compute CRC32 hash
            byte[] crc32;
            using (FileStream fs = File.OpenRead(isoPath))
            {
                Crc32 hasher = new();
                hasher.Append(fs);
                crc32 = hasher.GetCurrentHash();
                // Change endianness
                Array.Reverse(crc32);
            }
            string crc32_hash = Convert.ToHexString(crc32).ToLower();

            // Search for ISO on redump.org
            RedumpHttpClient redump = new();
            List<int> ids = redump.CheckSingleSitePage("http://redump.org/discs/system/ps3/quicksearch/" + crc32_hash).ConfigureAwait(false).GetAwaiter().GetResult();
            int id;
            if (ids.Count == 0)
            {
                Console.WriteLine("ISO not found in redump, cannot automatically retreive key.");
                return;
            }
            else if (ids.Count > 1)
            {
                // Compute SHA1 hash
                byte[] sha1;
                using (FileStream fs = File.OpenRead(isoPath))
                {
                    SHA1 hasher = SHA1.Create();
                    sha1 = hasher.ComputeHash(fs);
                }
                string sha1_hash = Convert.ToHexString(sha1).ToLower();

                // Search redump.org for SHA1 hash
                List<int> ids2 = redump.CheckSingleSitePage("http://redump.org/discs/system/ps3/quicksearch/" + sha1_hash).ConfigureAwait(false).GetAwaiter().GetResult();
                if (ids2.Count == 0)
                {
                    Console.WriteLine("ISO not found in redump, cannot automatically retreive key.");
                    return;
                }
                else if (ids2.Count > 1)
                {
                    Console.WriteLine("Cannot automatically get key from redump. Please search redump.org and run again with -k");
                    return;
                }
                id = ids2[0];
            }
            else
            {
                id = ids[0];
            }

            // Download key file from redump.org
            byte[] key = redump.GetByteArrayAsync($"http://redump.org/disc/{id}/key").ConfigureAwait(false).GetAwaiter().GetResult();
            if (key.Length != 16)
            {
                Console.WriteLine("Invalid key obtained from redump");
            }

            // Create IRD with key from redump
            Console.WriteLine($"Creating {irdPath} with Key: {Convert.ToHexString(key)}");
            IRD ird = new ReIRD(isoPath, key, opt.Layerbreak);
            ird.Write(irdPath);
            ird.Print();
        }
    }
}