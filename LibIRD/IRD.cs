using System;
using System.IO;
using System.IO.Compression;
using System.IO.Hashing;
using System.Security.Cryptography;
using System.Text;

namespace LibIRD
{
    /// <summary>
    /// IRD Generation, Reading and Writing
    /// </summary>
    public class IRD
    {
        #region Constants

        /// <summary>
        /// IRD file signature
        /// </summary>
        /// <remarks>"3IRD"</remarks>
        private static readonly byte[] Magic = new byte[] { 0x33, 0x49, 0x52, 0x44 };

        /// <summary>
        /// MD5 hash of null
        /// </summary>
        private static readonly byte[] NullMD5 = new byte[] {0xd4, 0x1d, 0x8c, 0xd9, 0x8f, 0x00, 0xb2, 0x04, 0xe9, 0x80, 0x09, 0x98, 0xec, 0xf8, 0x42, 0x7e};

        /// <summary>
        /// AES CBC Encryption Key for Data 1 (Disc Key)
        /// </summary>
        private static readonly byte[] D1AesKey = { 0x38, 0x0B, 0xCF, 0x0B, 0x53, 0x45, 0x5B, 0x3C, 0x78, 0x17, 0xAB, 0x4F, 0xA3, 0xBA, 0x90, 0xED };

        /// <summary>
        /// AES CBC Initial Value for Data 1 (Disc Key)
        /// </summary>
        private static readonly byte[] D1AesIV = { 0x69, 0x47, 0x47, 0x72, 0xAF, 0x6F, 0xDA, 0xB3, 0x42, 0x74, 0x3A, 0xEF, 0xAA, 0x18, 0x62, 0x87 };

        /// <summary>
        /// AES CBC Encryption Key for Data 2 (Disc ID)
        /// </summary>
        private static readonly byte[] D2AesKey = { 0x7C, 0xDD, 0x0E, 0x02, 0x07, 0x6E, 0xFE, 0x45, 0x99, 0xB1, 0xB8, 0x2C, 0x35, 0x99, 0x19, 0xB3 };

        /// <summary>
        /// AES CBC Initial Value for Data 2 (Disc ID)
        /// </summary>
        private static readonly byte[] D2AesIV = { 0x22, 0x26, 0x92, 0x8D, 0x44, 0x03, 0x2F, 0x43, 0x6A, 0xFD, 0x26, 0x7E, 0x74, 0x8B, 0x23, 0x93 };

        #endregion

        #region Publicly Settable Properties

        /// <summary>
        /// IRD Specification Version
        /// </summary>
        /// <remarks>1 byte, Versions 6-9 are currently supported</remarks>
        public byte Version
        {
            get => _version;
            set
            {
                if (value == 6 || value == 7 || value == 8 || value == 9)
                    _version = value;
            }
        }
        private byte _version = 9; // Default to latest IRD version = 9

        /// <summary>
        /// Unique Identifier
        /// </summary>
        /// <remarks>Redump-style IRDs use CRC32 hash of the ISO as the UID</remarks>
        public uint UID { get; set; } = 0x00000000; // Default to zeroed UID

        /// <summary>
        /// Extra Config, usually 0x0000
        /// </summary>
        public ushort ExtraConfig { get; set; } = 0x0000;

        /// <summary>
        /// Attachments, usually 0x0000
        /// </summary>
        public ushort Attachments { get; set; } = 0x0000;

        /// <summary>
        /// D1 key
        /// </summary>
        /// <remarks>16 bytes</remarks>
        public byte[] Data1Key { get; set; }

        /// <summary>
        /// D2 key
        /// </summary>
        /// <remarks>16 bytes</remarks>
        public byte[] Data2Key { get; set; }

        /// <summary>
        /// Uncompressed PIC data
        /// </summary>
        /// <remarks>115 bytes</remarks>
        public byte[] PIC { get; set; }

        #endregion

        #region Privately Settable Properties

        /// <summary>
        /// The same value stored in PARAM.SFO / TITLE_ID
        /// </summary>
        /// <remarks>9 bytes, ASCII, stored without dashes</remarks>
        public string TitleID { get; private set; }

        /// <summary>
        /// The same value stored in PARAM.SFO / TITLE
        /// </summary>
        /// <remarks>ASCII</remarks>
        public string Title { get; private set; }

