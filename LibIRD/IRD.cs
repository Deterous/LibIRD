using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using LibIRD.DiscUtils;
using LibIRD.DiscUtils.Iso9660;
using LibIRD.DiscUtils.Streams;
using SabreTools.Hashing;

namespace LibIRD
{
    /// <summary>
    /// ISO Rebuild Data
    /// </summary>
    /// <remarks>Generates IRD fields and reads/writes IRD files</remarks>
    public class IRD
    {
        #region Constants

        /// <summary>
        /// Blu-ray ISO sector size in bytes
        /// </summary>
        /// <remarks>2048</remarks>
        private protected const uint SectorSize = 2048;

        /// <summary>
        /// Size of a blu-ray layer in bytes (BD-25 max size)
        /// </summary>
        /// <remarks>12219392 sectors, default PS3 layerbreak value</remarks>
        private protected const long BDLayerSize = 25025314816;

        /// <summary>
        /// IRD file signature
        /// </summary>
        /// <remarks>"3IRD"</remarks>
        private static readonly byte[] Magic = [0x33, 0x49, 0x52, 0x44];

        /// <summary>
        /// MD5 hash of null
        /// </summary>
        private static readonly byte[] NullMD5 = [0xd4, 0x1d, 0x8c, 0xd9, 0x8f, 0x00, 0xb2, 0x04, 0xe9, 0x80, 0x09, 0x98, 0xec, 0xf8, 0x42, 0x7e];

        /// <summary>
        /// AES CBC Encryption Key for Data 1 (Disc Key)
        /// </summary>
        private static readonly byte[] D1AesKey = [0x38, 0x0B, 0xCF, 0x0B, 0x53, 0x45, 0x5B, 0x3C, 0x78, 0x17, 0xAB, 0x4F, 0xA3, 0xBA, 0x90, 0xED];

        /// <summary>
        /// AES CBC Initial Value for Data 1 (Disc Key)
        /// </summary>
        private static readonly byte[] D1AesIV = [0x69, 0x47, 0x47, 0x72, 0xAF, 0x6F, 0xDA, 0xB3, 0x42, 0x74, 0x3A, 0xEF, 0xAA, 0x18, 0x62, 0x87];

        /// <summary>
        /// AES CBC Encryption Key for Data 2 (Disc ID)
        /// </summary>
        private static readonly byte[] D2AesKey = [0x7C, 0xDD, 0x0E, 0x02, 0x07, 0x6E, 0xFE, 0x45, 0x99, 0xB1, 0xB8, 0x2C, 0x35, 0x99, 0x19, 0xB3];

        /// <summary>
        /// AES CBC Initial Value for Data 2 (Disc ID)
        /// </summary>
        private static readonly byte[] D2AesIV = [0x22, 0x26, 0x92, 0x8D, 0x44, 0x03, 0x2F, 0x43, 0x6A, 0xFD, 0x26, 0x7E, 0x74, 0x8B, 0x23, 0x93];

        #endregion

        #region Properties

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
        /// Extra Config
        /// </summary>
        /// <remarks>Usually set to 0x0000, set to 0x0001 for redump-style IRDs</remarks>
        public ushort ExtraConfig { get; set; } = 0x0000; // Default to zero

        /// <summary>
        /// Attachments
        /// </summary>
        /// <remarks>Reserved, usually set to 0x0000</remarks>
        public ushort Attachments { get; set; } = 0x0000; // Default to zero

        /// <summary>
        /// Disc Key
        /// </summary>
        /// <remarks>16 bytes</remarks>
        public byte[] DiscKey
        {
            get { return _discKey; }
            set
            {
                if (value != null && value.Length == 16)
                {
                    _discKey = value;
                    _data1Key = GenerateD1(value);
                }
                else
                    throw new ArgumentException("Disc Key must be a byte array of length 16", nameof(value));
            }
        }
        private byte[] _discKey;

        /// <summary>
        /// D1 key
        /// </summary>
        /// <remarks>16 bytes</remarks>
        public byte[] Data1Key
        {
            get { return _data1Key; }
            set
            {
                if (value != null && value.Length == 16)
                {
                    _data1Key = value;
                    _discKey = GenerateDiscKey(value);
                }
                else
                    throw new ArgumentException("Data 1 Key must be a byte array of length 16", nameof(value));
            }
        }
        private byte[] _data1Key;

        /// <summary>
        /// D2 key
        /// </summary>
        /// <remarks>16 bytes</remarks>
        public byte[] DiscID
        {
            get { return _discID; }
            set
            {
                if (value != null && value.Length == 16)
                {
                    _discID = value;
                    _data2Key = GenerateD2(value);
                }
                else
                    throw new ArgumentException("Disc ID must be a byte array of length 16", nameof(value));
            }
        }
        private byte[] _discID;

        /// <summary>
        /// D2 key
        /// </summary>
        /// <remarks>16 bytes</remarks>
        public byte[] Data2Key
        {
            get { return _data2Key; }
            set
            {
                if (value != null && value.Length == 16)
                {
                    _data2Key = value;
                    _discID = GenerateDiscID(value);
                }
                else
                    throw new ArgumentException("Data 2 Key must be a byte array of length 16", nameof(value));
            }
        }
        private byte[] _data2Key;

        /// <summary>
        /// Uncompressed PIC data
        /// </summary>
        /// <remarks>115 bytes</remarks>
        public byte[] PIC
        {
            get { return _pic; }
            set
            {
                if (value != null && value.Length == 115)
                    _pic = value;
                else
                    throw new ArgumentException("PIC must be a byte array of length 115", nameof(value));
            }
        }
        private byte[] _pic;

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
        public string DiscVersion { get; private set; }

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
        /// Number of files in the image
        /// </summary>
        public uint FileCount { get; private set; }

        /// <summary>
        /// Starting sector for each file
        /// </summary>
        /// <remarks><see cref="FileCount"/> files, alternating with each <see cref="FileHashes"/> entry</remarks>
        public long[] FileKeys { get; private set; }

        /// <summary>
        /// MD5 hashes for all decrypted files in the image
        /// </summary>
        /// <remarks><see cref="FileCount"/> files, 16-bytes per hash, alternating with each <see cref="FileHashes"/> entry</remarks>
        public byte[][] FileHashes { get; private set; }

        /// <summary>
        /// First byte of the PS3_DISC.SFB file
        /// </summary>
        /// <remarks>Last byte to read when reading the header</remarks>
        private long FirstDataSector { get; set; }

        /// <summary>
        /// Last byte of the PS3UPDAT.PUP file
        /// </summary>
        /// <remarks>Offset to use when reading the footer</remarks>
        private long UpdateEnd { get; set; }

        /// <summary>
        /// First sector of each region
        /// </summary>
        private long[] RegionStart { get; set; }

        /// <summary>
        /// Last sector of each region
        /// </summary>
        private long[] RegionEnd { get; set; }

        /// <summary>
        /// File extents to hash
        /// </summary>
        private Range<long, long>[][] FileExtents { get; set; }

        #endregion

        #region Constructors

        /// <summary>
        /// Manual constructor
        /// </summary>
        private IRD(
            byte version,
            string titleID,
            string title,
            string sysVersion,
            string discVersion,
            string appVersion,
            byte[] header,
            byte[] footer,
            byte[][] regionHashes,
            long[] fileKeys,
            byte[][] fileHashes,
            ushort extraConfig,
            ushort attachments,
            byte[] d1,
            byte[] d2,
            byte[] pic,
            uint uid)
        {
            TitleID = titleID;
            Title = title;
            SystemVersion = sysVersion;
            DiscVersion = discVersion;
            AppVersion = appVersion;
            HeaderLength = (uint)header.Length;
            Header = header;
            FooterLength = (uint)footer.Length;
            Footer = footer;
            RegionCount = (byte)regionHashes.Length;
            RegionHashes = regionHashes;
            FileCount = (uint)fileKeys.Length;
            FileKeys = fileKeys;
            FileHashes = fileHashes;
            Version = version;
            ExtraConfig = extraConfig;
            Attachments = attachments;
            Data1Key = d1;
            Data2Key = d2;
            PIC = pic;
            UID = uid;
        }

