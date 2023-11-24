using DiscUtils;
using DiscUtils.Iso9660;
using System;
using System.IO;
using System.IO.Compression;
using System.IO.Hashing;
using System.Security.Cryptography;
using System.Text;

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
        // TODO: Link Data1Key and Disc Key

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
        /// <remarks><see cref="FileHashes"/> files, 16-bytes per hash, alternating with each <see cref="FileHashes"/> entry</remarks>
        public byte[][] FileHashes { get; private set; }

        /// <summary>
        /// First byte of the PS3_DISC.SFB file
        /// </summary>
        /// <remarks>Last byte to read when reading the header</remarks>
        private long FirstDataSector { get; set; }

        /// <summary>
        /// First byte of the PS3UPDAT.PUP file
        /// </summary>
        private long UpdateOffset { get; set; }

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
        /// </summary>
        private protected IRD()
        {
            // Assumes that internally derived class will set fields in its own constructor
        }

        /// <summary>
        /// Constructor with given required fields
        /// </summary>
        /// <param name="isoPath">Path to the ISO</param>
        /// <param name="discKey">Disc Key, byte array of length 16</param>
        /// <param name="discID">Disc ID, byte array of length 16</param>
        /// <param name="discPIC">Disc PIC, byte array of length 115</param>
        /// <param name="redump">True if redump-style IRD</param>
        public IRD(string isoPath, byte[] discKey, byte[] discID, byte[] discPIC, bool redump = false)
        {
            // Parse ISO, Disc Key, Disc ID, and PIC
            DiscKey = discKey;
            GenerateD1(discKey);
            GenerateD2(discID);
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
            if (key == null)
                throw new ArgumentNullException(nameof(key));
            if (key.Length != 16)
                throw new ArgumentException("Disc Key must be a byte array of length 16", nameof(key));

            // Setup AES decryption
            using Aes aes = Aes.Create() ?? throw new InvalidOperationException("AES not available. Change your system settings");

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
            if (d1 == null)
                throw new ArgumentNullException(nameof(d1));
            if (d1.Length != 16)
                throw new ArgumentException("Disc Key must be a byte array of length 16", nameof(d1));

            // Setup AES decryption
            using Aes aes = Aes.Create() ?? throw new InvalidOperationException("AES not available. Change your system settings");

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
            if (d2 == null) throw new ArgumentNullException(nameof(d2));
            if (d2.Length != 16) throw new ArgumentException("Disc ID must be a byte array of length 16", nameof(d2));

            // Setup AES encryption
            using Aes aes = Aes.Create() ?? throw new InvalidOperationException("AES not available. Change your system settings");

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
            if (d2 == null)
                throw new ArgumentNullException(nameof(d2));
            if (d2.Length != 16)
                throw new ArgumentException("Disc ID must be a byte array of length 16", nameof(d2));

            // Setup AES encryption
            using Aes aes = Aes.Create() ?? throw new InvalidOperationException("AES not available. Change your system settings");

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
            if (getKeyLog == null)
                throw new ArgumentNullException(nameof(getKeyLog));
            if (!File.Exists(getKeyLog))
                throw new FileNotFoundException(nameof(getKeyLog));

            // Read from .getkey.log file
            using StreamReader sr = File.OpenText(getKeyLog);

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
            string discKeyStr = line["disc_key = ".Length..];
            // Validate Disc Key from log
            if (discKeyStr.Length != 32)
                throw new InvalidDataException("Unexpected Disc Key in .getkey.log");
            // Convert Disc Key to byte array
            discKey = Convert.FromHexString(discKeyStr);

            // Read Disc ID
            byte[] discID;
            while ((line = sr.ReadLine()) != null && line.Trim().StartsWith("disc_id = ") == false) ;
            if (line == null)
                throw new InvalidDataException("Could not find Disc ID in .getkey.log");
            // Get Disc ID from log
            string discIDStr = line["disc_id = ".Length..];
            // Validate Disc ID from log
            if (discIDStr.Length != 32)
                throw new InvalidDataException("Unexpected Disc ID in .getkey.log");
            // Replace X's in Disc ID with 00000001
            discIDStr = discIDStr[..24] + "00000001";
            // Convert Disc ID to byte array
            discID = Convert.FromHexString(discIDStr);

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
            discPIC = Convert.FromHexString(discPICStr[..230]);

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
        /// <exception cref="InvalidFileSystemException"></exception>
        private protected void GenerateIRD(string isoPath, bool redump = false)
        {
            // Parse ISO file as a file stream
            using FileStream fs = new FileStream(isoPath, FileMode.Open, FileAccess.Read) ?? throw new FileNotFoundException(isoPath);
            // Validate ISO file stream
            if (!CDReader.Detect(fs))
                throw new InvalidFileSystemException("Not a valid ISO file");

            // New ISO Reader from DiscUtils
            CDReader reader = new(fs, true, true);

            // If generating redump-style IRD
            if (redump)
            {
                // Redump-style IRDs set the lowest bit of ExtraConfig to 1
                ExtraConfig |= 0x01;

                // Redump-style IRDs use fields in PS3_DISC.SFB
                using DiscUtils.Streams.SparseStream s = reader.OpenFile("PS3_DISC.SFB", FileMode.Open, FileAccess.Read);

                // Parse PS3_DISC.SFB file
                PS3_DiscSFB ps3_DiscSFB = new(s);
                
                // If a valid TITLE_ID field is present, remove the hyphen to fit into standard IRD file
                if (ps3_DiscSFB.Field.ContainsKey("TITLE_ID") && ps3_DiscSFB.Field["TITLE_ID"].Length == 10 && ps3_DiscSFB.Field["TITLE_ID"][4] == '-')
                    TitleID = string.Concat(ps3_DiscSFB.Field["TITLE_ID"].AsSpan(0, 4), ps3_DiscSFB.Field["TITLE_ID"].AsSpan(5, 5));

                // If the version field is present, this is a multi-game disc
                // Redump-style IRDs use the VERSION field from PS3_DISC.SFB instead of VERSION from PARAM.SFO
                if (ps3_DiscSFB.Field.ContainsKey("VERSION"))
                    DiscVersion = ps3_DiscSFB.Field["VERSION"];
            }

            // Read PS3 Metadata from PARAM.SFO
            using (DiscUtils.Streams.SparseStream s = reader.OpenFile("PS3_GAME\\PARAM.SFO", FileMode.Open, FileAccess.Read))
            {
                // Parse PARAM.SFO file
                ParamSFO paramSFO = new(s);
                // If PS3_DISC.SFB did not set TitleID, use PARAM.SFO TITLE_ID
                TitleID ??= paramSFO["TITLE_ID"];
                Title = paramSFO["TITLE"];
                // If PS3_DISC.SFB did not set DiscVersion, use PARAM.SFO VERSION
                DiscVersion ??= paramSFO["VERSION"];
                AppVersion = paramSFO["APP_VER"];
            }

            // Determine system update version
            GetSystemVersion(fs, reader);

            // Read and compress the ISO header
            GetHeader(fs, reader);

            // Read and compress the ISO footer
            GetFooter(fs);

            // Process all regions on ISO
            HashRegions(fs);

            // TODO: Speed up program by hashing regions and files at the same time (read from filesystem only once)

            // Recursively count all files in ISO to allocate file arrays
            DiscDirectoryInfo rootDir = reader.GetDirectoryInfo("\\");
            FileCount = 0;
            CountFiles(rootDir);
            FileKeys = new long[FileCount];
            FileHashes = new byte[FileCount][];

            // Determine file offsets and hashes
            uint fileCount = FileCount;
            FileCount = 0;
            HashFiles(fs, reader, rootDir);
            if (FileCount != fileCount)
                throw new InvalidFileSystemException("Unexpected ISO filesystem error: ");
            Array.Sort(FileKeys, FileHashes);
        }

        #endregion

        #region Reading ISO

        /// <summary>
        /// Retreives and stores the system version
        /// </summary>
        /// <remarks>PS3UPDAT.PUP update file version number</remarks>
        /// <param name="fs">ISO filestream</param>
        /// <param name="reader"></param>
        /// <exception cref="InvalidFileSystemException"></exception>
        private void GetSystemVersion(FileStream fs, CDReader reader)
        {
            // Determine PUP file offset via cluster
            DiscUtils.Streams.Range<long, long>[] updateClusters = reader.PathToClusters("\\PS3_UPDATE\\PS3UPDAT.PUP");
            if (updateClusters == null || updateClusters.Length <= 0)
            {
                // File too small for dedicated cluster, try get the offset from the file extents instead
                DiscUtils.Streams.StreamExtent[] updateExtents = reader.PathToExtents("\\PS3_UPDATE\\PS3UPDAT.PUP");
                if (updateExtents == null || updateExtents.Length <= 0)
                    throw new InvalidFileSystemException("Unexpected PS3UPDAT.PUP file extent in ISO filestream");
                // PS3UPDAT.PUP file begins at start of first extent
                UpdateOffset = updateExtents[0].Start;
                // Update file ends at the last extent plus its length
                UpdateEnd = updateExtents[^1].Start + updateExtents[^1].Length;
            }
            else
            {
                // PS3UPDAT.PUP file begins at first byte of dedicated cluster
                UpdateOffset = updateClusters[0] != null ? SectorSize * updateClusters[0].Offset : 0;
                // Update file ends at the last byte of the last cluster
                UpdateEnd = SectorSize * (updateClusters[^1].Offset + updateClusters[^1].Count);
            }

            // Check PUP file Magic
            fs.Seek(UpdateOffset, SeekOrigin.Begin);
            byte[] pupMagic = new byte[5];
            fs.Read(pupMagic, 0, pupMagic.Length);
            // If magic is incorrect, set version to "0000" (unknown)
            if (Encoding.ASCII.GetString(pupMagic) != "SCEUF")
                SystemVersion = "0000";
            else
            {
                // Determine location of version string
                fs.Seek(UpdateOffset + 0x3E, SeekOrigin.Begin);
                byte[] offset = new byte[2];
                fs.Read(offset, 0, 2);
                // Move stream to PUP version string
                Array.Reverse(offset);
                ushort versionOffset = BitConverter.ToUInt16(offset, 0);
                fs.Seek(UpdateOffset + versionOffset, SeekOrigin.Begin);
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
        /// <param name="reader"></param>
        /// <exception cref="InvalidFileSystemException"></exception>
        private void GetHeader(FileStream fs, CDReader reader)
        {
            // Determine the extent of the header via cluster (Sector 0 to first data sector)
            DiscUtils.Streams.Range<long, long>[] sfbClusters = reader.PathToClusters("\\PS3_DISC.SFB");
            if (sfbClusters == null || sfbClusters.Length <= 0)
            {
                // File too small for dedicated cluster, try get the first sector from the file extents instead
                DiscUtils.Streams.StreamExtent[] sfbExtents = reader.PathToExtents("\\PS3_DISC.SFB");
                if (sfbExtents == null || sfbExtents.Length <= 0)
                    throw new InvalidFileSystemException("Unexpected PS3UPDAT.PUP file extent in ISO filestream");
                FirstDataSector = sfbExtents[0].Start;
            }
            else
            {
                // End of header is at beginning of first byte of dedicated cluster
                FirstDataSector = sfbClusters[0] != null ? sfbClusters[0].Offset : 0;
            }

            // Begin a GZip stream to write header to
            using MemoryStream headerStream = new();
            using (GZipStream gzs = new(headerStream, CompressionLevel.SmallestSize))
            {
                // Start reading data from the beginning of the ISO file
                fs.Seek(0, SeekOrigin.Begin);
                byte[] buf = new byte[SectorSize];
                int numBytes;

                // Read all data before the first data sector
                for (int i = 0; i < FirstDataSector; i++)
                {
                    numBytes = fs.Read(buf, 0, buf.Length);
                    gzs.Write(buf, 0, numBytes);
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
            using (GZipStream gzs = new(footerStream, CompressionLevel.SmallestSize))
            {
                // Start reading data from after last file
                fs.Seek(UpdateEnd, SeekOrigin.Begin);
                byte[] buf = new byte[SectorSize];
                int numBytes = (int)SectorSize;

                // Keep reading data until there is none left to read
                while (numBytes != 0)
                {
                    numBytes = fs.Read(buf, 0, buf.Length);
                    gzs.Write(buf, 0, numBytes);
                }
            }

            // Save stream to field
            Footer = footerStream.ToArray();
            FooterLength = (uint)Footer.Length;
        }

        /// <summary>
        /// Determines and stores the hashes for each disc region
        /// </summary>
        /// <param name="fs"></param>
        /// <exception cref="InvalidFileSystemException"></exception>
        private void HashRegions(FileStream fs)
        {
            // Determine the number of unencryted regions
            fs.Seek(0, SeekOrigin.Begin);
            byte[] decRegionCount = new byte[4];
            fs.Read(decRegionCount, 0, 4);

            // Total number of regions is 2x number of unencrypted regions, minus 1
            RegionCount = (byte)(2 * ((uint)decRegionCount[3]) - 1);
            if (RegionCount <= 0)
                throw new InvalidFileSystemException("No regions detected in ISO");
            RegionHashes = new byte[RegionCount][];
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
                    RegionStart[i] = BitConverter.ToInt32(regionSector) + 1;
                else
                    RegionStart[i] = BitConverter.ToInt32(regionSector);
                // Determine end sector offset of this region
                fs.Read(regionSector, 0, 4);
                Array.Reverse(regionSector, 0, 4);
                if (i % 2 == 1)
                    RegionEnd[i] = BitConverter.ToInt32(regionSector) - 1;
                else
                    RegionEnd[i] = BitConverter.ToInt32(regionSector);
            }

            // Remove header from first region
            RegionStart[0] = FirstDataSector;
            // Remove footer from last region
            RegionEnd[^1] = (UpdateEnd / SectorSize) - 1;

            // Determine MD5 hashes for each region
            using MD5 md5 = MD5.Create();
            byte[] buf = new byte[SectorSize];
            for (int i = 0; i < RegionCount; i++)
            {
                // Start reading data from first sector of region
                fs.Seek(SectorSize * RegionStart[i], SeekOrigin.Begin);

                // Compute MD5 hash for just the region portion of the ISO file
                int numBytes;
                for (long j = RegionStart[i]; j <= RegionEnd[i]; j++)
                {
                    // Read one sector at a time
                    numBytes = fs.Read(buf, 0, buf.Length);
                    // Check that an entire sector was read
                    if (numBytes < buf.Length)
                        throw new InvalidFileSystemException("Disc region ended unexpectedly");
                    // Process MD5 sum one sector at a time
                    md5.TransformBlock(buf, 0, buf.Length, null, 0);
                }

                // Compute and store MD5 hash of region
                md5.TransformFinalBlock(buf, 0, 0);
                RegionHashes[i] = md5.Hash;
            }
        }

        /// <summary>
        /// Determine and store hashes for all files and files within subdirectories recursively
        /// </summary>
        /// <param name="reader"></param>
        /// <param name="path"></param>
        private void HashFiles(FileStream fs, CDReader reader, DiscDirectoryInfo dir)
        {
            // Process all files in current directory
            foreach (DiscFileInfo fileInfo in dir.GetFiles())
            {
                string filePath = fileInfo.FullName;

                // Try get the first sector from the file extents instead
                DiscUtils.Streams.StreamExtent[] fileExtents = reader.PathToExtents(filePath);
                if (fileExtents == null || fileExtents.Length <= 0)
                    throw new InvalidFileSystemException("Unexpected file extent in ISO filestream for " + filePath);
                if (fileExtents.Length > 1)
                    throw new InvalidFileSystemException("Non-contiguous file detected");
                long firstByte = fileExtents[0].Start;
                int firstSector = (int)(firstByte / 2048);
                long fileLength = fileExtents[0].Length;
                FileKeys[FileCount] = firstSector;

                // Determine whether file is in encrypted or decrypted region
                bool encrypted = false;
                for (int i = RegionCount - 1; i > 0; i--)
                {
                    if (RegionStart[i] <= firstSector)
                    {
                        encrypted = i % 2 == 1;
                        break;
                    }
                }

                // Start reading data from the beginning of the ISO file
                fs.Seek(firstByte, SeekOrigin.Begin);
                byte[] buf = new byte[SectorSize];
                int numBytes;
                // Read all data before the first data sector
                MD5 md5 = MD5.Create();
                for (int i = 0; i < (fileLength / SectorSize); i++)
                {
                    numBytes = fs.Read(buf, 0, buf.Length);
                    // Check that an entire sector was read
                    if (numBytes < buf.Length)
                        throw new InvalidFileSystemException("Disc region ended unexpectedly");
                    // Decrypt sector if necessary
                    if (encrypted)
                        buf = DecryptSector(buf, firstSector + i);
                    // Hash sector
                    md5.TransformBlock(buf, 0, numBytes, null, 0);
                }
                // Read remaining partial sector
                if (fileLength % SectorSize != 0)
                {
                    numBytes = fs.Read(buf, 0, buf.Length);
                    // Check that an entire sector was read
                    if (numBytes < buf.Length)
                        throw new InvalidFileSystemException("Disc region ended unexpectedly");
                    // Decrypt partial sector if necessary
                    if (encrypted)
                        buf = DecryptSector(buf, firstSector + (int)(fileLength / SectorSize));
                    // Hash partial sector
                    md5.TransformBlock(buf, 0, (int)(fileLength % SectorSize), null, 0);
                }

                // Finalise and store MD5 hash
                md5.TransformFinalBlock(buf, 0, 0);
                FileHashes[FileCount] = md5.Hash;
                FileCount++;
            }

            // Recursively process all subfolders of current directory
            foreach (DiscDirectoryInfo dirInfo in dir.GetDirectories())
            {
                HashFiles(fs, reader, dirInfo);
            }
        }

        /// <summary>
        /// Decrypts a given sector byte array
        /// </summary>
        /// <param name="sector">Byte array to be decrypted</param>
        /// <exception cref="InvalidOperationException"></exception>
        private protected byte[] DecryptSector(byte[] sector, int sectorNumber)
        {
            // Setup AES decryption
            using Aes aes = Aes.Create() ?? throw new InvalidOperationException("AES not available. Change your system settings");

            // Set AES settings
            aes.Key = DiscKey;
            aes.Padding = PaddingMode.None;
            aes.Mode = CipherMode.CBC;

            // Determine Initial Value based on sector number
            byte[] iv = new byte[16];
            for (int i = 0; i < 16; i++)
            {
                byte a = (byte)(sectorNumber & 0xFF);
                iv[16 - i - 1] = (byte)(sectorNumber & 0xFF);
                sectorNumber >>= 8;
            }
            aes.IV = iv;

            // Perform AES decryption
            using MemoryStream stream = new();
            using ICryptoTransform dec = aes.CreateDecryptor();
            using CryptoStream cs = new(stream, dec, CryptoStreamMode.Write);
            cs.Write(sector, 0, sector.Length);
            cs.FlushFinalBlock();

            return stream.ToArray();
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

            // Write IRD data to stream in order
            using (BinaryWriter bw = new(stream, Encoding.UTF8, true))
            {
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
                    bw.Write(RegionHashes[i], 0, 16);

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
            }

            // Calculate the little-endian 32-bit "IEEE 802.3" CRC value of the IRD contents so far
            stream.Position = 0;
            Crc32 crc32 = new();
            crc32.Append(stream);
            byte[] crc = crc32.GetCurrentHash();

            // Write final CRC value to the stream
            stream.Write(crc, 0, 4);

            // Create the IRD file stream
            using FileStream fs = new(irdPath, FileMode.Create, FileAccess.Write);
            // Create a GZipped IRD file stream
            using GZipStream gzStream = new(fs, CompressionLevel.SmallestSize);
            // Write entire gzipped IRD stream to file
            stream.Position = 0;
            stream.CopyTo(gzStream);
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
            if (!File.Exists(irdPath))
                throw new FileNotFoundException(nameof(irdPath));

            // Open IRD for reading
            using FileStream fs = new(irdPath, FileMode.Open, FileAccess.Read);
            using GZipStream gzs = new(fs, CompressionMode.Decompress);
            using BinaryReader br = new(gzs);

            // Check for IRD file signature
            byte[] magic = br.ReadBytes(4);
            if (!((ReadOnlySpan<byte>)Magic).SequenceEqual(magic))
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
            for (int i = 0; i  < fileCount; i++)
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
                uid = br.ReadUInt16();

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
        public void Print()
        {
            // Build string from parameters
            StringBuilder print = new();
            print.AppendLine("IRD Contents:");
            print.AppendLine("=============");

            // Append IRD fields to string builder
            print.AppendLine($"Magic:        {Encoding.ASCII.GetString(Magic)}");
            print.AppendLine($"IRD Version:  {Version}");
            print.AppendLine($"Title ID:     {TitleID}");
            print.AppendLine($"Title:        {Title}");
            print.AppendLine($"PUP Version:  {SystemVersion}");
            print.AppendLine($"Disc Version: {DiscVersion}");
            print.AppendLine($"App Version:  {AppVersion}");
            print.AppendLine($"Regions:      {RegionCount}");
            print.AppendLine($"Files:        {FileCount}");
            if (ExtraConfig != 0x0000)
                print.AppendLine($"Extra Config: {ExtraConfig:X4}");
            if (Attachments != 0x0000)
                print.AppendLine($"Attachments:  {Attachments:X4}");
            print.AppendLine($"Unique ID:    {UID:X8}");
            print.AppendLine($"Data 1 Key:   {Convert.ToHexString(Data1Key)}");
            print.AppendLine($"Data 2 Key:   {Convert.ToHexString(Data2Key)}");
            print.AppendLine($"PIC:          {Convert.ToHexString(PIC)}");
            print.AppendLine();

            // Ensure UTF-8 will display properly
            Console.OutputEncoding = Encoding.UTF8;

            // Print formatted string
            Console.Write(print);
        }

        #endregion
    }
}
