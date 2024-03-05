using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace LibIRD
{
    /// <summary>
    /// PARAM.SFO file parsing
    /// </summary>
    public class ParamSFO
    {
        /// <summary>
        /// PARAM.SFO file signature
        /// </summary>
        /// <remarks>{ 0x00, 0x50, 0x53, 0x46 }</remarks>
        public static readonly string Magic = "\0PSF";

        /// <summary>
        /// PARAM.SFO file version
        /// </summary>
        /// <remarks>Typically { 0x01, 0x01, 0x00, 0x00 } (v1.1)</remarks>
        public uint Version { get; private set; }

        /// <summary>
        /// A field within the PS3_DISC.SFB file
        /// </summary>
        /// <remarks>string Key, string Value</remarks>
        public Dictionary<string, string> Field { get; private set; }

        /// <summary>
        /// Constructor using a PARAM.SFO file stream
        /// </summary>
        /// <param name="sfoStream">SFO file stream</param>
        public ParamSFO(Stream sfoStream)
        {
            // Parse file stream
            Parse(sfoStream);
        }

        /// <summary>
        /// Constructor using a PARAM.SFO file path
        /// </summary>
        /// <param name="sfoPath">Full file path to the PARAM.SFO file</param>
        public ParamSFO(string sfoPath)
        {
            // Read file as a stream, and parse file
            using FileStream fs = new(sfoPath, FileMode.Open, FileAccess.Read);
            Parse(fs);
        }

        /// <summary>
        /// Parse parameters and PARAM.SFO metadata from stream
        /// </summary>
        /// <param name="sfoStream">SFO file stream</param>
        /// <exception cref="FileLoadException"></exception>
        private void Parse(Stream sfoStream)
        {
            // Read binary stream
            using BinaryReader br = new(sfoStream);

            // Check file signature is correct
            string magic = Encoding.ASCII.GetString(br.ReadBytes(4));
            if (magic != ParamSFO.Magic)
                throw new FileLoadException("Unexpected PARAM.SFO file");

            // Parse header
            Version = br.ReadUInt32();
            uint keyTableStart = br.ReadUInt32();
            uint dataTableStart = br.ReadUInt32();
            uint paramCount = br.ReadUInt32();

            // Parse parameter metadata
            ushort[] keyOffset = new ushort[paramCount];
            uint[] dataFormat = new uint[paramCount];
            uint[] dataLength = new uint[paramCount];
            uint[] dataTotal = new uint[paramCount];
            uint[] dataOffset = new uint[paramCount];
            for (int i = 0; i < paramCount; i++)
            {
                keyOffset[i] = br.ReadUInt16();
                dataFormat[i] = br.ReadUInt16();
                dataLength[i] = br.ReadUInt32();
                dataTotal[i] = br.ReadUInt32();
                dataOffset[i] = br.ReadUInt32();
            }

            // Parse parameters
            Field = [];
            for (int i = 0; i < paramCount; i++)
            {
                // Move stream to ith key
                sfoStream.Position = keyTableStart + keyOffset[i];

                // Determine ith key length
                uint keyLen = ((i == paramCount - 1) ? dataTableStart - keyTableStart : keyOffset[i + 1])
                              - keyOffset[i];

                // Read ith key name
                string key = Encoding.ASCII.GetString(br.ReadBytes((int)keyLen)).TrimEnd('\0');

                // Move stream to ith data
                sfoStream.Position = dataTableStart + dataOffset[i];

                // Read ith data, based on data format
                Field[key] = dataFormat[i] switch
                {
                    // Non-null-terminated UTF-8 String
                    0x0004 => Encoding.UTF8.GetString(br.ReadBytes((int)dataLength[i])),
                    // Null-terminated UTF-8 String
                    0x0204 => Encoding.UTF8.GetString(br.ReadBytes((int)dataLength[i])).TrimEnd('\0'),
                    // Integer
                    0x0404 => br.ReadInt32().ToString(),
                    // Unknown data format, assume null-terminated string
                    _ => Encoding.UTF8.GetString(br.ReadBytes((int)dataLength[i])).TrimEnd('\0'),
                };
            }
        }

        /// <summary>
        /// Prints formatted parameters extracted from PARAM.SFO to console
        /// </summary>
        /// <param name="printPath">Optionally print to text file</param>
        public void Print(string printPath = null, string isoName = null)
        {
            // Build string from parameters
            StringBuilder printText = new();
            if (isoName != null)
                printText.AppendLine($"PARAM.SFO Contents: {isoName}");
            else
                printText.AppendLine("PARAM.SFO Contents:");
            printText.AppendLine("===================");

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
        /// Prints parameters extracted from PARAM.SFO to a json object
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