        /// <summary>
        /// Default constructor for internal derived classes only: resulting object not in usable state
        /// Assumes that internally derived class will set fields in its own constructor
        /// </summary>
        private protected IRD() { }

        /// <summary>
        /// Constructor with given required fields
        /// </summary>
        /// <param name="isoPath">Path to the ISO</param>
        /// <param name="discKey">Disc Key, byte array of length 16</param>
        /// <param name="discID">Disc ID, byte array of length 16</param>
        /// <param name="discPIC">Disc PIC, byte array of length 115</param>
        /// <param name="redump">True if redump-style IRD (default: false)</param>
        public IRD(string isoPath, byte[] discKey, byte[] discID, byte[] discPIC, bool redump = false)
        {
            // Parse ISO, Disc Key, Disc ID, and PIC
            DiscKey = discKey;
            DiscID = discID;
            PIC = discPIC;

            // Generate IRD files from ISO
            GenerateIRD(isoPath, redump);
        }

        /// <summary>
        /// Constructor that reads required fields from .getkey.log file
        /// </summary>
        /// <param name="isoPath">Path to the ISO</param>
        /// <param name="getKeyLog">Path to the .getkey.log file</param>
        /// <param name="redump">True if redump-style IRD</param>
        public IRD(string isoPath, string getKeyLog, bool redump = false)
        {
            // Parse .getkey.log file
            ParseGetKeyLog(getKeyLog);

            // Generate IRD files from ISO
            GenerateIRD(isoPath, redump);
        }

        #endregion

        #region Property Generation

        /// <summary>
        /// Generates Data 1, via AES-128 CBC decryption of a Disc Key
        /// </summary>
        /// <param name="key">Byte array containing AES encrypted Disc Key</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        private protected static byte[] GenerateD1(byte[] key)
        {
            // Validate key
            if (key.Length != 16)
                throw new ArgumentException("Disc Key must be a byte array of length 16", nameof(key));

            // Setup AES decryption
#if NET35_OR_GREATER || NETCOREAPP
            using Aes aes = Aes.Create() ?? throw new InvalidOperationException("AES not available. Change your system settings");
#else
            using Rijndael aes = Rijndael.Create() ?? throw new InvalidOperationException("AES not available. Change your system settings"); ;
#endif

            // Set AES settings
            aes.Key = D1AesKey;
            aes.IV = D1AesIV;
            aes.Padding = PaddingMode.None;
            aes.Mode = CipherMode.CBC;

            // Perform AES decryption
            using MemoryStream stream = new();
            using ICryptoTransform dec = aes.CreateDecryptor();
            using CryptoStream cs = new(stream, dec, CryptoStreamMode.Write);
            cs.Write(key, 0, 16);
            cs.FlushFinalBlock();

            // Save decrypted key to field
            return stream.ToArray();
        }

        /// <summary>
        /// Generates the Disc key, via AES-128 CBC encryption of a Data 1 Key
        /// </summary>
        /// <param name="d1">Byte array containing AES decrypted Data 1 Key</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        private protected static byte[] GenerateDiscKey(byte[] d1)
        {
            // Validate key
            if (d1.Length != 16)
                throw new ArgumentException("Disc Key must be a byte array of length 16", nameof(d1));

            // Setup AES decryption
#if NET35_OR_GREATER || NETCOREAPP
            using Aes aes = Aes.Create() ?? throw new InvalidOperationException("AES not available. Change your system settings");
#else
            using Rijndael aes = Rijndael.Create() ?? throw new InvalidOperationException("AES not available. Change your system settings"); ;
#endif

            // Set AES settings
            aes.Key = D1AesKey;
            aes.IV = D1AesIV;
            aes.Padding = PaddingMode.None;
            aes.Mode = CipherMode.CBC;

            // Perform AES decryption
            using MemoryStream stream = new();
            using ICryptoTransform enc = aes.CreateEncryptor();
            using CryptoStream cs = new(stream, enc, CryptoStreamMode.Write);
            cs.Write(d1, 0, 16);
            cs.FlushFinalBlock();

            // Save decrypted key to field
            return stream.ToArray();
        }

        /// <summary>
        /// Generates Data 2, via AES-128 CBC encryption of a Disc ID
        /// </summary>
        /// <param name="d2">Byte array containing AES decrypted Disc ID</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        private protected static byte[] GenerateD2(byte[] d2)
        {
            // Validate id
            if (d2.Length != 16) throw new ArgumentException("Disc ID must be a byte array of length 16", nameof(d2));

            // Setup AES encryption
#if NET35_OR_GREATER || NETCOREAPP
            using Aes aes = Aes.Create() ?? throw new InvalidOperationException("AES not available. Change your system settings");
#else
            using Rijndael aes = Rijndael.Create() ?? throw new InvalidOperationException("AES not available. Change your system settings"); ;
#endif

            // Set AES settings
            aes.Key = D2AesKey;
            aes.IV = D2AesIV;
            aes.Padding = PaddingMode.None;
            aes.Mode = CipherMode.CBC;

            // Perform AES encryption
            using MemoryStream stream = new();
            using ICryptoTransform enc = aes.CreateEncryptor();
            using CryptoStream cs = new(stream, enc, CryptoStreamMode.Write);
            cs.Write(d2, 0, 16);
            cs.FlushFinalBlock();

            // Save encrypted key to field
            return stream.ToArray();
        }

        /// <summary>
        /// Generates Disc ID, via AES-128 CBC decryption of a Data 2 Key
        /// </summary>
        /// <param name="d2">Byte array containing AES encrypted Data 2 Key</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        private protected static byte[] GenerateDiscID(byte[] d2)
        {
            // Validate id
            if (d2.Length != 16)
                throw new ArgumentException("Disc ID must be a byte array of length 16", nameof(d2));

            // Setup AES encryption
#if NET35_OR_GREATER || NETCOREAPP
            using Aes aes = Aes.Create() ?? throw new InvalidOperationException("AES not available. Change your system settings");
#else
            using Rijndael aes = Rijndael.Create() ?? throw new InvalidOperationException("AES not available. Change your system settings"); ;
#endif

            // Set AES settings
            aes.Key = D2AesKey;
            aes.IV = D2AesIV;
            aes.Padding = PaddingMode.None;
            aes.Mode = CipherMode.CBC;

            // Perform AES encryption
            using MemoryStream stream = new();
            using ICryptoTransform dec = aes.CreateDecryptor();
            using CryptoStream cs = new(stream, dec, CryptoStreamMode.Write);
            cs.Write(d2, 0, 16);
            cs.FlushFinalBlock();

            // Save encrypted key to field
            return stream.ToArray();
        }

