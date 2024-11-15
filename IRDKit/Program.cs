using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using CommandLine;
using LibIRD;
using SabreTools.Hashing;
using SabreTools.RedumpLib.Web;

namespace IRDKit
{
    internal class Program
    {
        #region Options

        /// <summary>
        /// IRD Creation command
        /// </summary>
        [Verb("create", HelpText = "Create an IRD from an ISO")]
        public class CreateOptions
        {
            [Value(0, Required = true, HelpText = "Path to ISO file(s), or directory of ISO files")]
            public IEnumerable<string> ISOPath { get; set; }

            [Option('o', "output", HelpText = "Path to the IRD file to be created (will overwrite)")]
            public string IRDPath { get; set; }

            [Option('b', "layerbreak", HelpText = "Layerbreak value in bytes (use with BD-Video hybrid discs). Default: 12219392")]
            public long? Layerbreak { get; set; }

            [Option('k', "key", HelpText = "Hexadecimal representation of the disc key")]
            public string Key { get; set; }

            [Option('l', "getkey-log", HelpText = "Path to a .getkey.log file")]
            public string GetKeyLog { get; set; }

            [Option('f', "key-file", HelpText = "Path to a redump .key file")]
            public string KeyFile { get; set; }

            [Option('r', "recurse", HelpText = "Recurse through all subdirectories and generate IRDs for all ISOs")]
            public bool Recurse { get; set; }

            [Option('v', "verbose", HelpText = "Print more information during IRD creation")]
            public bool Verbose { get; set; }
        }

        /// <summary>
        /// IRD or ISO information command
        /// </summary>
        [Verb("info", HelpText = "Print information from an IRD or ISO")]
        public class InfoOptions
        {
            [Value(0, Required = true, HelpText = "Path to IRD/ISO file(s), or directory of IRD/ISO files")]
            public IEnumerable<string> InPath { get; set; }

            [Option('o', "output", HelpText = "Path to the text or json file to be created (will overwrite)")]
            public string OutPath { get; set; }

            [Option('j', "json", HelpText = "Print IRD or ISO information as a JSON object")]
            public bool Json { get; set; }

            [Option('r', "recurse", HelpText = "Recurse through all subdirectories and print information for all ISOs and IRDs")]
            public bool Recurse { get; set; }
        }

        /// <summary>
        /// IRD diff command
        /// </summary>
        [Verb("diff", HelpText = "Compare two IRDs and print their differences")]
        public class DiffOptions
        {
            [Value(0, Required = true, HelpText = "Path to the first IRD to compare against")]
            public string InPath1 { get; set; }

            [Value(1, Required = true, HelpText = "Path to the second IRD file to compare")]
            public string InPath2 { get; set; }

            [Option('o', "output", HelpText = "Path to the text or json file to be created (will overwrite)")]
            public string OutPath { get; set; }
        }

        /// <summary>
        /// IRD rename command
        /// </summary>
        [Verb("rename", HelpText = "Rename one or more IRD files according to the redump PS3 DAT")]
        public class RenameOptions
        {
            [Value(0, Required = true, HelpText = "Path to IRD file(s), or directory of IRD files")]
            public IEnumerable<string> IRDPath { get; set; }

            [Option('d', "datfile", Required = true, HelpText = "Path to the redump PS3 Datfile")]
            public string DATPath { get; set; }

            [Option('s', "serial", HelpText = "Appends disc serial to new IRD filename")]
            public bool Serial { get; set; }

            [Option('e', "version", HelpText = "Appends disc version to new IRD filename")]
            public bool Ver { get; set; }

            [Option('c', "crc", HelpText = "Appends ISO CRC to new IRD filename")]
            public bool CRC { get; set; }

            [Option('r', "recurse", HelpText = "Recurse through all subdirectories and rename all IRDs")]
            public bool Recurse { get; set; }

            [Option('v', "verbose", HelpText = "Print more information about the renaming")]
            public bool Verbose { get; set; }
        }