        /// <summary>
        /// The same value stored in PARAM.SFO / PS3_SYSTEM_VER
        /// </summary>
        /// <remarks>4 bytes, ASCII, (e.g. "1.20", missing uses "0000"</remarks>
        public string SystemVersion { get; private set; }

        /// <summary>
        /// The same value stored in PARAM.SFO / VERSION
        /// </summary>
        /// <remarks>5 bytes, ASCII, e.g. "01.20"</remarks>
        public string GameVersion { get; private set; }

        /// <summary>
        /// The same value stored in PARAM.SFO / APP_VER
        /// </summary>
        /// <remarks>5 bytes, ASCII, e.g. "01.00"</remarks>
        public string AppVersion { get; private set; }

        /// <summary>
        /// Length of the gzip-compressed header data
        /// </summary>
        public uint HeaderLength { get; private set; }

        /// <summary>
        /// Gzip-compressed header data
        /// </summary>
        public byte[] Header { get; private set; }

        /// <summary>
        /// Length of the gzip-compressed footer data
        /// </summary>
        public uint FooterLength { get; private set; }

        /// <summary>
        /// Gzip-compressed footer data
        /// </summary>
        public byte[] Footer { get; private set; }

        /// <summary>
        /// Number of complete regions in the image
        /// </summary>
        public byte RegionCount { get; private set; }

        /// <summary>
        /// MD5 hashes for all complete regions in the image
        /// </summary>
        /// <remarks><see cref="RegionCount"/> regions, 16-bytes per hash</remarks>
        public byte[][] RegionHashes { get; private set; }

        /// <summary>
        /// Number of decrypted files in the image
        /// </summary>
        public uint FileCount { get; private set; }

        /// <summary>
        /// Starting sector for each decrypted file
        /// </summary>
        /// <remarks><see cref="FileCount"/> files, alternating with each <see cref="FileHashes"/> entry</remarks>
        public ulong[] FileKeys { get; private set; }

        /// <summary>
        /// MD5 hashes for all decrypted files in the image
        /// </summary>
        /// <remarks><see cref="FileHashes"/> files, 16-bytes per hash, alternating with each <see cref="FileHashes"/> entry</remarks>
        public byte[][] FileHashes { get; private set; }

        /// <summary>
        /// IRD content CRC
        /// </summary>
        public uint CRC { get; private set; }

        #endregion

        #region Generate Fields

        /// <summary>
        /// Generates Data 1, via AES-128 CBC decryption of a Disc Key
        /// </summary>
        /// <param name="key">Byte array containing AES encrypted Disc Key</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        private protected void GenerateD1(byte[] key)
        {
            // Validate key
            if (key == null || key.Length <= 0)
                throw new ArgumentNullException(nameof(key));
            if (key.Length != 16)
                throw new ArgumentException("Disc Key must be a byte array of length 16", nameof(key));

            // AES decryption
            using (Aes aes = Aes.Create())
            {
                // Validate aes is available
                if (aes == null)
                    throw new InvalidOperationException("AES not available. Change your system settings");

                // Set AES settings
                aes.Key = D1AesKey;
                aes.IV = D1AesIV;
                aes.Padding = PaddingMode.None;
                aes.Mode = CipherMode.CBC;

                // Perform AES decryption
                using (ICryptoTransform decryptor = aes.CreateDecryptor())
                {
                    MemoryStream ms = new MemoryStream();
                    CryptoStream cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Write);
                    cs.Write(key, 0, 16);
                    cs.FlushFinalBlock();
                    Data1Key = ms.ToArray();
                    ms.Close();
                    cs.Close();
                }
            }
        }