        /// <summary>
        /// Generates Data1Key, Data2Key, and PIC from the .getkey.log file
        /// </summary>
        /// <param name="getKeyLog">Path to the .getkey.log file</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="FileNotFoundException"></exception>
        /// <exception cref="InvalidDataException"></exception>
        private protected void ParseGetKeyLog(string getKeyLog)
        {
            // Validate .getkey.log file path
            if (!System.IO.File.Exists(getKeyLog))
                throw new FileNotFoundException(nameof(getKeyLog));

            // Read from .getkey.log file
            using StreamReader sr = System.IO.File.OpenText(getKeyLog);

            // Determine whether GetKey was successful
            string line;
            while ((line = sr.ReadLine()) != null && line.Trim().StartsWith("get_dec_key succeeded!") == false) ;
            if (line == null)
                throw new InvalidDataException(".getkey.log contains errors");

            // Look for Disc Key in log
            byte[] discKey;
            while ((line = sr.ReadLine()) != null && line.Trim().StartsWith("disc_key = ") == false) ;
            if (line == null)
                throw new InvalidDataException("Could not find Disc Key in .getkey.log");
            // Get Disc Key from log
            string discKeyStr = line.Substring("disc_key = ".Length);
            // Validate Disc Key from log
            if (discKeyStr.Length != 32)
                throw new InvalidDataException("Unexpected Disc Key in .getkey.log");
            // Convert Disc Key to byte array
            discKey = HexStringToByteArray(discKeyStr);

            // Read Disc ID
            byte[] discID;
            while ((line = sr.ReadLine()) != null && line.Trim().StartsWith("disc_id = ") == false) ;
            if (line == null)
                throw new InvalidDataException("Could not find Disc ID in .getkey.log");
            // Get Disc ID from log
            string discIDStr = line.Substring("disc_id = ".Length);
            // Validate Disc ID from log
            if (discIDStr.Length != 32)
                throw new InvalidDataException("Unexpected Disc ID in .getkey.log");
            // Replace X's in Disc ID with 00000001
            discIDStr = discIDStr.Substring(0, 24) + "00000001";
            // Convert Disc ID to byte array
            discID = HexStringToByteArray(discIDStr);

            // Look for PIC in log
            byte[] discPIC;
            while ((line = sr.ReadLine()) != null && line.Trim().StartsWith("PIC:") == false) ;
            if (line == null)
                throw new InvalidDataException("Could not find PIC in .getkey.log");
            // Get PIC from log
            string discPICStr = "";
            for (int i = 0; i < 8; i++)
                discPICStr += sr.ReadLine() ?? throw new InvalidDataException("Incomplete PIC in .getkey.log");
            // Validate PIC from log
            if (discPICStr.Length != 256)
                throw new InvalidDataException("Unexpected PIC in .getkey.log");
            // Convert PIC to byte array
            discPIC = HexStringToByteArray(discPICStr.Substring(0, 230));

            // Double check for warnings in .getkey.log
            while ((line = sr.ReadLine()) != null && line.Trim().StartsWith("WARNING") == false && line.Trim().StartsWith("SUCCESS") == false)
            {
                string t = line.Trim();
                if (t.StartsWith("WARNING"))
                    throw new InvalidDataException(".getkey.log contains errors");
                else if (t.StartsWith("SUCCESS"))
                    break;
            }

            // Parse DiscKey, DiscID, and PIC
            DiscKey = discKey;
            DiscID = discID;
            PIC = discPIC;
        }

        /// <summary>
        /// Constructor for generating values from an ISO file
        /// </summary>
        /// <param name="isoPath">Path to the ISO</param>
        /// <param name="redump">True if redump-style IRD</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="FileNotFoundException"></exception>
        /// <exception cref="IOException"></exception>
        private protected void GenerateIRD(string isoPath, bool redump = false)
        {
            // Parse ISO file as a file stream
            using FileStream fs = new FileStream(isoPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan) ?? throw new FileNotFoundException(isoPath);
            // Validate ISO file stream
            if (!CDReader.Detect(fs))
                throw new IOException("Not a valid ISO file");

            // New ISO Reader from DiscUtils
            CDReader reader = new(fs);

            // If generating redump-style IRD
            if (redump)
            {
                // Redump-style IRDs set the lowest bit of ExtraConfig to 1
                ExtraConfig |= 0x01;

                // Try to get Title ID and Disc Version from PS3_DISC.SFB
                try
                {
                    // Redump-style IRDs use fields from PS3_DISC.SFB
                    using SparseStream s = reader.OpenFile("PS3_DISC.SFB", FileMode.Open, FileAccess.Read);

                    // Parse PS3_DISC.SFB file
                    PS3_DiscSFB ps3_DiscSFB = new(s);

                    bool titleIDFound = ps3_DiscSFB.Field.TryGetValue("TITLE_ID", out string titleID);
                    // If a valid TITLE_ID field is present, remove the hyphen to fit into standard IRD file
                    if (titleIDFound && titleID.Length == 10 && titleID[4] == '-')
                        TitleID = titleID.Substring(0, 4) + titleID.Substring(5, 5);

                    // If the version field is present, this is a multi-game disc
                    // Redump-style IRDs use the VERSION field from PS3_DISC.SFB instead of VERSION from PARAM.SFO
                    bool discVersionFound = ps3_DiscSFB.Field.TryGetValue("VERSION", out string discVersion);
                    if (discVersionFound)
                        DiscVersion = discVersion;
                }
                catch { }
            }

            // Try to get Title ID, Title, Disc Version, and App Version from PARAM.SFO
            try
            {
                // Read PS3 Metadata from PARAM.SFO
                using SparseStream s = reader.OpenFile("\\PS3_GAME\\PARAM.SFO", FileMode.Open, FileAccess.Read);

                // Parse PARAM.SFO file
                ParamSFO paramSFO = new(s);

                // If PS3_DISC.SFB did not set TitleID, use PARAM.SFO TITLE_ID
                if (TitleID == null)
                {
                    bool titleIDFound = paramSFO.Field.TryGetValue("TITLE_ID", out string titleID);
                    if (titleIDFound)
                        TitleID = titleID.Length == 9 ? titleID : titleID.PadRight(9, '\0').Substring(0, 9);
                    else
                        TitleID = "\0\0\0\0\0\0\0\0\0";
                }

                // Try use Title from PARAM.SFO
                bool titleFound = paramSFO.Field.TryGetValue("TITLE", out string title);
                Title = titleFound ? title : String.Empty;

                // If PS3_DISC.SFB did not set DiscVersion, try use PARAM.SFO VERSION
                if (DiscVersion == null)
                {
                    bool discVersionFound = paramSFO.Field.TryGetValue("VERSION", out string discVersion);
                    if (discVersionFound)
                        DiscVersion = discVersion.Length == 5 ? discVersion : discVersion.PadRight(5, '\0').Substring(0, 5);
                    else
                        DiscVersion = "\0\0\0\0\0";
                }

                // Try use App Version from PARAM.SFO 
                bool appVersionFound = paramSFO.Field.TryGetValue("APP_VER", out string appVersion);
                if (appVersionFound)
                    AppVersion = appVersion.Length == 5 ? appVersion : appVersion.PadRight(5, '\0').Substring(0, 5);
                else
                    AppVersion = "\0\0\0\0\0";
            }
            catch { }

            // Ensure Title ID is set and valid
            if (string.IsNullOrEmpty(TitleID) || TitleID.Length != 9)
                TitleID = "\0\0\0\0\0\0\0\0\0";

            // Ensure Title is set
            Title ??= string.Empty;

            // Ensure Disc Version is set and valid
            if (string.IsNullOrEmpty(DiscVersion) || DiscVersion.Length != 5)
                DiscVersion = "\0\0\0\0\0";

            // Ensure App Version is set and valid
            if (string.IsNullOrEmpty(AppVersion) || AppVersion.Length != 5)
                AppVersion = "\0\0\0\0\0";

            // Try determine system update version
            try
            {
                GetSystemVersion(fs, reader);
            }
            catch { }

            // Ensure System Version was set and valid
            if (string.IsNullOrEmpty(SystemVersion) || SystemVersion.Length != 4)
                SystemVersion = "\0\0\0\0";

            // Recursively count all files in ISO to allocate file arrays
            DiscDirectoryInfo rootDir = reader.GetDirectoryInfo("\\");
            FileCount = 0;
            CountFiles(rootDir);

            // Pre-allocate arrays and reset file count
            FileKeys = new long[FileCount];
            FileExtents = new Range<long, long>[FileCount][];
            uint fileCount = FileCount;
            FileCount = 0;

            // Determine file offsets
            GetFiles(fs, reader, rootDir);

            // Resize arrays if non-contiguous files were detected
            if (FileCount != fileCount)
            {
                long[] tempFileKeys = FileKeys;
                Array.Resize(ref tempFileKeys, (int)FileCount);
                FileKeys = tempFileKeys;
                Range<long, long>[][] tempFileExtents = FileExtents;
                Array.Resize(ref tempFileExtents, (int)FileCount);
                FileExtents = tempFileExtents;
            }
            // Sort files by offset
            Array.Sort(FileKeys, FileExtents);

            // Determine start of footer if no update file present
            if (UpdateEnd == 0)
            {
                int lastFile = FileExtents.Length - 1;
                int lastExtent = FileExtents[lastFile].Length - 1;
                UpdateEnd = SectorSize * FileExtents[lastFile][lastExtent].Offset + FileExtents[lastFile][lastExtent].Count;
            }

            // Read and compress the ISO header
            GetHeader(fs, reader);

            // Read and compress the ISO footer
            GetFooter(fs);

            // Get info region info from ISO
            GetRegions(fs);

            // Calculate CRC32 hash of ISO only if generating a redump IRD and the UID is not already set
            RegionHashes = new byte[RegionCount][];
            FileHashes = new byte[FileCount][];
            HashISO(fs, redump && UID == 0x00000000);
        }

#endregion