        #endregion

        #region Program

        /// <summary>
        /// Parse command line arguments
        /// </summary>
        /// <param name="args">Command line arguments</param>
        public static void Main(string[] args)
        {
            // Ensure console prints foreign characters properly
            Console.OutputEncoding = Encoding.UTF8;

            // Parse arguments
            var result = Parser.Default.ParseArguments<CreateOptions, InfoOptions, DiffOptions, RenameOptions>(args).WithParsed(Run);
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
                    if (opt.ISOPath == null || !opt.ISOPath.Any())
                    {
                        Console.Error.WriteLine("Provide a valid ISO path to create an IRD");
                        return;
                    }

                    foreach (string isoPath in opt.ISOPath)
                    {
                        // Validate ISO path
                        if (string.IsNullOrEmpty(isoPath))
                            continue;

                        // If directory, search for all ISOs in current directory
                        if (Directory.Exists(isoPath))
                        {
                            // If recurse option enabled, search recursively
                            IEnumerable<string> isoFiles;
                            if (opt.Recurse)
                            {
                                if (opt.Verbose && isoPath == ".")
                                    Console.WriteLine($"Recursively searching for ISOs in current directory");
                                else if (opt.Verbose)
                                    Console.WriteLine($"Recursively searching for ISOs in {isoPath}");
                                isoFiles = Directory.EnumerateFiles(isoPath, "*.iso", SearchOption.AllDirectories);
                            }
                            else
                            {
                                if (opt.Verbose && isoPath == ".")
                                    Console.WriteLine($"Searching for ISOs in current directory");
                                else if (opt.Verbose)
                                    Console.WriteLine($"Searching for ISOs in {isoPath}");
                                isoFiles = Directory.EnumerateFiles(isoPath, "*.iso", SearchOption.TopDirectoryOnly);
                            }

                            // Warn if no files are found
                            if (!isoFiles.Any())
                            {
                                if (opt.Recurse)
                                    Console.Error.WriteLine($"No ISOs found in {isoPath} (ensure .iso extension)");
                                else
                                    Console.Error.WriteLine($"No ISOs found in {isoPath} (ensure .iso extension, or try use -r)");
                                continue;
                            }

                            // Determine output IRD folder
                            string outputPath = opt.IRDPath;
                            if (File.Exists(opt.IRDPath))
                                outputPath = Path.GetDirectoryName(opt.IRDPath);

                            // Create an IRD file for all ISO files found
                            foreach (string file in isoFiles.OrderBy(x => x))
                                ISO2IRD(file, irdPath: outputPath, keyPath: opt.KeyFile, verbose: opt.Verbose);
                        }
                        else
                        {
                            // Check that given file exists
                            if (!File.Exists(isoPath))
                            {
                                Console.Error.WriteLine($"ISO not found: {isoPath}");
                                continue;
                            }

                            string irdPath;
                            // Save to given output path and filename, if only 1 IRD is being created
                            if (opt.ISOPath.Count() == 1)
                                irdPath = ISO2IRD(isoPath, irdPath: opt.IRDPath, hexKey: opt.Key, keyPath: opt.KeyFile, getKeyLog: opt.GetKeyLog, layerbreak: opt.Layerbreak, verbose: opt.Verbose);
                            // Save to given output path, if more than 1 IRD is being created
                            else
                                irdPath = ISO2IRD(isoPath, irdPath: Path.GetDirectoryName(opt.IRDPath), verbose: opt.Verbose);

                            if (irdPath != null)
                                Console.WriteLine($"IRD saved to {irdPath}");
                        }
                    }

                    break;

                // Process options from an `info` command
                case InfoOptions opt:

                    // Validate required parameter
                    if (opt.InPath == null || !opt.InPath.Any())
                    {
                        Console.Error.WriteLine("Provide a valid ISO or IRD path to print info about");
                        return;
                    }

                    // Clear the output file path if it exists
                    if (opt.OutPath != null && opt.OutPath != "")
                        File.Delete(opt.OutPath);

                    foreach (string filePath in opt.InPath)
                    {
                        // Validate path
                        if (string.IsNullOrEmpty(filePath))
                            continue;

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
                            {
                                Console.Error.WriteLine("No IRDs or ISOs found (ensure .ird and .iso extensions)");
                                return;
                            }

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
                                catch (IOException)
                                {
                                    // Not a valid ISO file despite extension, assume file is an IRD
                                    if (!opt.Json)
                                        Console.Error.WriteLine($"{file} is not a valid ISO file\n");
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
                            if (!File.Exists(filePath))
                            {
                                Console.Error.WriteLine($"{filePath} is not a valid file");
                                continue;
                            }

                            // Print info from given file
                            PrintInfo(filePath, opt.Json, true, opt.OutPath);

                            if (opt.OutPath != null && opt.OutPath != "")
                                Console.WriteLine($"Info saved to {opt.OutPath}");
                        }
                    }

                    break;

                // Process options from a `diff` command
                case DiffOptions opt:

                    // Validate required parameters
                    if (opt.InPath1 == null || opt.InPath2 == null || !File.Exists(opt.InPath1) || !File.Exists(opt.InPath2))
                    {
                        Console.Error.WriteLine("Provide two paths to IRDs to compare");
                        return;
                    }

                    // Clear the output file path if it exists
                    if (opt.OutPath != null && opt.OutPath != "")
                        File.Delete(opt.OutPath);

                    // Compare the two IRDs
                    PrintDiff(opt.InPath1, opt.InPath2, opt.OutPath);

                    if (opt.OutPath != null && opt.OutPath != "")
                        Console.WriteLine($"Diff saved to {opt.OutPath}");

                    break;

                // Process options from a 'rename' command
                case RenameOptions opt:

                    // Validate required parameters
                    if (opt.IRDPath == null || !opt.IRDPath.Any())
                    {
                        Console.Error.WriteLine("Provide a valid IRD path to rename");
                        return;
                    }

                    // Read DAT file
                    XDocument datfile = DatParser(opt.DATPath);
                    if (datfile == null)
                    {
                        Console.Error.WriteLine("Unable to parse DAT file");
                        return;
                    }

                    foreach (string irdPath in opt.IRDPath)
                    {
                        // Validate IRD path
                        if (string.IsNullOrEmpty(irdPath))
                            continue;

                        // If directory, search for all ISOs in current directory
                        if (Directory.Exists(irdPath))
                        {
                            // If recurse option enabled, search recursively
                            IEnumerable<string> irdFiles;
                            if (opt.Recurse)
                            {
                                if (opt.Verbose && irdPath == ".")
                                    Console.WriteLine($"Recursively renaming IRDs in current directory");
                                else if (opt.Verbose)
                                    Console.WriteLine($"Recursively renaming IRDs in {irdPath}");
                                irdFiles = Directory.EnumerateFiles(irdPath, "*.ird", SearchOption.AllDirectories);
                            }
                            else
                            {
                                if (opt.Verbose && irdPath == ".")
                                    Console.WriteLine($"Renaming IRDs in current directory");
                                else if (opt.Verbose)
                                    Console.WriteLine($"Renaming IRDs in {irdPath}");
                                irdFiles = Directory.EnumerateFiles(irdPath, "*.ird", SearchOption.TopDirectoryOnly);
                            }

                            // Warn if no files are found
                            if (!irdFiles.Any())
                            {
                                if (opt.Recurse)
                                    Console.Error.WriteLine($"No IRDs found in {irdPath} (ensure .ird extension)");
                                else
                                    Console.Error.WriteLine($"No IRDs found in {irdPath} (ensure .ird extension, or try use -r)");
                                continue;
                            }

                            // Rename all IRD files found
                            foreach (string file in irdFiles.OrderBy(x => x))
                            {
                                try
                                {
                                    RenameIRD(file, datfile, serial: opt.Serial, ver: opt.Ver, crc: opt.CRC, verbose: opt.Verbose);
                                }
                                catch (Exception e)
                                {
                                    Console.Error.WriteLine(e);
                                }
                            }

                        }
                        else
                        {
                            // Check that given file exists
                            if (!File.Exists(irdPath))
                            {
                                Console.Error.WriteLine($"IRD not found: {irdPath}");
                                continue;
                            }

                            // Rename provided IRD path
                            try
                            {
                                RenameIRD(irdPath, datfile, serial: opt.Serial, ver: opt.Ver, crc: opt.CRC, verbose: opt.Verbose);
                            }
                            catch (Exception e)
                            {
                                Console.Error.WriteLine(e);
                            }
                        }
                    }

                    break;

                // Unknown command
                default:
                    break;
            }
        }