        /// <summary>
        /// Generates Data 2, via AES-128 CBC encryption of a Disc ID
        /// </summary>
        /// <param name="id">Byte array containing AES decrypted Disc ID</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        private protected void GenerateD2(byte[] id)
        {
            // Validate id
            if (id == null || id.Length <= 0)
                throw new ArgumentNullException(nameof(id));
            if (id.Length != 16)
                throw new ArgumentException("Disc ID must be a byte array of length 16", nameof(id));

            // AES encryption
            using (Aes aes = Aes.Create())
            {
                // Validate aes is available
                if (aes == null)
                    throw new InvalidOperationException("AES not available. Change your system settings");

                // Set AES settings
                aes.Key = D2AesKey;
                aes.IV = D2AesIV;
                aes.Padding = PaddingMode.None;
                aes.Mode = CipherMode.CBC;
                
                // Perform AES encryption
                using (ICryptoTransform encryptor = aes.CreateEncryptor())
                {
                    MemoryStream ms = new MemoryStream();
                    CryptoStream cs = new CryptoStream(ms, encryptor, CryptoStreamMode.Write);
                    cs.Write(id, 0, 16);
                    cs.FlushFinalBlock();
                    Data2Key = ms.ToArray();
                    ms.Close();
                    cs.Close();
                }
            }
        }

        #endregion

        #region Constructors

        // TODO: Constructor using D1 and D2 directly, rather than Disc Key / Disc ID

        /// <summary>
        /// Default constructor for internal derived classes only: resulting object not in usable state
        /// </summary>
        private protected IRD()
        {
            // Assumes that derived class will set private variables and generate in its own constructor
        }

        /// <summary>
        /// Constructor that reads an existing IRD file and stores its fields in the IRD object
        /// </summary>
        /// <param name="irdPath">Path to IRD file to read data from</param>
        public IRD(string irdPath)
        {
            Read(irdPath);
        }

        /// <summary>
        /// Public Constructor must be called with required fields
        /// </summary>
        /// <param name="discPath">Path to the ISO</param>
        /// <param name="discKey">Disc Key, byte array of length 16</param>
        /// <param name="discID">Disc ID, byte array of length 16</param>
        /// <param name="discPIC">Disc PIC, byte array of length 115</param>
        public IRD(string discPath, byte[] discKey, byte[] discID, byte[] discPIC)
        {
            // Store IRD fields that cannot be determined from the ISO
            GenerateD1(discKey);
            GenerateD2(discID);
            PIC = discPIC;

            // Generate the remaining IRD fields from the disc drive or mounted ISO
            Generate(discPath);
        }

        #endregion

        #region Methods

        /// <summary>
        /// Generate IRD fields from a disc drive or mounted ISO
        /// </summary>
        /// <param name="isoPath">Path to the ISO</param>
        /// <exception cref="ArgumentException"></exception>
        private protected void Generate(string isoPath)
        {
            // Validate ISO path
            if (isoPath == null || isoPath.Length <= 0)
                throw new ArgumentNullException(nameof(isoPath));

            // TODO: Code the difficult part (remove these initial values)
            TitleID = "ABCD12345";
            Title = "Title";
            SystemVersion = "01.00";
            GameVersion = "01.00";
            AppVersion = "01.00";
            HeaderLength = 1;
            Header = new byte[HeaderLength];
            Header[0] = 0x00;
            FooterLength = 1;
            Footer = new byte[FooterLength];
            Footer[0] = 0x00;
            RegionCount = 1;
            RegionHashes = new byte[RegionCount][];
            RegionHashes[0] = new byte[] { 0x00 };
            FileCount = 1;
            FileKeys = new ulong[FileCount];
            FileKeys[0] = 0x00;
            FileHashes = new byte[FileCount][];
            FileHashes[0] = new byte[] { 0x00 };
            FileHashes[0][0] = 0x00;
        }

        /// <summary>
        /// Read IRD data from file, store fields into class
        /// </summary>
        /// <param name="irdPath">Path to IRD file to read data from</param>
        /// <exception cref="ArgumentException"></exception>
        public void Read(string irdPath)
        {
            // TODO: Check irdPath is a valid IRD file path
            if (irdPath == null || !File.Exists(irdPath))
                throw new ArgumentException("IRD File Path invalid", nameof(irdPath));

            // TODO: Implement reading IRD file data into fields (remove these intial values)
            TitleID = "ABCD12345";
            Title = "Title";
            SystemVersion = "01.00";
            GameVersion = "01.00";
            AppVersion = "01.00";
            HeaderLength = 1;
            Header = new byte[HeaderLength];
            Header[0] = 0x00;
            FooterLength = 1;
            Footer = new byte[FooterLength];
            Footer[0] = 0x00;
            RegionCount = 1;
            RegionHashes = new byte[RegionCount][];
            RegionHashes[0] = new byte[] { 0x00 };
            FileCount = 1;
            FileKeys = new ulong[FileCount];
            FileKeys[0] = 0x00;
            FileHashes = new byte[FileCount][];
            FileHashes[0] = new byte[] { 0x00 };
            FileHashes[0][0] = 0x00;
        }