        #region Reading ISO

        /// <summary>
        /// Retreives and stores the system version
        /// </summary>
        /// <remarks>PS3UPDAT.PUP update file version number</remarks>
        /// <param name="fs">ISO filestream</param>
        /// <param name="reader">CDReader</param>
        /// <exception cref="IOException"></exception>
        private void GetSystemVersion(FileStream fs, CDReader reader)
        {
            // Determine PUP file offset via cluster
            Range<long, long>[] updateClusters = reader.PathToClusters("\\PS3_UPDATE\\PS3UPDAT.PUP");
            if (updateClusters == null && updateClusters.Length == 0 && updateClusters[0] == null)
                throw new IOException("Invalid file extents for PS3UPDAT.PUP");

            // PS3UPDAT.PUP file begins at first byte of dedicated cluster
            long updateOffset = SectorSize * updateClusters[0].Offset;
            // Update file ends at the last byte of the last cluster
            int lastExtent = updateClusters.Length - 1;
            UpdateEnd = SectorSize * updateClusters[lastExtent].Offset + updateClusters[lastExtent].Count;

            // Check PUP file Magic
            fs.Seek(updateOffset, SeekOrigin.Begin);
            byte[] pupMagic = new byte[5];
            fs.Read(pupMagic, 0, pupMagic.Length);
            // If magic is incorrect, set version to all nulls, "\0\0\0\0" (unknown)
            if (Encoding.ASCII.GetString(pupMagic) != "SCEUF")
                SystemVersion = "\0\0\0\0";
            else
            {
                // Determine location of version string
                fs.Seek(updateOffset + 0x3E, SeekOrigin.Begin);
                byte[] offset = new byte[2];
                fs.Read(offset, 0, 2);
                // Move stream to PUP version string
                Array.Reverse(offset);
                ushort versionOffset = BitConverter.ToUInt16(offset, 0);
                fs.Seek(updateOffset + versionOffset, SeekOrigin.Begin);
                // Read version string
                byte[] version = new byte[4];
                fs.Read(version, 0, version.Length);
                // Set version string
                SystemVersion = Encoding.ASCII.GetString(version);
            }
        }

        /// <summary>
        /// Retreives and stores the header
        /// </summary>
        /// <param name="fs">ISO filestream</param>
        /// <param name="reader">CDReader</param>
        /// <exception cref="IOException"></exception>
        private void GetHeader(FileStream fs, CDReader reader)
        {
            // Determine the extent of the header via cluster (Sector 0 to first data sector)
            Range<long, long>[] sfbClusters = reader.PathToClusters("\\PS3_DISC.SFB");
            if (sfbClusters == null && sfbClusters.Length == 0 && sfbClusters[0] == null)
                throw new IOException("Invalid file extents for PS3_DISC.SFB");
            // End of header is at beginning of first byte of dedicated cluster
            FirstDataSector = sfbClusters[0].Offset;

            // Begin a GZip stream to write header to
            using MemoryStream headerStream = new();
#if NET6_0_OR_GREATER
            using (GZipStream gzStream = new(headerStream, CompressionLevel.SmallestSize))
#elif NETCOREAPP || NET45_OR_GREATER
            using (GZipStream gzStream = new(headerStream, CompressionLevel.Optimal))
#else
            using (GZipStream gzStream = new(headerStream, CompressionMode.Compress))
#endif
            {
                // Start reading data from the beginning of the ISO file
                fs.Seek(0, SeekOrigin.Begin);
                byte[] buf = new byte[SectorSize];
                int numBytes;

                // Read all data before the first data sector
                for (int i = 0; i < FirstDataSector; i++)
                {
                    numBytes = fs.Read(buf, 0, buf.Length);
                    gzStream.Write(buf, 0, numBytes);
                }
            }

            // Save stream to field
            Header = headerStream.ToArray();
            HeaderLength = (uint)Header.Length;
        }

        /// <summary>
        /// Retreives and stores the footer
        /// </summary>
        /// <param name="fs">ISO filestream</param>
        private void GetFooter(FileStream fs)
        {
            // Begin a GZip stream to write footer to
            using MemoryStream footerStream = new();
#if NET6_0_OR_GREATER
            using (GZipStream gzStream = new(footerStream, CompressionLevel.SmallestSize))
#elif NETCOREAPP || NET45_OR_GREATER
            using (GZipStream gzStream = new(footerStream, CompressionLevel.Optimal))
#else
            using (GZipStream gzStream = new(footerStream, CompressionMode.Compress))
#endif
            {
                // Start reading data from after last file (PS3UPDAT.PUP)
                fs.Seek(UpdateEnd, SeekOrigin.Begin);
                byte[] buf = new byte[SectorSize];
                int numBytes = (int)SectorSize;

                // Keep reading data until there is none left to read
                while (numBytes != 0)
                {
                    numBytes = fs.Read(buf, 0, buf.Length);
                    gzStream.Write(buf, 0, numBytes);
                }
            }

            // Save stream to field
            Footer = footerStream.ToArray();
            FooterLength = (uint)Footer.Length;
        }

        /// <summary>
        /// Retreives and stores the Region extents
        /// </summary>
        /// <param name="fs">ISO filestream</param>
        /// <exception cref="IOException"></exception>
        private void GetRegions(FileStream fs)
        {
            // Determine the number of unencryted regions
            fs.Seek(0, SeekOrigin.Begin);
            byte[] decRegionCount = new byte[4];
            fs.Read(decRegionCount, 0, 4);

            // Total number of regions is 2x number of unencrypted regions, minus 1
            RegionCount = (byte)(2 * ((uint)decRegionCount[3]) - 1);
            if (RegionCount <= 0)
                throw new IOException("No regions detected in ISO");
            RegionStart = new long[RegionCount];
            RegionEnd = new long[RegionCount];

            // Determine the extent for each region
            byte[] regionSector = new byte[4];
            fs.Seek(8, SeekOrigin.Begin);
            fs.Read(regionSector, 0, 4);
            Array.Reverse(regionSector, 0, 4);
            for (int i = 0; i < RegionCount; i++)
            {
                // End sector of previous region is start of this region
                if (i % 2 == 1)
                    RegionStart[i] = BitConverter.ToInt32(regionSector, 0) + 1;
                else
                    RegionStart[i] = BitConverter.ToInt32(regionSector, 0);
                // Determine end sector offset of this region
                fs.Read(regionSector, 0, 4);
                Array.Reverse(regionSector, 0, 4);
                if (i % 2 == 1)
                    RegionEnd[i] = BitConverter.ToInt32(regionSector, 0) - 1;
                else
                    RegionEnd[i] = BitConverter.ToInt32(regionSector, 0);
            }

            // Remove header from first region
            RegionStart[0] = FirstDataSector;
            // Remove footer from last region
            int lastRegion = RegionEnd.Length - 1;
            RegionEnd[lastRegion] = (UpdateEnd / SectorSize) - 1;
        }

