using CommandLine;
using DiscUtils;
using DiscUtils.Iso9660;
using LibIRD;
using SabreTools.RedumpLib.Web;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace IRDKit
{
    internal class Program
    {
        /// <summary>
        /// IRD Creation command
        /// </summary>
        [Verb("create", HelpText = "Create an IRD from an ISO")]
        public class CreateOptions
        {
            [Value(0, Required = true, HelpText = "Path to an ISO file, or directory of ISO files")]
            public IEnumerable<string> ISOPath { get; set; }

            [Option('o', "output", HelpText = "Path to the IRD file to be created (will overwrite)")]
            public string IRDPath { get; set; }

            [Option('b', "layerbreak", HelpText = "Layerbreak value in bytes (use with BD-Video hybrid discs). Default: 12219392")]
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
        /// IRD or ISO information command
        /// </summary>
        [Verb("info", HelpText = "Print information from an IRD or ISO")]
        public class InfoOptions
        {
            [Value(0, Required = true, HelpText = "Path to an IRD or ISO file, or directory of IRD and/or ISO files")]
            public IEnumerable<string> InPath { get; set; }

            [Option('o', "output", HelpText = "Path to the text or json file to be created (will overwrite)")]
            public string OutPath { get; set; }

            [Option('j', "json", HelpText = "Print IRD or ISO information as a JSON object")]
            public bool Json { get; set; }

            [Option('r', "recurse", HelpText = "Recurse through all subdirectories and print information for all ISOs and IRDs")]
            public bool Recurse { get; set; }
        }

        /// <summary>
        /// Parse command line arguments
        /// </summary>
        /// <param name="args">Command line arguments</param>
        public static void Main(string[] args)
        {
            // Ensure console prints foreign characters properly
            Console.OutputEncoding = Encoding.UTF8;

            // Parse arguments
            var result = Parser.Default.ParseArguments<CreateOptions, InfoOptions>(args).WithParsed(Run);
        }

        /// <summary>
        /// Parse arguments
        /// </summary>
        /// <param name="args">Command-line arguments</param>
        /// <exception cref="ArgumentException"></exception>
        private static void Run(object args)
        {
            switch (args)
            {
                // Process options from a `create` command
                case CreateOptions opt:

                    // Validate ISO paths
                    ArgumentNullException.ThrowIfNull(opt.ISOPath);

                    foreach (string isoPath in opt.ISOPath)
                    {
                        // Validate ISO path
                        ArgumentNullException.ThrowIfNull(isoPath);

                        // If directory, search for all ISOs in current directory
                        if (Directory.Exists(isoPath))
                        {
                            // If recurse option enabled, search recursively
                            IEnumerable<string> isoFiles;
                            if (opt.Recurse)
                            {
                                if (isoPath == ".")
                                    Console.WriteLine($"Recursively searching for ISOs in current directory");
                                else
                                    Console.WriteLine($"Recursively searching for ISOs in {isoPath}");
                                isoFiles = Directory.EnumerateFiles(isoPath, "*.iso", SearchOption.AllDirectories);
                            }
                            else
                            {
                                if (isoPath == ".")
                                    Console.WriteLine($"Searching for ISOs in current directory");
                                else
                                    Console.WriteLine($"Searching for ISOs in {isoPath}");
                                isoFiles = Directory.EnumerateFiles(isoPath, "*.iso", SearchOption.TopDirectoryOnly);
                            }

                            // Warn if no files are found
                            if (!isoFiles.Any())
                                Console.WriteLine("No ISOs found (ensure .iso extension)");

                            // Create an IRD file for all ISO files found
                            foreach (string file in isoFiles)
                                ISO2IRD(file);
                        }
                        else
                        {
                            // Check that given file exists
                            if (!File.Exists(isoPath)) throw new ArgumentException("Not a valid file or directory");

                            // Save to given output path, if only 1 IRD is being created
                            if (opt.ISOPath.Count() == 1 && opt.IRDPath != null && opt.IRDPath != "")
                            {
                                string irdPath = ISO2IRD(isoPath, opt.IRDPath, opt.Key, opt.KeyFile, opt.GetKeyLog, opt.Layerbreak);
                                if (irdPath != null)
                                    Console.WriteLine($"IRD saved to {irdPath}");
                            }
                            else
                            {
                                string irdPath = ISO2IRD(isoPath, null, opt.Key, opt.KeyFile, opt.GetKeyLog, opt.Layerbreak);
                                if (irdPath != null)
                                    Console.WriteLine($"IRD saved to {irdPath}");
                            }
                        }
                    }

                    break;

                // Process options from an `info` command
                case InfoOptions opt:

                    // Clear the output file path if it exists
                    if (opt.OutPath != null && opt.OutPath != "")
                        File.Delete(opt.OutPath);

                    foreach (string filePath in opt.InPath)
                    {

                        // If directory, search for all ISOs in current directory
                        if (Directory.Exists(filePath))
                        {
                            // If recurse option enabled, search recursively
                            IEnumerable<string> irdFiles;
                            IEnumerable<string> isoFiles;
                            if (opt.Recurse)
                            {
                                if (filePath == ".")
                                    Console.WriteLine($"Recursively searching for IRDs and ISOs in current directory...\n");
                                else
                                    Console.WriteLine($"Recursively searching for IRDs and ISOs in {filePath}...\n");
                                irdFiles = Directory.EnumerateFiles(filePath, "*.ird", SearchOption.AllDirectories);
                                isoFiles = Directory.EnumerateFiles(filePath, "*.iso", SearchOption.AllDirectories);
                            }
                            else
                            {
                                if (filePath == ".")
                                    Console.WriteLine($"Searching for IRDs and ISOs in current directory...\n");
                                else
                                    Console.WriteLine($"Searching for IRDs and ISOs in {filePath}...\n");
                                irdFiles = Directory.EnumerateFiles(filePath, "*.ird", SearchOption.TopDirectoryOnly);
                                isoFiles = Directory.EnumerateFiles(filePath, "*.iso", SearchOption.TopDirectoryOnly);
                            }

                            // Warn if no files are found
                            if (!isoFiles.Any() && !irdFiles.Any())
                                Console.WriteLine("No IRDs or ISOs found (ensure .ird and .iso extensions)");

                            // Open JSON object
                            if (opt.Json)
                            {
                                if (opt.OutPath != null && opt.OutPath != "")
                                    File.AppendAllText(opt.OutPath, "{\n");
                                else
                                    Console.WriteLine('{');
                            }

                            // Print info from all IRDs
                            bool noISO = !isoFiles.Any();
                            string lastIRD = irdFiles.Last();
                            foreach (string file in irdFiles)
                            {
                                PrintInfo(file, opt.Json, (noISO && file.Equals(lastIRD)), opt.OutPath);
                            }


                            // Print info from all ISOs
                            string lastISO = isoFiles.Last();
                            foreach (string file in isoFiles)
                            {
                                try
                                {
                                    PrintISO(file, opt.Json, file.Equals(lastISO), opt.OutPath);
                                }
                                catch (InvalidFileSystemException)
                                {
                                    // Not a valid ISO file despite extension, assume file is an IRD
                                    if (!opt.Json)
                                        Console.WriteLine($"{file} is not a valid ISO file\n");
                                }
                            }

                            // Close JSON object
                            if (opt.Json)
                            {
                                if (opt.OutPath != null && opt.OutPath != "")
                                    File.AppendAllText(opt.OutPath, "}\n");
                                else
                                    Console.WriteLine('}');
                            }

                            if (opt.OutPath != null && opt.OutPath != "")
                                Console.WriteLine($"Info saved to {opt.OutPath}");
                        }
                        else
                        {
                            // Check that given file exists
                            if (!File.Exists(filePath)) throw new ArgumentException($"{filePath} is not a valid file or directory");

                            // Print info from given file
                            PrintInfo(filePath, opt.Json, true, opt.OutPath);

                            if (opt.OutPath != null && opt.OutPath != "")
                                Console.WriteLine($"Info saved to {opt.OutPath}");
                        }
                    }

                    break;

                // Unknown command
                default:
                    break;
            }
        }

        /// <summary>
        /// Prints info about a file
        /// </summary>
        /// <param name="inPath">File to retrieve info from</param>
        /// <param name="json">Whether to format output as JSON (true) or plain text (false)</param>
        /// <param name="outPath">File to output info to</param>
        public static void PrintInfo(string inPath, bool json, bool single = true, string outPath = null)
        {
            // Check if file is an ISO
            bool isISO = String.Compare(Path.GetExtension(inPath), ".iso", StringComparison.OrdinalIgnoreCase) == 0;
            if (isISO)
            {
                try
                {
                    PrintISO(inPath, json, single, outPath);
                    return;
                }
                catch (InvalidFileSystemException)
                {
                    // Not a valid ISO file despite extension, try open as IRD
                }
            }

            // Assume it is an IRD file
            try
            {
                if (json)
                {
                    IRD ird = IRD.Read(inPath);
                    if (outPath != null)
                        File.AppendAllText(outPath, $"\"{Path.GetFileName(inPath)}\": ");
                    else
                        Console.Write($"\"{Path.GetFileName(inPath)}\": ");
                    ird.PrintJson(outPath, single);
                }
                else
                    IRD.Read(inPath).Print(outPath, Path.GetFileName(inPath));

                if (json)
                    return;
                return;
            }
            catch (InvalidDataException)
            {
                // Not a valid IRD file despite extension, give up
                if (json)
                    return;
                if (isISO)
                    Console.WriteLine($"{inPath} is not a valid ISO file\n");
                else
                    Console.WriteLine($"{inPath} is not a valid IRD file\n");
            }
        }

        /// <summary>
        /// Print information about ISO file
        /// </summary>
        /// <param name="isoPath">Path to ISO file</param>
        /// <param name="json">Whether to format output as JSON (true) or plain text (false)</param>
        /// <param name="outPath">File to output info to</param>
        /// <exception cref="FileNotFoundException"></exception>
        /// <exception cref="InvalidFileSystemException"></exception>
        public static void PrintISO(string isoPath, bool json, bool single = true, string outPath = null)
        {
            // Open ISO file for reading
            using FileStream fs = new FileStream(isoPath, FileMode.Open, FileAccess.Read) ?? throw new FileNotFoundException(isoPath);
            // Validate ISO file stream
            if (!CDReader.Detect(fs))
                throw new InvalidFileSystemException($"{isoPath} is not a valid ISO file");
            // Create new ISO reader
            using CDReader reader = new(fs, true, true);

            // Write PS3_DISC.SFB info
            try
            {
                using DiscUtils.Streams.SparseStream s = reader.OpenFile("PS3_DISC.SFB", FileMode.Open, FileAccess.Read);
                PS3_DiscSFB ps3_DiscSFB = new(s);
                if (json)
                {
                    // Begin JSON object
                    if (json)
                    {
                        if (outPath != null)
                            File.AppendAllText(outPath, $"\"{Path.GetFileName(isoPath)}\": {{\n");
                        else
                            Console.WriteLine($"\"{Path.GetFileName(isoPath)}\": {{");
                    }

                    // Print PS3_DISC.SFB info
                    if (outPath != null)
                        File.AppendAllText(outPath, "\"PS3_DISC.SFB\": ");
                    else
                        Console.Write("\"PS3_DISC.SFB\": ");
                    ps3_DiscSFB.PrintJson(outPath);
                    if (outPath != null)
                        File.AppendAllText(outPath, ",\n");
                    else
                        Console.WriteLine(',');
                }
                else
                    ps3_DiscSFB.Print(outPath, Path.GetFileName(isoPath));
            }
            catch (FileNotFoundException)
            {
                if (!json)
                    Console.WriteLine($"{isoPath} is not a valid PS3 ISO file\n");
                return;
            }

            // Write PARAM.SFO info
            try
            {
                using DiscUtils.Streams.SparseStream s = reader.OpenFile(Path.Combine("PS3_GAME", "PARAM.SFO"), FileMode.Open, FileAccess.Read);
                ParamSFO paramSFO = new(s);
                if (json)
                {
                    if (outPath != null)
                        File.AppendAllText(outPath, "\"PARAM.SFO\": ");
                    else
                        Console.Write("\"PARAM.SFO\": ");
                    paramSFO.PrintJson(outPath);
                }
                else
                    paramSFO.Print(outPath, Path.GetFileName(isoPath));
            }
            catch (FileNotFoundException)
            {
                if (!json)
                    Console.WriteLine($"\\PS3_GAME\\PARAM.SFO not found in {isoPath}\n");
            }

            // End JSON object
            if (json)
            {
                if (single)
                {
                    if (outPath != null)
                        File.AppendAllText(outPath, "\n}\n");
                    else
                        Console.WriteLine("\n}");
                }
                else
                {
                    if (outPath != null)
                        File.AppendAllText(outPath, "\n},\n");
                    else
                        Console.WriteLine("\n},");
                }
            }
        }

        /// <summary>
        /// Creates an IRD file from an ISO file
        /// </summary>
        /// <param name="isoPath">Path to an ISO file</param>
        /// <param name="irdPath">Path to IRD file to be created (optional)</param>
        /// <param name="hexKey">Hex string disc key</param>
        /// <param name="keyPath">Disc key file (overridden by hex string if present)</param>
        /// <param name="getKeyLog">GetKey log file (overridden by disc key or key file if present)</param>
        /// <param name="layerbreak">Layerbreak value of disc</param>
        public static string ISO2IRD(string isoPath, string irdPath = null, string hexKey = null, string keyPath = null, string getKeyLog = null, long? layerbreak = null)
        {
            // Check file exists
            FileInfo iso = new(isoPath);
            if (!iso.Exists)
            {
                Console.WriteLine($"{nameof(isoPath)} is not a valid file or directory");
                return null;
            }

            // Determine IRD path if none given
            irdPath ??= Path.ChangeExtension(isoPath, ".ird");

            // Create new reproducible redump-style IRD with a given hex key
            if (hexKey != null)
            {
                try
                {
                    // Get disc key from hex string
                    byte[] discKey = Convert.FromHexString(hexKey);
                    if (discKey == null || discKey.Length != 16)
                        throw new ArgumentException(hexKey);

                    Console.WriteLine($"Creating {irdPath} with Key: {hexKey}");
                    IRD ird1 = new ReIRD(isoPath, discKey, layerbreak);
                    ird1.Write(irdPath);
                    ird1.Print();
                    return irdPath;
                }
                catch (ArgumentException)
                {
                    Console.Error.WriteLine($"{hexKey} is not a valid key, detecting key automatically...");
                }
                catch (FileNotFoundException)
                {
                    Console.Error.WriteLine("File not found, failed to create IRD");
                    return null;
                }
            }

            // Create new reproducible redump-style IRD with a given key file
            if (keyPath != null)
            {
                // Read key from .key file
                byte[] discKey = File.ReadAllBytes(keyPath);
                try
                {
                    IRD ird2 = new ReIRD(isoPath, discKey, layerbreak);
                    Console.WriteLine($"Creating {irdPath} with Key: {Convert.ToHexString(discKey)}");
                    ird2.Write(irdPath);
                    ird2.Print();
                    return irdPath;
                }
                catch (ArgumentException)
                {
                    Console.Error.WriteLine($"{Convert.ToHexString(discKey)} is not a valid key, detecting key automatically...");
                }
                catch (FileNotFoundException)
                {
                    Console.Error.WriteLine("File not found, failed to create IRD");
                    return null;
                }
            }

            // Create new reproducible redump-style IRD with a given GetKey log
            if (getKeyLog != null)
            {
                try
                {
                    Console.WriteLine($"Creating {irdPath} with key from: {getKeyLog}");
                    IRD ird3 = new ReIRD(isoPath, getKeyLog);
                    ird3.Write(irdPath);
                    ird3.Print();
                    return irdPath;
                }
                catch (FileNotFoundException)
                {
                    Console.Error.WriteLine("File not found, failed to create IRD");
                    return null;
                }
            }

            // No key provided, try search for .key file
            string keyfilePath = Path.ChangeExtension(isoPath, ".key");
            FileInfo keyFile = new(keyfilePath);
            if (keyFile.Exists)
            {
                // Found .key file, try use it
                try
                {
                    // Read key from .key file
                    byte[] discKey = File.ReadAllBytes(keyfilePath);
                    if (discKey == null || discKey.Length != 16)
                        throw new ArgumentException(keyfilePath);

                    Console.WriteLine($"Creating {irdPath} with Key: {Convert.ToHexString(discKey)}");
                    IRD ird2 = new ReIRD(isoPath, discKey, layerbreak);
                    ird2.Write(irdPath);
                    ird2.Print();
                    return irdPath;
                }
                catch (ArgumentException)
                {
                    Console.Error.WriteLine("Given key file not valid, detecting key automatically...");
                }
                catch (FileNotFoundException)
                {
                    Console.Error.WriteLine("File not found, failed to create IRD");
                    return null;
                }
            }

            // No key provided, try search for .getkey.log file
            string logfilePath = Path.ChangeExtension(isoPath, ".getkey.log");
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
                    return irdPath;
                }
                catch (FileNotFoundException)
                {
                    Console.Error.WriteLine("File not found, failed to create IRD");
                    return null;
                }
            }

            // No key provided, try get key from redump.org
            Console.WriteLine("No key provided... Searching for key on redump.org");

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
                Console.WriteLine("ISO not found in redump, cannot automatically retreive key");
                return null;
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
                    Console.WriteLine("ISO not found in redump, cannot automatically retreive key");
                    return null;
                }
                else if (ids2.Count > 1)
                {
                    Console.WriteLine("Cannot automatically get key from redump. Please search redump.org and run again with -k");
                    return null;
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
            IRD ird = new ReIRD(isoPath, key, layerbreak);
            ird.Write(irdPath);
            ird.Print();
            return irdPath;
        }
    }
}
