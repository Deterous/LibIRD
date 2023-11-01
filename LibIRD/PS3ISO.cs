using DiscUtils;
using DiscUtils.Iso9660;
using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace LibIRD
{
    /// <summary>
    /// PS3 ISO Data
    /// </summary>
    public class PS3ISO
    {
        #region Constants

        /// <summary>
        /// MD5 hash of null
        /// </summary>
        private protected static readonly byte[] NullMD5 = new byte[] { 0xd4, 0x1d, 0x8c, 0xd9, 0x8f, 0x00, 0xb2, 0x04, 0xe9, 0x80, 0x09, 0x98, 0xec, 0xf8, 0x42, 0x7e };

        #endregion

        #region Properties

        /// <summary>
        /// The same value stored in PARAM.SFO / TITLE_ID
        /// </summary>
        /// <remarks>9 bytes, ASCII, stored without dashes</remarks>
        public string TitleID { get; private protected set; }

        /// <summary>
        /// The same value stored in PARAM.SFO / TITLE
        /// </summary>
        /// <remarks>ASCII</remarks>
        public string Title { get; private protected set; }

        /// <summary>
        /// The same value stored in PARAM.SFO / PS3_SYSTEM_VER
        /// </summary>
        /// <remarks>4 bytes, ASCII, (e.g. "1.20", missing uses "0000"</remarks>
        public string SystemVersion { get; private protected set; }

        /// <summary>
        /// The same value stored in PARAM.SFO / VERSION
        /// </summary>
        /// <remarks>5 bytes, ASCII, e.g. "01.20"</remarks>
        public string GameVersion { get; private protected set; }

        /// <summary>
        /// The same value stored in PARAM.SFO / APP_VER
        /// </summary>
        /// <remarks>5 bytes, ASCII, e.g. "01.00"</remarks>
        public string AppVersion { get; private protected set; }

        /// <summary>
        /// Length of the gzip-compressed header data
        /// </summary>
        public uint HeaderLength { get; private protected set; }

        /// <summary>
        /// Gzip-compressed header data
        /// </summary>
        public byte[] Header { get; private protected set; }

        /// <summary>
        /// Length of the gzip-compressed footer data
        /// </summary>
        public uint FooterLength { get; private protected set; }

        /// <summary>
        /// Gzip-compressed footer data
        /// </summary>
        public byte[] Footer { get; private protected set; }

        /// <summary>
        /// Number of complete regions in the image
        /// </summary>
        public byte RegionCount { get; private protected set; }

        /// <summary>
        /// MD5 hashes for all complete regions in the image
        /// </summary>
        /// <remarks><see cref="RegionCount"/> regions, 16-bytes per hash</remarks>
        public byte[][] RegionHashes { get; private protected set; }

        /// <summary>
        /// Number of decrypted files in the image
        /// </summary>
        public uint FileCount { get; private protected set; }

        /// <summary>
        /// Starting sector for each decrypted file
        /// </summary>
        /// <remarks><see cref="FileCount"/> files, alternating with each <see cref="FileHashes"/> entry</remarks>
        public ulong[] FileKeys { get; private protected set; }

        /// <summary>
        /// MD5 hashes for all decrypted files in the image
        /// </summary>
        /// <remarks><see cref="FileHashes"/> files, 16-bytes per hash, alternating with each <see cref="FileHashes"/> entry</remarks>
        public byte[][] FileHashes { get; private protected set; }

        #endregion

        /// <summary>
        /// Process all files and subdirectories recursively
        /// </summary>
        /// <param name="reader"></param>
        /// <param name="path"></param>
        private void ParseDir(CDReader reader, string path)
        {
            DiscDirectoryInfo dir = reader.GetDirectoryInfo(path);

            // Process all files in current directory
            foreach (DiscFileInfo fileInfo in dir.GetFiles())
            {
                // Save directory full path
                // Save offset
                // Save start sector (offset)
                // Save total sectors (count)
            }

            // Process all subfolders
            foreach (DiscDirectoryInfo dirInfo in dir.GetDirectories())
            {
                // Save directory full path
                // Save offset
                // Save start sector (offset)
                // Save total sectors (count)
                ParseDir(reader, dirInfo.FullName);
            }
        }

        /// <summary>
        /// Determine PUP file offset via extent
        /// </summary>
        /// <param name="fs"></param>
        /// <param name="reader"></param>
        private void GetSystemVersion(FileStream fs, CDReader reader)
        {
            
            //DiscUtils.Streams.StreamExtent[] a = reader.PathToExtents("\\PS3_UPDATE\\PS3UPDAT.PUP");
            //long pupOffset = a[0].Start;

            // Determine PUP file offset via cluster
            DiscUtils.Streams.Range<long, long>[] updateClusters = reader.PathToClusters("\\PS3_UPDATE\\PS3UPDAT.PUP");
            long pupOffset = updateClusters[0] != null ? 2048 * updateClusters[0].Offset : 0;

            // Check PUP file Magic
            fs.Seek(pupOffset, SeekOrigin.Begin);
            byte[] pupMagic = new byte[5];
            fs.Read(pupMagic, 0, pupMagic.Length);
            if (Encoding.ASCII.GetString(pupMagic) != "SCEUF")
            {
                // If magic is incorrect, set version to "0000" (unknown)
                SystemVersion = "0000";
            }
            else
            {
                // Determine location of version string
                fs.Seek(pupOffset + 0x3E, SeekOrigin.Begin);
                byte[] offset = new byte[2];
                fs.Read(offset, 0, 2);
                // Move stream to PUP version string
                Array.Reverse(offset);
                ushort versionOffset = BitConverter.ToUInt16(offset, 0);
                fs.Seek(pupOffset + versionOffset, SeekOrigin.Begin);
                // Read version string
                byte[] version = new byte[4];
                fs.Read(version, 0, version.Length);
                // Set version string
                SystemVersion = Encoding.ASCII.GetString(version);
            }
        }

        /// <summary>
        /// Constructor for generating values from an ISO file
        /// </summary>
        /// <param name="isoPath">Path to the ISO</param>
        /// <exception cref="ArgumentNullException"></exception>
        internal PS3ISO(string isoPath)
        {
            // Validate ISO path
            if (isoPath == null || isoPath.Length <= 0)
                throw new ArgumentNullException(nameof(isoPath));

            // Check file exists
            var iso = new FileInfo(isoPath);
            if (!iso.Exists)
                throw new FileNotFoundException(isoPath);

            // TODO: remove these initial values once they're set properly
            RegionCount = 3;
            RegionHashes = new byte[RegionCount][];
            for (int i = 0; i < RegionCount; i++)
                RegionHashes[i] = NullMD5;
            FileCount = 13;
            FileKeys = new ulong[FileCount];
            for (int i = 0; i < FileCount; i++)
                FileKeys[i] = (ulong)(i * 16);
            FileHashes = new byte[FileCount][];
            for (int i = 0; i < FileCount; i++)
                FileHashes[i] = NullMD5;

            // Process ISO file
            using FileStream fs = new FileStream(isoPath, FileMode.Open, FileAccess.Read) ?? throw new FileNotFoundException(isoPath);
            // Validate ISO file
            if (!CDReader.Detect(fs))
                throw new InvalidDataException("Not a valid ISO file");

            // New ISO Reader from DiscUtils
            CDReader reader = new(fs, true, true);

            // Read PS3 Metadata from PARAM.SFO
            using (DiscUtils.Streams.SparseStream s = reader.OpenFile("PS3_GAME\\PARAM.SFO", FileMode.Open, FileAccess.Read))
            {
                // Parse PARAM.SFO file
                ParamSFO paramSFO = new(s);

                // Store required values for IRD
                TitleID = paramSFO["TITLE_ID"];
                Title = paramSFO["TITLE"];
                GameVersion = paramSFO["VERSION"];
                AppVersion = paramSFO["APP_VER"];
            }

            // Determine system update version
            GetSystemVersion(fs, reader);


            // Determine the extent of the Header (Sector 0 to first data sector)
            //DiscUtils.Streams.Range<long, long> clusters = reader.PathToClusters("\\").First();
            //long firstSector = clusters.Offset;
            //long totalSectors = (long) Math.Ceiling(clusters.Count / 2048.0);

            // Compress the header and store
            DiscUtils.Streams.Range<long, long>[] sfbClusters = reader.PathToClusters("\\PS3_DISC.SFB");
            long firstSector = sfbClusters[0] != null ? sfbClusters[0].Offset : 0;
            using (MemoryStream headerStream = new())
            {
                using (GZipStream gzs = new(headerStream, CompressionLevel.SmallestSize))
                {
                    fs.Seek(0, SeekOrigin.Begin);
                    byte[] buf = new byte[2048];
                    // Read all sectors before the first data sector
                    for (int i = 0; i < firstSector; i++)
                    {
                        int numBytes = fs.Read(buf, 0, buf.Length);
                        gzs.Write(buf, 0, numBytes);
                    }
                }
                Header = headerStream.ToArray();
                HeaderLength = (uint)Header.Length;
            }

            // Compress the footer and store
            DiscUtils.Streams.StreamExtent[] updateBytes = reader.PathToExtents("\\PS3_UPDATE\\PS3UPDAT.PUP");
            long lastByte = updateBytes[^1].Start + updateBytes[^1].Length;
            using (MemoryStream footerStream = new())
            {
                using (GZipStream gzs = new(footerStream, CompressionLevel.SmallestSize))
                {
                    // Start saving data from after last file
                    fs.Seek(lastByte, SeekOrigin.Begin);
                    byte[] buf = new byte[2048];
                    int numBytes = 2048;
                    // Keep reading data until there is none left to read
                    while (numBytes != 0)
                    {
                        numBytes = fs.Read(buf, 0, buf.Length);
                        gzs.Write(buf, 0, numBytes);
                    }
                }
                Footer = footerStream.ToArray();
                FooterLength = (uint)Footer.Length;
            }



            // Get info on root directory
            //DiscDirectoryInfo rootDir = reader.GetDirectoryInfo("\\");
            // Recursively process all subdirectories
            ParseDir(reader, "\\");

            // Calculate last sector
            //DiscDirectoryInfo lastFile = reader.
            //lastSector = lastFile.StartSector + lastFile.TotalSectors;

        }
    }
}