        /// <summary>
        /// Determine and store file extents for all files and files within subdirectories recursively
        /// </summary>
        /// <param name="fs">ISO filestream</param>
        /// <param name="reader">CDReader</param>
        /// <param name="dir">Folder to search for files within</param>
        private void GetFiles(FileStream fs, CDReader reader, DiscDirectoryInfo dir)
        {
            // Process all files in current directory
            foreach (DiscFileInfo fileInfo in dir.GetFiles())
            {
                string filePath = fileInfo.FullName;

                // Determine the extents of the file via clusters
                Range<long, long>[] fileExtent = reader.PathToClusters(filePath);

                // If invalid clusters were returned, we can't hash this file
                if (fileExtent == null && fileExtent.Length == 0)
                    throw new IOException($"Unexpected file extents for {filePath}");

                // Determine smallest file offset as first sector
                long smallestOffset = fileExtent[0].Offset;
                bool nonContiguous = false;
                for (int i = 1; i < fileExtent.Length; i++)
                {
                    if (fileExtent[i] == null)
                        throw new IOException($"Unexpected file extents for {filePath}");

                    if (fileExtent[i].Offset * SectorSize != fileExtent[i - 1].Offset * SectorSize + fileExtent[i - 1].Count)
                        nonContiguous = true;

                    if (fileExtent[i].Offset < smallestOffset)
                        smallestOffset = fileExtent[i].Offset;
                }

                // If already encountered file offset, skip this file
                if (Array.Exists(FileKeys, element => element == smallestOffset))
                    continue;
                else if (nonContiguous)
                    Console.WriteLine($"Non-contiguous file found: {filePath}");

                // Add file offset to keys and extents to extents
                FileKeys[FileCount] = smallestOffset;
                FileExtents[FileCount] = fileExtent;
                FileCount++;
            }

            // Recursively process all subfolders of current directory
            foreach (DiscDirectoryInfo dirInfo in dir.GetDirectories())
            {
                GetFiles(fs, reader, dirInfo);
            }
        }

        /// <summary>
        /// Decrypts a given byte array of sector(s)
        /// </summary>
        /// <param name="buffer">Byte array to be decrypted</param>
        /// <param name="sectorNumber">Sector number of first sector being decrypted</param>
        /// <param name="offset">Number of sectors to skip</param>
        /// <param name="count">Number of sectors to decrypt, beginning from offset</param>
        /// <exception cref="InvalidOperationException"></exception>
        private protected void DecryptSectors(ref byte[] buffer, int sectorNumber, int offset = 0, int? count = null)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            if (buffer.Length == 0 || buffer.Length % SectorSize != 0)
                throw new ArgumentException("Encrypted buffer must be multiple of SectorSize");

            if (offset < 0 || offset >= buffer.Length / SectorSize)
                throw new ArgumentException("Offset sector must be within buffer");

            count ??= (int)(buffer.Length / SectorSize) - offset;

            if (count < 0 || count > (buffer.Length / SectorSize) - offset)
                throw new ArgumentException("Number of sectors must be within buffer");

            // Setup AES decryption
#if NET35_OR_GREATER || NETCOREAPP
            using Aes aes = Aes.Create() ?? throw new InvalidOperationException("AES not available. Change your system settings");
#else
            using Rijndael aes = Rijndael.Create() ?? throw new InvalidOperationException("AES not available. Change your system settings"); ;
#endif

            // Set AES settings
            aes.Key = DiscKey;
            aes.Padding = PaddingMode.None;
            aes.Mode = CipherMode.CBC;

            // Convert offset and count to number of bytes
            offset *= (int)SectorSize;
            count *= (int)SectorSize;
            // Decrypt buffer one sector at a time
            for (int i = offset; i < offset + count; i += (int)SectorSize)
            {
                // Determine AES Initial Value based on sector number
                byte[] iv = new byte[16];
                int tempNum = sectorNumber;
                for (int j = 0; j < 16; j++)
                {
                    iv[16 - j - 1] = (byte)(tempNum & 0xFF);
                    tempNum >>= 8;
                }
                aes.IV = iv;

                // Perform AES decryption
                using MemoryStream stream = new();
                using CryptoStream cs = new(stream, aes.CreateDecryptor(), CryptoStreamMode.Write);
                cs.Write(buffer, i, (int)SectorSize);
                cs.FlushFinalBlock();

                // Write decrypted sector to output
                stream.ToArray().CopyTo(buffer, i);
                sectorNumber++;
            }
            return;
        }

        /// <summary>
        /// Recursively determines file count
        /// </summary>
        /// <param name="dir"></param>
        private void CountFiles(DiscDirectoryInfo dir)
        {
            FileCount += (uint)dir.GetFiles().Length;
            foreach (DiscDirectoryInfo dirInfo in dir.GetDirectories())
                CountFiles(dirInfo);
        }

        #endregion

        #region Hashing

