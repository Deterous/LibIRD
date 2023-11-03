using DiscUtils;
using DiscUtils.Iso9660;
using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
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
        /// Blu-ray ISO sector size in bytes
        /// </summary>
        /// <remarks>2048</remarks>
        private protected static readonly uint SectorSize = 2048;

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

        private long[] FileStart { get; set; }
        private long[] FileEnd { get; set; }

        #endregion

        #region Constructors

        /// <summary>
        /// Default generator for reading IRD files rather than ISOs
        /// </summary>
        private protected PS3ISO(string titleID,
                                 string title,
                                 string sysVersion,
                                 string gameVersion,
                                 string appVersion,
                                 byte[] header,
                                 byte[] footer,
                                 byte[][] regionHashes,
                                 ulong[] fileKeys,
                                 byte[][] fileHashes)
        {
            TitleID = titleID;
            Title = title;
            SystemVersion = sysVersion;
            GameVersion = gameVersion;
            AppVersion = appVersion;
            HeaderLength = (uint)header.Length;
            Header = header;
            FooterLength = (uint)footer.Length;
            Footer = footer;
            RegionHashes = regionHashes;
            FileKeys = fileKeys;
            FileHashes = fileHashes;
        }

        /// <summary>
        /// Constructor for generating values from an ISO file
        /// </summary>
        /// <param name="isoPath">Path to the ISO</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="FileNotFoundException"></exception>
        /// <exception cref="InvalidFileSystemException"></exception>
        internal PS3ISO(string isoPath)
        {
            // Validate ISO path
            if (isoPath == null || isoPath.Length <= 0)
                throw new ArgumentNullException(nameof(isoPath));

            // Check file exists
            var iso = new FileInfo(isoPath);
            if (!iso.Exists)
                throw new FileNotFoundException(nameof(isoPath));

            // TODO: remove these initial values once they're set properly

            // Parse ISO file as a file stream
            using FileStream fs = new FileStream(isoPath, FileMode.Open, FileAccess.Read) ?? throw new FileNotFoundException(isoPath);
            // Validate ISO file stream
            if (!CDReader.Detect(fs))
                throw new InvalidFileSystemException("Not a valid ISO file");

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

            // Read and compress the ISO header
            GetHeader(fs, reader);

            // Read and compress the ISO footer
            GetFooter(fs);

            // Recursively count all files in ISO to allocate arrays
            DiscDirectoryInfo rootDir = reader.GetDirectoryInfo("\\");
            FileCount = 0;
            CountFiles(rootDir);
            FileStart = new long[FileCount];
            FileEnd = new long[FileCount];
            FileKeys = new ulong[FileCount];
            FileHashes = new byte[FileCount][];

            // Determine file offsets and hashes
            uint fileCount = FileCount;
            FileCount = 0;
            GetFileExtents(rootDir, reader);
            if (FileCount != fileCount)
                throw new InvalidFileSystemException("Unexpected ISO filesystem error: ");

            // Get MD5 hash for all regions and files on ISO
            HashData(fs);
            for (int i = 0; i < FileCount; i++)
            {
                FileKeys[i] = (ulong)FileStart[i];
                FileHashes[i] = NullMD5;
            }
        }

        #endregion

        #region Methods

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
        private void HashData(FileStream fs)
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

            // Determine the extent for each region
            byte[] regionSector = new byte[4];
            long[] regionStart = new long[RegionCount];
            long[] regionEnd = new long[RegionCount];
            fs.Seek(8, SeekOrigin.Begin);
            fs.Read(regionSector, 0, 4);
            Array.Reverse(regionSector, 0, 4);
            for (int i = 0; i < RegionCount; i++)
            {
                // End sector of previous region is start of this region
                if (i % 2 == 1)
                    regionStart[i] = BitConverter.ToInt32(regionSector) + 1;
                else
                    regionStart[i] = BitConverter.ToInt32(regionSector);
                // Determine end sector offset of this region
                fs.Read(regionSector, 0, 4);
                Array.Reverse(regionSector, 0, 4);
                if (i % 2 == 1)
                    regionEnd[i] = BitConverter.ToInt32(regionSector) - 1;
                else
                    regionEnd[i] = BitConverter.ToInt32(regionSector);
            }

            // Remove header from first region
            regionStart[0] = FirstDataSector;
            // Remove footer from last region
            regionEnd[^1] = (UpdateEnd / SectorSize) - 1;

            // Determine MD5 hashes for each region
            using MD5 md5 = MD5.Create();
            byte[] buf = new byte[SectorSize];
            for (int i = 0; i < RegionCount; i++)
            {
                // Start reading data from first sector of region
                fs.Seek(SectorSize * regionStart[i], SeekOrigin.Begin);

                // Compute MD5 hash for just the region portion of the ISO file
                int numBytes;
                for (long j = regionStart[i]; j <= regionEnd[i]; j++)
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
        /// Recursively determines file count
        /// </summary>
        /// <param name="dir"></param>
        private void CountFiles(DiscDirectoryInfo dir)
        {
            FileCount += (uint)dir.GetFiles().Length;
            foreach (DiscDirectoryInfo dirInfo in dir.GetDirectories())
                CountFiles(dirInfo);
        }

        /// <summary>
        /// Determine byte extents for all files and files within subdirectories recursively
        /// </summary>
        /// <param name="reader"></param>
        /// <param name="dir"></param>
        /// <exception cref="InvalidFileSystemException"></exception>
        private void GetFileExtents(DiscDirectoryInfo dir, CDReader reader)
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
                FileStart[FileCount] = fileExtents[0].Start;
                FileEnd[FileCount] = fileExtents[0].Length;
                FileCount++;
            }

            // Recursively process all subfolders of current directory
            foreach (DiscDirectoryInfo dirInfo in dir.GetDirectories())
            {
                GetFileExtents(dirInfo, reader);
            }
        }

        #endregion

    }
}