        #endregion

        #region Functionality

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
                catch (IOException)
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
                    Console.Error.WriteLine($"{inPath} is not a valid ISO file\n");
                else
                    Console.Error.WriteLine($"{inPath} is not a valid IRD file\n");
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
            using FileStream fs = new(isoPath, FileMode.Open, FileAccess.Read);
            // Validate ISO file stream
            if (fs == null || !LibIRD.DiscUtils.Iso9660.CDReader.Detect(fs))
            {
                Console.Error.WriteLine($"{isoPath} is not a valid ISO file");
                return;
            }
            // Create new ISO reader
            using LibIRD.DiscUtils.Iso9660.CDReader reader = new(fs);

            // Write PS3_DISC.SFB info
            try
            {
                using LibIRD.DiscUtils.Streams.SparseStream s = reader.OpenFile("\\PS3_DISC.SFB", FileMode.Open, FileAccess.Read);
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
                    Console.Error.WriteLine($"{isoPath} is not a valid PS3 ISO file\n");
                return;
            }

            // Write PARAM.SFO info
            try
            {
                using LibIRD.DiscUtils.Streams.SparseStream s = reader.OpenFile("\\PS3_GAME\\PARAM.SFO", FileMode.Open, FileAccess.Read);
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
                    Console.Error.WriteLine($"\\PS3_GAME\\PARAM.SFO not found in {isoPath}\n");
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
        /// Prints the differences between two IRD files
        /// </summary>
        /// <param name="irdPath1">First IRD path to compare against</param>
        /// <param name="irdPath2">Second IRD path to compare against</param>
        /// <param name="outPath">File to write comparison to, null if print to Console</param>
        public static void PrintDiff(string irdPath1, string irdPath2, string outPath = null)
        {
            // Check they are different IRDs
            if (Path.GetFullPath(irdPath1) == Path.GetFullPath(irdPath2))
            {
                Console.Error.WriteLine("Provide two different IRDs for a diff");
                return;
            }

            // Parse each IRD
            IRD IRD1 = IRD.Read(irdPath1);
            IRD IRD2 = IRD.Read(irdPath2);

            // Build a formatted diff
            StringBuilder printText = new();

            // Print any version difference
            if (IRD1.Version != IRD2.Version)
                printText.AppendLine($"Version: {IRD1.Version} vs {IRD2.Version}");

            // Print any title ID difference
            if (IRD1.TitleID != IRD2.TitleID)
                printText.AppendLine($"TitleID: {IRD1.TitleID} vs {IRD2.TitleID}");

            // Print any title difference
            if (IRD1.Title != IRD2.Title)
                printText.AppendLine($"Title: \"{IRD1.Title}\" vs \"{IRD2.Title}\"");

            // Print any system version difference
            if (IRD1.SystemVersion != IRD2.SystemVersion)
                printText.AppendLine($"PUP Version: {IRD1.SystemVersion} vs {IRD2.SystemVersion}");

            // Print any disc version difference
            if (IRD1.DiscVersion != IRD2.DiscVersion)
                printText.AppendLine($"Disc Version: {IRD1.DiscVersion} vs {IRD2.DiscVersion}");

            // Print any app version difference
            if (IRD1.AppVersion != IRD2.AppVersion)
                printText.AppendLine($"App Version: {IRD1.AppVersion} vs {IRD2.AppVersion}");

            // Un-gzip the headers to compare them
            byte[] header1 = Decompress(IRD1.Header);
            byte[] header2 = Decompress(IRD2.Header);

            // Print the difference in header length, if not 0
            if (header1.Length != header2.Length)
                printText.AppendLine($"Header Length: {header1.Length} vs {header2.Length}");

            // Print number of bytes that the headers differ by, if not 0
            int headerDiff;
            if (header1.Length < header2.Length)
                headerDiff = header2.Length - header1.Length + header1.Where((x, i) => x != header2[i]).Count();
            else
                headerDiff = header1.Length - header2.Length + header2.Where((x, i) => x != header1[i]).Count();
            if (headerDiff != 0)
                printText.AppendLine($"Header: Differs by {headerDiff} bytes");

            // Un-gzip the footers to compare them
            byte[] footer1 = Decompress(IRD1.Footer);
            byte[] footer2 = Decompress(IRD2.Footer);

            // Print the difference in footer length, if not 0
            if (footer1.Length != footer2.Length)
                printText.AppendLine($"Footer Length: {footer1.Length} vs {footer2.Length}");

            // Print number of bytes that the footers differ by, if not 0
            int footerDiff;
            if (footer1.Length < footer2.Length)
                footerDiff = footer2.Length - footer1.Length + footer1.Where((x, i) => x != footer2[i]).Count();
            else
                footerDiff = footer1.Length - footer2.Length + footer2.Where((x, i) => x != footer1[i]).Count();
            if (footerDiff != 0)
                printText.AppendLine($"Footer: Differs by {footerDiff} bytes");

            // Print the difference in number of regions, if not 0
            if (IRD1.RegionCount != IRD2.RegionCount)
                printText.AppendLine($"Region Count: {IRD1.RegionCount} vs {IRD2.RegionCount}");

            // Print any differences in region hashes
            int regionCount = IRD2.RegionCount < IRD1.RegionCount ? IRD2.RegionCount : IRD1.RegionCount;
            if (regionCount > IRD1.RegionHashes.Length)
                regionCount = IRD1.RegionHashes.Length;
            if (regionCount > IRD2.RegionHashes.Length)
                regionCount = IRD2.RegionHashes.Length;
            for (int i = 0; i < regionCount; i++)
            {
                if (!IRD1.RegionHashes[i].SequenceEqual(IRD2.RegionHashes[i]))
                    printText.AppendLine($"Region {i} Hash: {LibIRD.IRD.ByteArrayToHexString(IRD1.RegionHashes[i])} vs {LibIRD.IRD.ByteArrayToHexString(IRD2.RegionHashes[i])}");
            }

            // Print the difference in number of files, if not 0
            if (IRD1.FileCount != IRD2.FileCount)
                printText.AppendLine($"File Count: {IRD1.FileCount} vs {IRD2.FileCount}");

            // Print the mismatch file hashes, for each file offset at which they differ
            List<long> missingOffsets1 = [];
            List<long> missingOffsets2 = [];
            for (int i = 0; i < IRD1.FileKeys.Length; i++)
            {
                int j = Array.FindIndex(IRD2.FileKeys, element => element == IRD1.FileKeys[i]);
                if (j == -1)
                    missingOffsets2.Add(IRD1.FileKeys[i]);
                if (j != -1 && !IRD1.FileHashes[i].SequenceEqual(IRD2.FileHashes[j]))
                    printText.AppendLine($"File Hash at Offset {IRD1.FileKeys[i]}: {LibIRD.IRD.ByteArrayToHexString(IRD1.FileHashes[i])} vs {LibIRD.IRD.ByteArrayToHexString(IRD2.FileHashes[j])}");
            }
            for (int i = 0; i < IRD2.FileKeys.Length; i++)
            {
                int j = Array.FindIndex(IRD1.FileKeys, element => element == IRD2.FileKeys[i]);
                if (j == -1)
                    missingOffsets1.Add(IRD2.FileKeys[i]);
            }
            // Print the file offsets that differ
            if (missingOffsets1.Count > 0)
                printText.AppendLine($"File Offsets not Present in {irdPath1}: {string.Join(", ", missingOffsets1)}");
            if (missingOffsets2.Count > 0)
                printText.AppendLine($"File Offsets not Present in {irdPath2}: {string.Join(", ", missingOffsets2)}");

            // Print any extra config data difference
            if (IRD1.ExtraConfig != IRD2.ExtraConfig)
                printText.AppendLine($"Extra Config: {IRD1.ExtraConfig:X4} vs {IRD2.ExtraConfig:X4}");

            // Print any attachments data difference
            if (IRD1.Attachments != IRD2.Attachments)
                printText.AppendLine($"Attachments: {IRD1.Attachments:X4} vs {IRD2.Attachments:X4}");

            // Print any unique ID difference
            if (IRD1.UID != IRD2.UID)
                printText.AppendLine($"Unique ID: {IRD1.UID:X8} vs {IRD2.UID:X8}");

            // Print any data 1 key difference
            if (!IRD1.Data1Key.SequenceEqual(IRD2.Data1Key))
                printText.AppendLine($"Data 1 Key: {LibIRD.IRD.ByteArrayToHexString(IRD1.Data1Key)} vs {LibIRD.IRD.ByteArrayToHexString(IRD2.Data1Key)}");

            // Print any data 2 key difference
            if (!IRD1.Data2Key.SequenceEqual(IRD2.Data2Key))
                printText.AppendLine($"Data 2 Key: {LibIRD.IRD.ByteArrayToHexString(IRD1.Data2Key)} vs {LibIRD.IRD.ByteArrayToHexString(IRD2.Data2Key)}");

            // Print any PIC difference
            if (!IRD1.PIC.SequenceEqual(IRD2.PIC))
                printText.AppendLine($"PIC: {LibIRD.IRD.ByteArrayToHexString(IRD1.PIC)} vs {LibIRD.IRD.ByteArrayToHexString(IRD2.PIC)}");

            // Write formatted string to file if output path provided, otherwise to console
            if (outPath != null)
                File.AppendAllText(outPath, printText.ToString());
            else
                Console.WriteLine(printText.ToString());
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
        public static string ISO2IRD(string isoPath, string irdPath = null, string hexKey = null, string keyPath = null, string getKeyLog = null, long? layerbreak = null, bool verbose = false)
        {
            // Check file exists
            FileInfo iso;
            try
            {
                iso = new(isoPath);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.Message + ", failed to create IRD");
                return null;
            }
            if (!iso.Exists)
            {
                Console.Error.WriteLine($"{nameof(isoPath)} is not a valid file or directory");
                return null;
            }

            // Determine IRD path if only folder given
            if (Directory.Exists(irdPath))
                irdPath = Path.Combine(irdPath, Path.GetFileName(Path.ChangeExtension(isoPath, ".ird")));

            // Determine IRD path if none given
            if (irdPath == string.Empty)
                irdPath = Path.GetFileName(Path.ChangeExtension(isoPath, ".ird"));
            irdPath ??= Path.ChangeExtension(isoPath, ".ird");

            // Create new reproducible redump-style IRD with a given hex key
            if (hexKey != null)
            {
                try
                {
                    // Get disc key from hex string
                    byte[] discKey = LibIRD.IRD.HexStringToByteArray(hexKey);
                    if (discKey == null || discKey.Length != 16)
                        Console.Error.WriteLine($"{hexKey} is not a valid key, detecting key automatically...");
                    else
                    {
                        Console.WriteLine($"Creating {irdPath} with Key: {hexKey}");
                        IRD ird1 = new ReIRD(isoPath, discKey, layerbreak);
                        ird1.Write(irdPath);
                        if (verbose)
                            ird1.Print();
                        return irdPath;
                    }
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine(e.Message + ", failed to create IRD");
                    return null;
                }
            }

            // Create new reproducible redump-style IRD with a given key file
            if (keyPath != null)
            {
                try
                {
                    // If key directory was given, append filename and .key
                    if (Directory.Exists(keyPath))
                    {
                        keyPath = Path.Combine(keyPath, Path.ChangeExtension(Path.GetFileName(isoPath), ".key"));
                    }
                    // Read key from .key file
                    byte[] discKey = File.ReadAllBytes(keyPath);
                    if (discKey == null || discKey.Length != 16)
                        Console.Error.WriteLine($"{hexKey} is not a valid key, detecting key automatically...");
                    else
                    {
                        Console.WriteLine($"Creating {irdPath} with Key: {LibIRD.IRD.ByteArrayToHexString(discKey)}");
                        IRD ird1 = new ReIRD(isoPath, discKey, layerbreak);
                        ird1.Write(irdPath);
                        if (verbose)
                            ird1.Print();
                        return irdPath;
                    }
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine(e.Message + ", failed to create IRD");
                    return null;
                }
            }

            // Create new reproducible redump-style IRD with a given GetKey log
            if (getKeyLog != null)
            {
                try
                {
                    Console.WriteLine($"Creating {irdPath} with key from: {getKeyLog}");
                    IRD ird1 = new ReIRD(isoPath, getKeyLog);
                    ird1.Write(irdPath);
                    if (verbose)
                        ird1.Print();
                    return irdPath;
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine(e.Message + ", failed to create IRD");
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
                        Console.Error.WriteLine($"{hexKey} is not a valid key, detecting key automatically...");
                    else
                    {
                        Console.WriteLine($"Creating {irdPath} with Key: {LibIRD.IRD.ByteArrayToHexString(discKey)}");
                        IRD ird1 = new ReIRD(isoPath, discKey, layerbreak);
                        ird1.Write(irdPath);
                        if (verbose)
                            ird1.Print();
                        return irdPath;
                    }
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine(e.Message + ", failed to create IRD");
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
                    IRD ird1 = new ReIRD(isoPath, logfilePath);
                    ird1.Write(irdPath);
                    if (verbose)
                        ird1.Print();
                    return irdPath;
                }
                catch (Exception e)
                {
                    Console.Error.WriteLine(e.Message + ", failed to create IRD");
                    return null;
                }
            }

            // No key provided, try get key from redump.org
            if (verbose)
                Console.WriteLine("No key provided... Searching for key on redump.org");

            // Compute CRC32 hash
            byte[] crc32 = HashTool.GetFileHashArray(isoPath, HashType.CRC32);
            string crc32String = LibIRD.IRD.ByteArrayToHexString(crc32).ToLowerInvariant();

            // Convert to UInt for use as UID in IRD
            Array.Reverse(crc32);
            uint crc32UInt = BitConverter.ToUInt32(crc32, 0);

            // Search for ISO on redump.org
            RedumpClient redump = new();
            List<int> ids = redump.CheckSingleSitePage("http://redump.org/discs/system/ps3/quicksearch/" + crc32String).ConfigureAwait(false).GetAwaiter().GetResult();
            int id;
            if (ids.Count == 0)
            {
                Console.Error.WriteLine("ISO not found in redump and no valid key provided, cannot create IRD");
                return null;
            }
            else if (ids.Count > 1)
            {
                // More than one result for the CRC32 hash, compute SHA1 hash instead
                string sha1String = HashTool.GetFileHash(isoPath, HashType.SHA1);

                // Search redump.org for SHA1 hash
                List<int> ids2 = redump.CheckSingleSitePage("http://redump.org/discs/system/ps3/quicksearch/" + sha1String).ConfigureAwait(false).GetAwaiter().GetResult();
                if (ids2.Count == 0)
                {
                    Console.Error.WriteLine("ISO not found in redump and no valid key provided, cannot create IRD");
                    return null;
                }
                else if (ids2.Count > 1)
                {
                    Console.Error.WriteLine("Cannot automatically get key from redump. Please search redump.org and run again with -k");
                    return null;
                }
                id = ids2[0];
            }
            else
            {
                // One result found, assume it is the PS3 ISO
                id = ids[0];
            }

            // Download key file from redump.org
            byte[]? key = redump.DownloadData($"http://redump.org/disc/{id}/key").ConfigureAwait(false).GetAwaiter().GetResult();
            if (key == null || key.Length != 16)
            {
                Console.Error.WriteLine("Invalid key obtained from redump and no valid key provided, cannot create IRD");
                return null;
            }

            // Create IRD with key from redump
            Console.WriteLine($"Creating {irdPath} with Key from redump.org: {LibIRD.IRD.ByteArrayToHexString(key)}");
            try
            {
                IRD ird = new ReIRD(isoPath, key, layerbreak, crc32UInt);
                ird.Write(irdPath);
                if (verbose)
                    ird.Print();
                return irdPath;
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e.Message + ", failed to create IRD");
                return null;
            }
        }