        /// <summary>
        /// Calculate hashes for all region and file extents
        /// </summary>
        /// <param name="fs">ISO filestream</param>
        /// <param name="redump">True if also calculating CRC32 of entire ISO</param>
        private void HashISO(FileStream fs, bool redump)
        {
            // Initialise CRC32 ISO hasher, only used if making redump-style IRD
            byte[] crc32;
            HashWrapper isoHasher = new(HashType.CRC32);

            // Initialise MD5 region hashes
            List<int> regions = [];
            HashWrapper[] regionMD5 = new HashWrapper[RegionCount];
            for (int i = 0; i < RegionCount; i++)
            {
                regions.Add(i);
                regionMD5[i] = new(HashType.MD5);
            }

            // Initialise MD5 file hashes
            List<int> files = [];
            HashWrapper[] fileMD5 = new HashWrapper[FileCount];
            for (int i = 0; i < FileCount; i++)
            {
                files.Add(i);
                fileMD5[i] = new(HashType.MD5);
            }

            // Start hashing from beginning of ISO
            long currentSector = 0;
            fs.Seek(currentSector, SeekOrigin.Begin);

            // Read from ISO, 1024 sectors at a time
            int bufSectors = 1024;
            byte[] buf = new byte[bufSectors * SectorSize];
            while (true)
            {
                // Attempt to read a full buffer
                bufSectors = 1024;
                int numBytes = fs.Read(buf, 0, buf.Length);

                // If end of ISO reached, stop reading
                if (numBytes == 0)
                {
                    // Ensure all hashes have been saved
                    foreach (int i in regions)
                    {
                        // Close region hash
                        regionMD5[i].Terminate();
                        RegionHashes[i] = regionMD5[i].CurrentHashBytes;
                        regionMD5[i].Dispose();
                    }
                    foreach (int i in files)
                    {
                        // Close file hash
                        fileMD5[i].Terminate();
                        FileHashes[i] = fileMD5[i].CurrentHashBytes;
                        fileMD5[i].Dispose();
                    }

                    // If making redump-style IRD, save CRC32 hash to UID field
                    if (redump)
                    {
                        isoHasher.Terminate();
                        crc32 = isoHasher.CurrentHashBytes;
                        isoHasher.Dispose();
                        UID = BitConverter.ToUInt32(crc32, 0);
                    }

                    return;
                }

                // Keep trying to read to fill buffer, remove once partial buffer hashing is supported
                if (numBytes != buf.Length)
                {
                    while (numBytes % SectorSize != 0)
                    {
                        int newNumBytes = fs.Read(buf, numBytes, buf.Length - numBytes);
                        numBytes += newNumBytes;

                        // If end of ISO reached, trim buffer and hash
                        if (newNumBytes == 0 && numBytes % SectorSize != 0)
                        {
                            //numBytes -= numBytes % (int)SectorSize;
                            Console.Error.WriteLine("ERROR: ISO filestream ended early");
                            break;
                        }
                    }
                    // Only hash portion of buffer
                    bufSectors = numBytes / (int)SectorSize;
                    if (bufSectors == 0)
                        Console.Error.WriteLine("ERROR: Trailing partial sector in ISO filestream");
                    if (numBytes > buf.Length)
                        throw new IOException("ERROR: Read more bytes than buffer size???");
                }

                // Hash ISO
                if (redump)
                    isoHasher.Process(buf, 0, numBytes);

                // Hash regions
                List<int> regionsEnded = [];
                foreach (int i in regions)
                {
                    // Stop hashing regions if current region has not yet started (assumes regions are ordered)
                    if (RegionStart[i] > currentSector + bufSectors)
                        break;

                    // Skip region if it has already ended
                    //if (RegionEnd[i] < currentSector)
                    //    continue;

                    // Check if region has ended in this buffer [We know: Start is not in the future, Ending is not in the past]
                    if (RegionEnd[i] < currentSector + bufSectors)
                    {
                        // Determine start byte, if region is entirely within the buffer
                        int startByte = RegionStart[i] > currentSector ? (int)(SectorSize * (RegionStart[i] - currentSector)) : 0;
                        // Determine end byte
                        int endByte = (int)(SectorSize * (RegionEnd[i] - currentSector + 1));
                        // Close region hash
                        regionMD5[i].Process(buf, startByte, endByte - startByte);
                        regionMD5[i].Terminate();
                        RegionHashes[i] = regionMD5[i].CurrentHashBytes;
                        regionMD5[i].Dispose();
                        regionsEnded.Add(i);
                    }
                    // Check if region has already begun
                    else if (RegionStart[i] <= currentSector)
                    {
                        // Hash buffer
                        regionMD5[i].Process(buf, 0, (int)SectorSize * bufSectors);
                    }
                    // Region Start is in this buffer, ending is in the future
                    else
                    {
                        // Hash partial buffer
                        int regionStart = (int)(SectorSize * (RegionStart[i] - currentSector));
                        regionMD5[i].Process(buf, regionStart, (int)SectorSize * bufSectors - regionStart);
                    }
                }
                if (regionsEnded.Count > 0)
                    regions.RemoveAll(item => regionsEnded.Contains(item));

                // Decrypt any encrypted sectors of buffer
                for (int i = 1; i < RegionCount; i += 2)
                {
                    // If the current encrypted region is within the buffer
                    if (RegionStart[i] < currentSector + bufSectors
                        && RegionEnd[i] >= currentSector)
                    {
                        // First sector to decrypt from
                        int encOffset = 0;

                        // Don't decrypt initial sectors if the encrypted region starts within this buffer
                        if (RegionStart[i] > currentSector)
                            encOffset = (int)(RegionStart[i] - currentSector);

                        // Number of sectors to decrypt
                        int encCount = bufSectors - encOffset;

                        // Don't decrypt last sectors if the encrypted region ends within this buffer
                        if (RegionEnd[i] < currentSector + bufSectors)
                            encCount -= (int)(currentSector + bufSectors - RegionEnd[i] - 1);

                        // Decrypt encrypted sectors
                        DecryptSectors(ref buf, (int)currentSector + encOffset, encOffset, encCount);
                    }
                }

                // Hash files
                List<int> filesEnded = [];
                foreach (int i in files)
                {
                    // Stop hashing files if current file has not yet started (assumes FileKeys are sorted)
                    if (FileKeys[i] > currentSector + bufSectors)
                        break;

                    // Hash each file extent for each file
                    for (int j = 0; j < FileExtents[i].Length; j++)
                    {
                        // Skip hashing file extent if it has not yet started or already ended
                        if (FileExtents[i][j].Offset > currentSector + bufSectors
                            || SectorSize * FileExtents[i][j].Offset + FileExtents[i][j].Count < SectorSize * currentSector)
                            continue;
                        // Determine first file byte location in buffer
                        int startByte = FileExtents[i][j].Offset > currentSector ? (int)(SectorSize * (FileExtents[i][j].Offset - currentSector)) : 0;
                        // Determine last file byte location in buffer
                        int endByte = (int)(FileExtents[i][j].Count - SectorSize * (currentSector - FileExtents[i][j].Offset));
                        // Don't hash more than the buffer size
                        endByte = endByte < bufSectors * (int)SectorSize ? endByte : bufSectors * (int)SectorSize;
                        // Hash portion of buffer that file exists in
                        fileMD5[i].Process(buf, startByte, endByte - startByte);
                    }

                    // Check if current file has ended in this buffer (assumes last extent contains last byte)
                    int lastExtent = FileExtents[i].Length - 1;
                    long lastByte = SectorSize * FileExtents[i][lastExtent].Offset + FileExtents[i][lastExtent].Count;
                    if (lastByte <= SectorSize * (currentSector + bufSectors)
                        && lastByte > SectorSize * currentSector)
                    {
                        // Close file hash
                        fileMD5[i].Terminate();
                        FileHashes[i] = fileMD5[i].CurrentHashBytes;
                        fileMD5[i].Dispose();
                        filesEnded.Add(i);
                    }
                }
                if (filesEnded.Count > 0)
                    files.RemoveAll(item => filesEnded.Contains(item));

                currentSector += bufSectors;
            }
        }

        #endregion

        #region IRD File

