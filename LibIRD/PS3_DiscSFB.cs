using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace LibIRD
{
    /// <summary>
    /// PS3_DISC.SFB file parsing
    /// </summary>
    public class PS3_DiscSFB
    {
        /// <summary>
        /// PS3_DISC.SFB file signature
        /// </summary>
        /// <remarks>{ 0x2E, 0x53, 0x46, 0x42 }</remarks>
        public static readonly string Magic = ".SFB";

        /// <summary>
        /// PS3_DISC.SFB file version
        /// </summary>
        /// <remarks>Typically v1, { 0x00, 0x01 }</remarks>
        public ushort Version { get; private set; }

        /// <summary>
        /// A field within the PS3_DISC.SFB file
        /// </summary>
        /// <remarks>string Key, string Value</remarks>
        public Dictionary<string, string> Field { get; private set; }

        /// <summary>
        /// Constructor using a PARAM.SFO file path
        /// </summary>
        /// <param name="sfbPath">Full file path to the PS3_DISC.SFB file</param>
        public PS3_DiscSFB(string sfbPath)
        {
            // Read file as a stream, and parse file
            using FileStream fs = new(sfbPath, FileMode.Open, FileAccess.Read);
            Parse(fs);
        }

        /// <summary>
        /// Parse PS3_DISC.SFB from stream
        /// </summary>
        /// <param name="sfbStream">SFB file stream</param>
        public PS3_DiscSFB(Stream sfbStream)
        {
            // Parse file stream
            Parse(sfbStream);
        }

        /// <summary>
        /// Read fields from PS3_DISC.SFB
        /// </summary>
        /// <param name="sfbStream">File stream for PS3_DISC.SFB</param>
        /// <exception cref="FileLoadException"></exception>
        private void Parse(Stream sfbStream)
        {
            // Read binary stream
            using BinaryReader br = new(sfbStream);

            // Check file signature is correct
            string magic = Encoding.ASCII.GetString(br.ReadBytes(4));
            if (magic != PS3_DiscSFB.Magic)
                throw new FileLoadException("Unexpected PS3_DISC.SFB file");

            // Read SFB file version
            byte[] buf = br.ReadBytes(2);
            Array.Reverse(buf);
            Version = BitConverter.ToUInt16(buf, 0);

            // Process all field headers
            sfbStream.Seek(0x20, SeekOrigin.Begin);
            string field = Encoding.ASCII.GetString(br.ReadBytes(0x10)).Trim('\0');
            Field = [];
            while (field != null && field != "")
            {
                // Find location of value
                buf = br.ReadBytes(4);
                Array.Reverse(buf);
                int offset = BitConverter.ToInt32(buf, 0);
                buf = br.ReadBytes(4);
                Array.Reverse(buf);
                int length = BitConverter.ToInt32(buf, 0);

                // Access and store value
                long pos = sfbStream.Position;
                sfbStream.Seek(offset, SeekOrigin.Begin);
                Field[field] = Encoding.ASCII.GetString(br.ReadBytes(length)).Trim('\0');
                sfbStream.Seek(pos + 8, SeekOrigin.Begin);

                // Attempt to read new field
                field = Encoding.ASCII.GetString(br.ReadBytes(0x10)).Trim('\0');
            }
        }

        /// <summary>
        /// Prints formatted parameters extracted from PS3_DISC.SFB to console
        /// </summary>
        /// <param name="printPath">Optionally print to text file</param>
        public void Print(string printPath = null, string isoName = null)
        {
            // Build string from parameters
            StringBuilder printText = new();
            if (isoName != null)
                printText.AppendLine($"PS3_DISC.SFB Contents: {isoName}");
            else
                printText.AppendLine("PS3_DISC.SFB Contents:");
            printText.AppendLine("======================");

            // Loop through all parameters in PARAM.SFO
            foreach (KeyValuePair<string, string> field in Field)
                printText.AppendLine(field.Key + ": " + field.Value);
            // Blank line
            printText.Append(Environment.NewLine);

            // If no path given, print to console
            if (printPath == null)
            {
                // Ensure UTF-8 will display properly in console
                Console.OutputEncoding = Encoding.UTF8;

                // Print formatted string to console
                Console.Write(printText);
            }
            else
            {
                // Write data to file
                File.AppendAllText(printPath, printText.ToString());
            }
        }

        /// <summary>
        /// Prints parameters extracted from PS3_DISC.SFB to a json object
        /// </summary>
        /// <param name="jsonPath">Optionally print to json file</param>
        public void PrintJson(string jsonPath = null)
        {
            // Serialise PS3_Disc.SFB data to a JSON object
            string json = JsonConvert.SerializeObject(Field, Formatting.Indented);

            // If no path given, output to console
            if (jsonPath == null)
            {
                // Ensure UTF-8 will display properly in console
                Console.OutputEncoding = Encoding.UTF8;

                // Print formatted string to console
                Console.Write(json);
            }
            else
            {
                // Write to path
                File.AppendAllText(jsonPath, json);
            }
        }
    }
}
