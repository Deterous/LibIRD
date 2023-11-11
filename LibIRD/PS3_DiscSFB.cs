using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

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
        public Dictionary<string, string> Field {  get; private set; }

        /// <summary>
        /// Constructor using a PARAM.SFO file path
        /// </summary>
        /// <param name="sfbPath">Full file path to the PS3_DISC.SFB file</param>
        /// <exception cref="ArgumentNullException"></exception>
        public PS3_DiscSFB(string sfbPath)
        {
            // Validate file path
            if (sfbPath == null || sfbPath.Length <= 0)
                throw new ArgumentNullException(nameof(sfbPath));

            // Read file as a stream, and parse file
            using FileStream fs = new(sfbPath, FileMode.Open, FileAccess.Read);
            Parse(fs);
        }

        /// <summary>
        /// Parse PS3_DISC.SFB from stream
        /// </summary>
        /// <param name="sfbStream">SFB file stream</param>
        /// <exception cref="FileLoadException"></exception>
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
            Version = BitConverter.ToUInt16(buf);

            // Process all field headers
            sfbStream.Seek(0x20, SeekOrigin.Begin);
            string field = Encoding.ASCII.GetString(br.ReadBytes(0x10)).Trim('\0');
            Field = new Dictionary<string, string> { };
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
        public void Print()
        {
            // Build string from parameters
            StringBuilder print = new();
            print.AppendLine("PS3_DISC.SFB Contents:");
            print.AppendLine("======================");

            // Loop through all parameters in PARAM.SFO
            foreach (KeyValuePair<string, string> field in Field)
            {
                print.Append(field.Key);
                print.Append(": ");
                print.AppendLine(field.Value);
            }

            // Ensure UTF-8 will display properly
            Console.OutputEncoding = Encoding.UTF8;

            // Print formatted string
            Console.Write(print);
        }
    }
}