        /// <summary>
        /// Write IRD data to file
        /// </summary>
        /// <param name="irdPath">Path to the ISO</param>
        /// <exception cref="ArgumentNullException"></exception>
        public void Write(string irdPath)
        {
            // Validate irdPath
            if (irdPath == null || irdPath.Length <= 0)
                throw new ArgumentNullException(nameof(irdPath));

            // Create new stream to uncompressed IRD contents
            using MemoryStream stream = new();

            // Write IRD data to stream as UTF8
            using BinaryWriter bw = new(stream, Encoding.UTF8);

            // IRD File Signature
            bw.Write(Magic);

            // IRD File Version
            bw.Write(Version);

            // PARAM.SFO / TITLE_ID
            byte[] titleIDBuf = Encoding.ASCII.GetBytes(TitleID);
            bw.Write(titleIDBuf, 0, 9);

            // PARAM.SFO / TITLE
            bw.Write(Title);

            // PARAM.SFO / PS3_SYSTEM_VER
            byte[] systemVersionBuf = Encoding.ASCII.GetBytes(SystemVersion);
            bw.Write(systemVersionBuf, 0, 4);

            // PARAM.SFO / VERSION
            byte[] buf = Encoding.ASCII.GetBytes(DiscVersion);
            byte[] discVersionBuf = new byte[5];
            Array.Copy(buf, 0, discVersionBuf, 0, buf.Length);
            bw.Write(discVersionBuf, 0, 5);

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
            {
                if (RegionHashes[i] == null)
                    bw.Write(NullMD5);
                else
                    bw.Write(RegionHashes[i], 0, 16);
            }

            // Number of files hashed
            bw.Write(FileCount);

            // Hashes for each file
            for (int i = 0; i < FileCount; i++)
            {
                bw.Write(FileKeys[i]);
                if (FileHashes[i] == null)
                    bw.Write(NullMD5);
                else
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

            // Calculate the little-endian 32-bit "IEEE 802.3" CRC value of the IRD contents so far
            stream.Position = 0;
            byte[] crc32 = HashTool.GetStreamHashArray(stream, HashType.CRC32, true);

            // Write final CRC value to the stream
            stream.Write(crc32, 0, 4);

            // Create the IRD file stream
            using FileStream fs = new(irdPath, FileMode.Create, FileAccess.Write);
            // Create a GZipped IRD file stream
#if NET6_0_OR_GREATER
            using GZipStream gzStream = new(fs, CompressionLevel.SmallestSize);
#elif NETCOREAPP || NET45_OR_GREATER
            using GZipStream gzStream = new(fs, CompressionLevel.Optimal);
#else
            using GZipStream gzStream = new(fs, CompressionMode.Compress);
#endif
            // Write entire gzipped IRD stream to file
            stream.Position = 0;
#if NET40_OR_GREATER || NETCOREAPP
            stream.CopyTo(gzStream);
#else
            byte[] buffer = new byte[1024];
            int numBytes;
            while ((numBytes = stream.Read(buffer, 0, buffer.Length)) > 0)
                gzStream.Write(buffer, 0, numBytes);
#endif
        }

        /// <summary>
        /// Read and store IRD data from an existing file
        /// </summary>
        /// <param name="irdPath">Path to the IRD file</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="FileNotFoundException"></exception>
        public static IRD Read(string irdPath)
        {
            // Validate irdPath
            if (irdPath == null || irdPath.Length <= 0)
                throw new ArgumentNullException(nameof(irdPath));
            if (!System.IO.File.Exists(irdPath))
                throw new FileNotFoundException(nameof(irdPath));

            // Open IRD for reading
            using FileStream fs = new(irdPath, FileMode.Open, FileAccess.Read);
            using GZipStream gzs = new(fs, CompressionMode.Decompress);
            using BinaryReader br = new(gzs);

            // Check for IRD file signature
            byte[] magic = br.ReadBytes(4);
            if (magic == null || magic.Length != 4 || magic[0] != Magic[0] || magic[1] != Magic[1] || magic[2] != Magic[2] || magic[3] != Magic[3])
                throw new InvalidDataException("IRD file not recognised");

            // Read file version
            byte version = br.ReadByte();
            if (version < 6 || version > 9)
                throw new InvalidDataException("Unsupported IRD version detected");

            // Read Title ID and Title
            string titleID = Encoding.ASCII.GetString(br.ReadBytes(9));
            string title = br.ReadString();

            // Read System, Game, and App Version
            string sysVersion = Encoding.ASCII.GetString(br.ReadBytes(4));
            string discVersion = Encoding.ASCII.GetString(br.ReadBytes(5));
            string appVersion = Encoding.ASCII.GetString(br.ReadBytes(5));

            // Read UID (for Version 7)
            uint uid = 0;
            if (version == 7)
                uid = br.ReadUInt32();

            // Read Header
            uint headerLength = br.ReadUInt32();
            byte[] header = br.ReadBytes((int)headerLength);

            // Read Footer
            uint footerLength = br.ReadUInt32();
            byte[] footer = br.ReadBytes((int)footerLength);

            // Read region hashes
            byte regionCount = br.ReadByte();
            byte[][] regionHashes = new byte[regionCount][];
            for (int i = 0; i < regionCount; i++)
                regionHashes[i] = br.ReadBytes(16);

            // Read file hashes
            uint fileCount = br.ReadUInt32();
            long[] fileKeys = new long[fileCount];
            byte[][] fileHashes = new byte[fileCount][];
            for (int i = 0; i < fileCount; i++)
            {
                fileKeys[i] = br.ReadInt64();
                fileHashes[i] = br.ReadBytes(16);
            }

            // Read extra config and number of attachments
            ushort extraConfig = br.ReadUInt16();
            ushort attachments = br.ReadUInt16();

            // Read PIC data (for Version 8 and prior)
            byte[] pic = new byte[115];
            if (version >= 9)
                pic = br.ReadBytes(115);

            // Read Data 1 Key, Data 2 Key
            byte[] d1 = br.ReadBytes(16);
            byte[] d2 = br.ReadBytes(16);

            // Read PIC data (for Version 8 and prior)
            if (version < 9)
                pic = br.ReadBytes(115);

            // Read UID (for Version 8 onwards)
            if (version > 7)
                uid = br.ReadUInt32();

            // Read and CRC32 hash
            byte[] crc = br.ReadBytes(4);

            // Create new IRD object with read parameters
            return new IRD(version,
                           titleID,
                           title,
                           sysVersion,
                           discVersion,
                           appVersion,
                           header,
                           footer,
                           regionHashes,
                           fileKeys,
                           fileHashes,
                           extraConfig,
                           attachments,
                           d1,
                           d2,
                           pic,
                           uid);
        }

        /// <summary>
        /// Prints IRD fields to console
        /// </summary>
        /// <param name="printPath">Optional path to save file to</param>
        /// <param name="printAll">Optionally print IRD name in output header</param>
        /// <param name="printAll">Optionally print all IRD data</param>
        public void Print(string printPath = null, string irdName = null, bool printAll = false)
        {
            // Build string from parameters
            StringBuilder printText = new();
            if (irdName == null)
                printText.AppendLine("IRD Contents:");
            else
                printText.AppendLine($"IRD Contents: {irdName}");
            printText.AppendLine("=============");

            // Append IRD fields to string builder
            printText.AppendLine($"Magic:        {Encoding.ASCII.GetString(Magic)}");
            printText.AppendLine($"IRD Version:  {Version}");
            printText.AppendLine($"Title ID:     {TitleID}");
            printText.AppendLine($"Title:        {Title}");
            printText.AppendLine($"PUP Version:  {SystemVersion}");
            printText.AppendLine($"Disc Version: {DiscVersion}");
            printText.AppendLine($"App Version:  {AppVersion}");
            if (printAll)
            {
                printText.AppendLine($"Header:       {ByteArrayToHexString(Header)}");
                printText.AppendLine($"Footer:       {ByteArrayToHexString(Footer)}");
            }
            printText.AppendLine($"Regions:      {RegionCount}");
            if (printAll)
            {
                for (int i = 0; i < RegionCount; i++)
                {
                    printText.Append($"              Region {i} : ");
                    if (RegionHashes[i] == null)
                        printText.AppendLine($"{ByteArrayToHexString(NullMD5)}");
                    else
                        printText.AppendLine($"{ByteArrayToHexString(RegionHashes[i])}");
                }
            }
            printText.AppendLine($"Files:        {FileCount}");
            if (printAll)
            {
                for (int i = 0; i < FileCount; i++)
                {
                    printText.Append($"              {FileKeys[i]:X7} : ");
                    if (FileHashes[i] == null)
                        printText.AppendLine($"{ByteArrayToHexString(NullMD5)}");
                    else
                        printText.AppendLine($"{ByteArrayToHexString(FileHashes[i])}");
                }
            }
            if (printAll || ExtraConfig != 0x0000)
                printText.AppendLine($"Extra Config: {ExtraConfig:X4}");
            if (printAll || Attachments != 0x0000)
                printText.AppendLine($"Attachments:  {Attachments:X4}");
            printText.AppendLine($"Unique ID:    {UID:X8}");
            printText.AppendLine($"Data 1 Key:   {ByteArrayToHexString(Data1Key)}");
            printText.AppendLine($"Data 2 Key:   {ByteArrayToHexString(Data2Key)}");
            printText.AppendLine($"PIC:          {ByteArrayToHexString(PIC)}");
            printText.AppendLine();

            if (printPath == null)
            {
                // Ensure UTF-8 will display properly
                Console.OutputEncoding = Encoding.UTF8;

                // Print formatted string
                Console.Write(printText);
            }
            else
            {
                System.IO.File.AppendAllText(printPath, printText.ToString());
            }
        }


        /// <summary>
        /// Prints IRD fields to a json object
        /// </summary>
        /// <param name="jsonPath">Optionally print to json file</param>
        /// <param name="single">Optionally print single object (no trailing comma)</param>
        /// <param name="printAll">Optionally print all IRD data</param>
        public void PrintJson(string jsonPath = null, bool single = true, bool printAll = false)
        {
            // Build string from parameters
            StringBuilder json = new();

            // Append IRD fields to string builder
            json.AppendLine("{");
            json.AppendLine($"  \"Magic\": \"{Encoding.ASCII.GetString(Magic)}\",");
            json.AppendLine($"  \"IRD Version\": \"{Version}\",");
            json.AppendLine($"  \"Title ID\": \"{TitleID}\",");
            json.AppendLine($"  \"Title\": \"{Title}\",");
            json.AppendLine($"  \"PUP Version\": \"{SystemVersion}\",");
            json.AppendLine($"  \"Disc Version\": \"{DiscVersion}\",");
            json.AppendLine($"  \"App Version\": \"{AppVersion}\",");
            if (printAll)
            {
                json.AppendLine($"  \"Header\": \"{ByteArrayToHexString(Header)}\",");
                json.AppendLine($"  \"Footer\": \"{ByteArrayToHexString(Footer)}\",");
            }
            if (!printAll)
                json.AppendLine($"  \"Regions\": \"{RegionCount}\",");
            else
            {
                json.Append($"  \"Regions\": [ ");
                for (int i = 0; i < RegionCount - 1; i++)
                {
                    if (RegionHashes[i] == null)
                        json.Append($"\"{ByteArrayToHexString(NullMD5)}\", ");
                    else
                        json.Append($"\"{ByteArrayToHexString(RegionHashes[i])}\", ");
                }
                if (RegionCount > 0)
                {
                    if (RegionHashes[RegionCount - 1] == null)
                        json.Append($"\"{ByteArrayToHexString(NullMD5)}\"");
                    else
                        json.Append($"\"{ByteArrayToHexString(RegionHashes[RegionCount - 1])}\"");
                }
                json.AppendLine(" ],");
            }
            if(!printAll)
                json.AppendLine($"  \"Files\": \"{FileCount}\",");
            else
            {
                json.Append($"  \"Files\": [ ");
                for (int i = 0; i < FileCount - 1; i++)
                {
                    json.Append($"  \"{FileKeys[i]}\" : ");
                    if (RegionHashes[i] == null)
                        json.Append($"\"{ByteArrayToHexString(NullMD5)}\", ");
                    else
                        json.Append($"\"{ByteArrayToHexString(FileHashes[i])}\", ");
                }
                if (FileCount > 0)
                {
                    json.Append($"  \"{FileKeys[FileCount - 1]}\" : ");
                    if (FileHashes[FileCount - 1] == null)
                        json.Append($"\"{ByteArrayToHexString(NullMD5)}\"");
                    else
                        json.Append($"\"{ByteArrayToHexString(FileHashes[FileCount - 1])}\"");
                }
                json.AppendLine(" ],");
            }
            if (printAll || ExtraConfig != 0x0000)
                json.AppendLine($"  \"Extra Config\": \"{ExtraConfig:X4}\",");
            if (printAll || Attachments != 0x0000)
                json.AppendLine($"  \"Attachments\": \"{Attachments:X4}\",");
            json.AppendLine($"  \"Unique ID\": \"{UID:X8}\",");
            json.AppendLine($"  \"Data 1 Key\": \"{ByteArrayToHexString(Data1Key)}\",");
            json.AppendLine($"  \"Data 2 Key\": \"{ByteArrayToHexString(Data2Key)}\",");
            json.AppendLine($"  \"PIC\": \"{ByteArrayToHexString(PIC)}\"");
            if (single)
                json.AppendLine("}");
            else
                json.AppendLine("},");

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
                System.IO.File.AppendAllText(jsonPath, json.ToString());
            }
        }