        /// <summary>
        /// Write IRD data to file
        /// </summary>
        /// <param name="irdPath">Path to IRD file to be written to</param>
        public void Write(string irdPath)
        {
            // Validate irdPath
            if (irdPath == null || irdPath.Length <= 0)
                throw new ArgumentNullException(nameof(irdPath));

            // Create the IRD file stream
            using (FileStream fs = new FileStream(irdPath, FileMode.Create, FileAccess.Write))
            {
                // Create a GZipped IRD file stream
                using (GZipStream outStream = new GZipStream(fs, CompressionLevel.Optimal))
                {
                    // Keep track of the little-endian 32-bit "IEEE 802.3" CRC value
                    Crc32 crcStream = new Crc32();

                    // Write IRD data to stream in order
                    using (BinaryWriter bw = new BinaryWriter(outStream))
                    {
                        // IRD File Signature
                        bw.Write(Magic);

                        // IRD File Version
                        bw.Write(_version);

                        // PARAM.SFO / TITLE_ID
                        byte[] titleIDBuf = Encoding.ASCII.GetBytes(TitleID);
                        bw.Write(titleIDBuf, 0, 9);

                        // PARAM.SFO / TITLE
                        bw.Write(Title);

                        // PARAM.SFO / PS3_SYSTEM_VER
                        byte[] systemVersionBuf = Encoding.ASCII.GetBytes(SystemVersion);
                        bw.Write(systemVersionBuf, 0, 4);

                        // PARAM.SFO / VERSION
                        byte[] buf = Encoding.ASCII.GetBytes(GameVersion);
                        byte[] gameVersionBuf = new byte[5];
                        Array.Copy(buf, 0, gameVersionBuf, 0, buf.Length);
                        bw.Write(gameVersionBuf, 0, 5);

                        // PARAM.SFO / APP_VER
                        buf = Encoding.ASCII.GetBytes(AppVersion);
                        byte[] appVersionBuf = new byte[5];
                        Array.Copy(buf, 0, appVersionBuf, 0, buf.Length);
                        bw.Write(appVersionBuf, 0, 5);

                        // IRD Unique Identifier, for version 7
                        if (_version == 7)
                            bw.Write(UID);

                        // Compress Header
                        bw.Write(HeaderLength);
                        bw.Write(Header, 0, (int)HeaderLength);

                        // Compress Footer
                        bw.Write(FooterLength);
                        bw.Write(Footer, 0, (int)FooterLength);

                        // Number of regions hashed
                        bw.Write(RegionCount);

                        // Hashes for each region
                        for (int i = 0; i < RegionCount; i++)
                            bw.Write(RegionHashes[i], 0, 16);

                        // Number of files hashed
                        bw.Write(FileCount);

                        // Hashes for each file
                        for (int i = 0; i < FileCount; i++)
                        {
                            bw.Write(FileKeys[i]);
                            bw.Write(FileHashes[i], 0, 16);
                        }

                        // Reserved fields
                        bw.Write(ExtraConfig);
                        bw.Write(Attachments);

                        // PIC data is placed here for Version 9
                        if (_version >= 9)
                            bw.Write(PIC, 0, 115);

                        // Disc Authentication keys
                        bw.Write(Data1Key, 0, 16);
                        bw.Write(Data2Key, 0, 16);

                        // PIC data is placed here prior to Version 9
                        if (_version < 9)
                            bw.Write(PIC, 0, 115);

                        // IRD Unique Identifier, for versions after 7
                        if (_version > 7)
                            bw.Write(UID);
                    }

                    // Calculate final CRC32
                    CRC = BitConverter.ToUInt32(crcStream.GetCurrentHash(), 0);

                    // Write final CRC32 to stream
                    outStream.Write(BitConverter.GetBytes(CRC), 0, 4);
                }
            }
        }

        #endregion
    }
}
