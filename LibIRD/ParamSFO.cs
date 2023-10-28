using System;
using System.IO;
using System.Text;

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
        /// <remarks>Typically { 0x01 0x01 0x00 0x00 } (v1.1)</remarks>
        public uint Version { get; private set; }

        /// <summary>
        /// The location of the first byte of the Key Table
        /// </summary>
        public uint KeyTableStart { get; private set; }

        /// <summary>
        /// The location of the first byte of the Data Table
        /// </summary>
        public uint DataTableStart { get; private set; }

        /// <summary>
        /// The number of parameters in the table
        /// </summary>
        public uint ParamCount { get; private set; }

        /// <summary>
        /// Parameter, a single entry in parameter table
        /// </summary>
        public class Param
        {
            /// <summary>
            /// Offset of key, relative to KeyTableStart
            /// </summary>
            public ushort KeyOffset { get; internal set; }

            /// <summary>
            /// Format of parameter
            /// </summary>
            /// <remarks>0x0400 is string, 0x0404 is uint</remarks>
            public ushort DataFormat { get; internal set; }

            /// <summary>
            /// Number of bytes used for parameter
            /// </summary>
            public uint DataLength { get; internal set; }

            /// <summary>
            /// Total number of bytes for parameter
            /// </summary>
            /// <remarks>DataTotal - DataLength is padding of 0x00</remarks>
            public uint DataTotal { get; internal set; }

            /// <summary>
            /// offset of parameter, relative to DataTableStart
            /// </summary>
            public uint DataOffset { get; internal set; }

            /// <summary>
            /// The name of the parameter
            /// </summary>
            public string Name { get; internal set; }

            /// <summary>
            /// The value of the parameter, if it is a string
            /// </summary>
            public string StringValue { get; internal set; } = null;

            /// <summary>
            /// The value of the parameter, if it is a UInt32
            /// </summary>
            public int IntValue { get; internal set; } = 0;
        }

        /// <summary>
        /// The parameters in the table
        /// </summary>
        /// <remarks><see cref="ParamCount"/> Params in the table</remarks>
        public Param[] Params {  get; private set; }

        /// <summary>
        /// String index overloading, gets string value of given key
        /// </summary>
        /// <param name="key">Parameter to be retreived</param>
        /// <returns>The string value of the given key</returns>
        public string this[string key]
        {
            get
            {
                int index = Array.FindIndex(Params, param => param.Name ==  key);
                return Params[index].StringValue;
            }
        }

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
        /// <param name="sfoPath">Full file path to the PARAM.SFO</param>
        /// <exception cref="ArgumentNullException"></exception>
        public ParamSFO(string sfoPath)
        {
            // Validate file path
            if (sfoPath == null || sfoPath.Length <= 0)
                throw new ArgumentNullException(nameof(sfoPath));

            // Read file as a stream, and parse file
            using (FileStream fs = new FileStream(sfoPath, FileMode.Open, FileAccess.Read))
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
            using (BinaryReader br = new BinaryReader(sfoStream))
            {
                // Check file signature is correct
                string magic = Encoding.ASCII.GetString(br.ReadBytes(4));
                if (magic != ParamSFO.Magic)
                    throw new FileLoadException("Not a valid PARAM.SFO file");

                // Parse header
                Version = br.ReadUInt32();
                KeyTableStart = br.ReadUInt32();
                DataTableStart = br.ReadUInt32();
                ParamCount = br.ReadUInt32();

                // Parse parameter metadata
                Params = new Param[ParamCount];
                for (int i = 0; i < ParamCount; i++)
                {
                    Params[i] = new Param
                    {
                        KeyOffset = br.ReadUInt16(),
                        DataFormat = br.ReadUInt16(),
                        DataLength = br.ReadUInt32(),
                        DataTotal = br.ReadUInt32(),
                        DataOffset = br.ReadUInt32()
                    };
                }

                // Parse parameters
                for (int i = 0; i < ParamCount; i++)
                {
                    // Move stream to ith key
                    sfoStream.Position = KeyTableStart + Params[i].KeyOffset;

                    // Determine ith key length
                    uint keyLen = ((i == ParamCount - 1) ? DataTableStart - KeyTableStart : Params[i + 1].KeyOffset)
                                - Params[i].KeyOffset;

                    // Read ith key name
                    Params[i].Name = Encoding.ASCII.GetString(br.ReadBytes((int) keyLen)).TrimEnd('\0');

                    // Move stream to ith data
                    sfoStream.Position = DataTableStart + Params[i].DataOffset;

                    // Read ith data, based on data format
                    switch (Params[i].DataFormat)
                    {
                        case 0x0400: // Non-null-terminated UTF-8 String
                            Params[i].StringValue = Encoding.UTF8.GetString(br.ReadBytes((int)Params[i].DataLength));
                            break;
                        case 0x0402: // Null-terminated UTF-8 String
                            Params[i].StringValue = Encoding.UTF8.GetString(br.ReadBytes((int)Params[i].DataLength)).TrimEnd('\0');
                            break;
                        case 0x0404: // Integer
                            //if (Params[i].DataLength != 4)
                                //throw new ArgumentException("Integer parameter not 4 bytes?");
                            Params[i].IntValue = br.ReadInt32();
                            break;
                        default: // Unknown data format, assume null-terminated string
                            Params[i].StringValue = Encoding.UTF8.GetString(br.ReadBytes((int)Params[i].DataLength)).TrimEnd('\0');
                            break;
                    }
                }
            }
        }

        /// <summary>
        /// Prints formatted parameters extracted from PARAM.SFO to console
        /// </summary>
        public void Print()
        {
            StringBuilder print = new StringBuilder("PARAM.SFO Contents:\n====================\n");
            for (int i = 0; i < ParamCount; i++)
            {
                print.Append(Params[i].Name);
                print.Append(' ');
                for (int j = Params[i].Name.Length; j < 20; j++)
                    print.Append(' ');
                switch (Params[i].DataFormat)
                {
                    case 0x0404:
                        print.Append(Params[i].IntValue);
                        break;
                    default:
                        print.Append(Params[i].StringValue);
                        break;
                }
                print.Append('\n');
            }
            Console.Write(print);
        }
    }
}