        #endregion

        #region IRD Validation

        /// <summary>
        /// Returns dictionary of file paths and their offset
        /// </summary>
        /// <param name="cd">CDReader object</param>
        public Dictionary<string, byte[]> GetFileHashes()
        {
            using MemoryStream headerStream = new MemoryStream(Header);
            using GZipStream gzStream = new GZipStream(headerStream, CompressionMode.Decompress);
            using MemoryStream isoStream = new MemoryStream();
#if NETCOREAPP || NETSTANDARD || NET40_OR_GREATER
            gzStream.CopyTo(isoStream);
#else
            byte[] buffer = new byte[SectorSize];
            int bytesRead;
            while ((bytesRead = gzStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                isoStream.Write(buffer, 0, bytesRead);
            }
#endif
            isoStream.Position = 0;
            using CDReader reader = new CDReader(isoStream);

            // Get all file paths and their offsets
            var files = new Dictionary<string, long>();
            DiscDirectoryInfo rootDir = reader.GetDirectoryInfo("\\");
            GetFileOffsets(reader, rootDir, files);

            // Get all paths at each offset
            var offsetToHash = new Dictionary<long, byte[]>();
            for (int i = 0; i < FileCount; i++)
                offsetToHash[FileKeys[i]] = FileHashes[i];

            // Return dictionary of paths and hashes
            var result = new Dictionary<string, byte[]>();
            foreach (KeyValuePair<string, long> kvp in files)
            {
                if (offsetToHash.ContainsKey(kvp.Value))
                    result[kvp.Key] = offsetToHash[kvp.Value];
            }
            return result;
        }

        /// <summary>
        /// Gets all file paths from the ISO header
        /// </summary>
        /// <param name="reader">CDReader object</param>
        /// <param name="path">Path to look within</param>
        /// <param name="files">Dictionary of files and their offset</param>
        public void GetFileOffsets(CDReader reader, DiscDirectoryInfo path, Dictionary<string, long> files)
        {
            foreach (DiscFileInfo fileInfo in path.GetFiles())
            {
                string filePath = fileInfo.FullName;
                Range<long, long>[] fileExtent = reader.PathToClusters(filePath);
                if (fileExtent == null && fileExtent.Length == 0)
                    throw new IOException($"Unexpected file extents for {filePath}");
                long offset = fileExtent[0].Offset;
                for (int i = 1; i < fileExtent.Length; i++)
                {
                    if (fileExtent[i] == null)
                        throw new IOException($"Unexpected file extents for {filePath}");
                    if (fileExtent[i].Offset < offset)
                        offset = fileExtent[i].Offset;
                }
                files[filePath] = offset;
            }
            foreach (DiscDirectoryInfo dirInfo in path.GetDirectories())
            {
                GetFileOffsets(reader, dirInfo, files);
            }
        }

        #endregion

        #region Helper Functions

        /// <summary>
        /// Converts a hex string into a byte array
        /// </summary>
        /// <param name="hex">Hex string</param>
        /// <returns>Converted byte array, or null if invalid hex string</returns>
        public static byte[] HexStringToByteArray(string hexString)
        {
            // Valid hex string must be an even number of characters
            if (string.IsNullOrEmpty(hexString) || hexString!.Length % 2 == 1)
                return null;

            // Convert ASCII to byte via lookup table
            int[] hexLookup = [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F];
            byte[] byteArray = new byte[hexString.Length / 2];
            for (int i = 0; i < hexString.Length; i += 2)
            {
                // Convert next two chars to ASCII value relative to '0'
                int a = Char.ToUpper(hexString[i]) - '0';
                int b = Char.ToUpper(hexString[i + 1]) - '0';

                // Ensure hex string only has '0' through '9' and 'A' through 'F' (case insensitive)
                if ((a < 0 || b < 0 || a > 22 || b > 22) || (a > 10 && a < 17) || (b > 10 && b < 17))
                    return null;
                byteArray[i / 2] = (byte)(hexLookup[a] << 4 | hexLookup[b]);
            }

            return byteArray;
        }

        /// <summary>
        /// Converts a byte array into a hex string
        /// </summary>
        /// <param name="byteArray">Byte array</param>
        /// <returns>Hex string representation of byte array, null if invalid</returns>
        public static string ByteArrayToHexString(byte[] byteArray)
        {
            // Validate byte array
            if (byteArray == null || byteArray.Length == 0)
                return null;

            // Store map of values to hex string
            string hex = "0123456789ABCDEF";

            // Add two characters per byte value
            StringBuilder hexString = new(2 * byteArray.Length);
            foreach (byte b in byteArray)
            {
                hexString.Append(hex[(int)(b >> 4)]);
                hexString.Append(hex[(int)(b & 0x0F)]);
            }

            // Return hex string
            return hexString.ToString();
        }

        #endregion
    }
}