        public static XDocument DatParser(string datpath = null)
        {
            try
            {
                if (!File.Exists(datpath))
                    return null;

                return XDocument.Load(datpath);
            }
            catch
            {
                return null;
            }
        }

        public static string GetDatFilename(IRD ird, XDocument datfile)
        {
            string crc32 = ird.UID.ToString("X8").ToLower();
            XElement node = datfile.Root.Elements("game").Where(e => e.Element("rom").Attribute("crc").Value == crc32).FirstOrDefault() ?? throw new ArgumentException("Cannot find ISO in redump DAT");
            return node.Attribute("name").Value;
        }

        public static void RenameIRD(string irdPath, XDocument datfile, bool serial = false, bool ver = false, bool crc = false, bool verbose = false)
        {
            IRD ird = IRD.Read(irdPath);

            if (ird.ExtraConfig != 0x0001)
                throw new ArgumentException($"{irdPath} is not a redump-style IRD");

            string filename = GetDatFilename(ird, datfile) ?? throw new ArgumentException($"Cannot determine DAT filename for {irdPath}");
            if (serial)
                filename += $" [{ird.TitleID.Substring(0, 4).Replace('\0', ' ')}-{ird.TitleID.Substring(4, 5).Replace('\0', ' ')}]";
            if (ver)
                filename += $" [{ird.DiscVersion.Replace('\0', ' ')}]";
            if (crc)
                filename += $" [{ird.UID:X8}]";

            // Rename irdPath to filename
            string directory = Path.GetDirectoryName(Path.GetFullPath(filename));
            string filepath;
            if (!string.IsNullOrEmpty(directory))
                filepath = Path.Combine(Path.GetDirectoryName(irdPath), filename + ".ird");
            else
                filepath = filename + ".ird";

            // Rename IRD to new name
            if (irdPath != filepath)
            {
                if (verbose)
                    Console.WriteLine($"Renaming {Path.GetFileName(irdPath)} to {Path.GetFileName(filepath)}");
                File.Move(irdPath, filepath);
            }
            else
            {
                if (verbose)
                    Console.WriteLine($"Skipping {Path.GetFileName(irdPath)}, already named correctly");
            }
        }

#endregion

        #region Helper Functions

        /// <summary>
        /// Decompress a gzipped byte array
        /// </summary>
        /// <param name="data">Gzipped byte array</param>
        /// <returns>Un-gzipped byte array</returns>
        static byte[] Decompress(byte[] data)
        {
            using var compressedStream = new MemoryStream(data);
            using var zipStream = new GZipStream(compressedStream, CompressionMode.Decompress);
            using var resultStream = new MemoryStream();
            zipStream.CopyTo(resultStream);
            return resultStream.ToArray();
        }

        #endregion
    }
}
