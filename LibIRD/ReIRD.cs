using System;
using System.IO;
using System.IO.Hashing;

namespace LibIRD
{
    #region Enumerations

    /// <summary>
    /// Type used to define all known PS3 disc regions
    /// </summary>
    public enum Region
    {
        // Null region
        NONE = 0x00,

        // Japan and Asia
        Asia = 0x01, // BLAS/BCAS serials
        Japan = Asia, // BLJM/BCJS serials
        Korea = Asia, // BLKS/BCKS serials

        // USA and Canada (NA)
        NorthAmerica = 0x03,
        USA = NorthAmerica,
        Canada = NorthAmerica,

        // Europe, Middle East, and Africa (EMEA)
        Europe = 0x04,
        MiddleEast = Europe,
        Africa = Europe,

        // Australia and New Zealand (Oceania)
        Australia = 0x06, // Some releases use Region.Europe instead
        NewZealand = Australia, // Some releases use Region.Europe instead

        // Brazil and Latin America (LATAM)
        LatinAmerica = 0x09, // e.g. Portuguese + Spanish release
        Brazil = LatinAmerica,
        // Mexico is often Region.NorthAmerica

        // Russia and Eastern Europe
        EasternEurope = 0x0A, // e.g. Russian + Polish language release
        Russia = EasternEurope,
        // Poland is Region.Europe
    }

    #endregion

    /// <summary>
    /// Create Reproducible Redump-style IRD file from an ISO, CRC32 and Disc Key
    /// </summary>
    public class ReIRD : IRD
    {

        #region Constants

        /// <summary>
        /// Size of a blu-ray layer in bytes (BD-25 max size)
        /// </summary>
        /// <remarks>Can be used as the default PS3 layerbreak value</remarks>
        private const long BDLayerSize = 25025314816; // 12219392 sectors

        #endregion

        #region Property Generation

        /// <summary>
        /// Generates a Disc ID given a size and region, where region is a single byte
        /// </summary>
        /// <returns>Valid Disc ID, byte array length 16</returns>
        private static byte[] GenerateID(long size, Region region = Region.NONE)
        {
            if (size > BDLayerSize) // if BD-50, Disc ID is fixed
                return new byte[]{ 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF,
                                   0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00};
            else // else if BD-25, Disc ID has a byte referring to disc region
            {
                return new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF,
                                    0x00, 0x02, 0x00, (byte)region, 0x00, 0x00, 0x00, 0x01 };
            }
        }

        /// <summary>
        /// Generates the PIC data for a given ISO size in bytes
        /// </summary>
        /// <param name="size">Total ISO size in number of bytes</param>
        /// <param name="layerbreak">Layer break value, byte at which disc layers are split across</param>
        /// <param name="exactIRD">True to generate a PIC in 3k3y style (0x03 at 115th byte for BD-50 discs)</param>
        /// <exception cref="ArgumentException"></exception>
        private void GeneratePIC(long size, long layerbreak = BDLayerSize, bool exactIRD = false)
        {
            // Validate size
            if (size == 0 || (size % 2048) != 0)
                throw new ArgumentException("ISO Size in bytes must be a positive integer multiple of 2048", nameof(size));
            // Validate layerbreak
            if (layerbreak == 0 || (layerbreak != BDLayerSize && layerbreak >= size))
                throw new ArgumentException("Layerbreak in bytes must be a positive integer less than the ISO Size", nameof(size));

            // TODO: Generate correct PICs for Hybrid PS3 discs (BD-50 with layerbreak value other than 12219392)
            if (size > BDLayerSize) // if BD-50
            {
                // num_sectors + layer_sector_end (0x00100000) + sectors_between_layers (0x01358C00 - 0x00CA73FE) - 3
                byte[] total_sectors = BitConverter.GetBytes((uint)(size / 2048 + 8067071));

                // Initial portion of PIC (24 bytes)
                PIC = new byte[]{
                // [4098 bytes] [2x 0x00] ["DI"]   [v1] [10units] [DI num]
                0x10, 0x02, 0x00, 0x00, 0x44, 0x49, 0x01, 0x10, 0x00, 0x00, 0x20, 0x00,
                //   ["BDR"]         [2 layers]
                0x42, 0x44, 0x4F, 0x01, 0x21, 0x01, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00,
                // Total sectors used on disc (4 bytes)
                total_sectors[3], total_sectors[2], total_sectors[1], total_sectors[0],
                // 1st Layer sector start location (4 bytes)
                0x00, 0x10, 0x00, 0x00,
                // 1st Layer sector end location (4 bytes)
                0x00, 0xCA, 0x73, 0xFE,
                // 32 bytes of zeros
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                // Initial portion of PIC again, for 2nd layer
                // ["DI"]  [v1] [11unit][DI num]
                0x44, 0x49, 0x01, 0x11, 0x00, 0x01, 0x20, 0x00,
                //   ["BDR"]           [2 layers]
                0x42, 0x44, 0x4F, 0x01, 0x21, 0x01, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00,
                // Total sectors used on disc
                total_sectors[3], total_sectors[2], total_sectors[1], total_sectors[0],
                // 2nd Layer sector start location
                0x01, 0x35, 0x8C, 0x00,
                // 2nd Layer sector end location
                0x01, 0xEF, 0xFF, 0xFE,
                // Remaining 32 bytes are zeroes
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
                // 3k3y style: 0x03 at last byte
                if (exactIRD)
                    PIC[114] = 0x03;

            }
            else // if BD-25
            {
                // Total sectors used on disc: num_sectors + layer_sector_end (0x00100000) - 1
                byte[] total_sectors = BitConverter.GetBytes((uint)(size / 2048 + 1048575));
                // Layer sector end location: num_sectors + layer_sector_end (0x00100000) - 2
                byte[] end_sector = BitConverter.GetBytes((uint)(size / 2048 + 1048574));

                // Initial portion of PIC (24 bytes)
                PIC = new byte[]{ 0x10, 0x02, 0x00, 0x00, 0x44, 0x49, 0x01, 0x08, 0x00, 0x00, 0x20, 0x00,
                                     0x42, 0x44, 0x4F, 0x01, 0x11, 0x01, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00,
                // Total sectors used on disc (4 bytes)
                total_sectors[3], total_sectors[2], total_sectors[1], total_sectors[0],
                // Layer sector start location (4 bytes)
                0x00, 0x10, 0x00, 0x00,
                // Layer sector end location (4 bytes)
                end_sector[3], end_sector[2], end_sector[1], end_sector[0],
                // Remaining 79 bytes are zeroes
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
                0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
            }
        }

        /// <summary>
        /// Generates the UID field by computing the CRC32 hash of the ISO
        /// </summary>
        /// <param name="isoPath">Path to the ISO</param>
        private void GenerateUID(string isoPath)
        {
            // Compute CRC32 hash
            byte[] crc32;
            using (FileStream fs = File.OpenRead(isoPath))
            {
                Crc32 hasher = new Crc32();
                hasher.Append(fs);
                crc32 = hasher.GetCurrentHash();
                Array.Reverse(crc32);
            }

            // Redump ISO CRC32 hash is used as the Unique ID in the reproducible IRD
            UID = BitConverter.ToUInt32(crc32, 0);
        }

        #endregion

        #region Constructors

        /// <summary>
        /// Constructor using .getkey.log for Disc Key
        /// </summary>
        /// <param name="isoPath">Path to the ISO</param>
        /// <param name="getKeyLog">Path to the GetKey log file</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="FileNotFoundException"></exception>
        /// <exception cref="InvalidDataException"></exception>
        public ReIRD(string isoPath, string getKeyLog)
        {
            // Validate ISO path
            if (isoPath == null || isoPath.Length <= 0)
                throw new ArgumentNullException(nameof(isoPath));

            // Check file exists
            var iso = new FileInfo(isoPath);
            if (!iso.Exists)
                throw new FileNotFoundException(isoPath);

            // Calculate size of ISO
            long size = iso.Length;

            // Parse .getkey.log for the Disc Key (Data1Key)
            ParseGetKeyLog(getKeyLog);

            // Generate Data 2 using Disc ID
            GenerateD2(GenerateID(size));
            byte[] d2 = Data2Key;
            if (!((ReadOnlySpan<byte>)Data2Key).SequenceEqual(d2))
                throw new InvalidDataException("Unexpected Disc ID in .getkey.log");

            // Generate Disc PIC
            byte[] pic = PIC;
            GeneratePIC(size);
            if (!((ReadOnlySpan<byte>) PIC).SequenceEqual(pic))
                throw new InvalidDataException("Unexpected PIC in .getkey.log");

            // Generate Unique Identifier using ISO CRC32
            GenerateUID(isoPath);

            // Generate the IRD hashes
            Generate(isoPath);
        }

        /// <summary>
        /// Constructor with required redump-style IRD fields
        /// </summary>
        /// <param name="isoPath">Path to the ISO</param>
        /// <param name="key">Disc Key, redump-style (AES encrypted Data 1)</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="FileNotFoundException"></exception>
        public ReIRD(string isoPath, byte[] key)
        {
            // Validate ISO path
            if (isoPath == null || isoPath.Length <= 0)
                throw new ArgumentNullException(nameof(isoPath));

            // Check file exists
            var iso = new FileInfo(isoPath);
            if (!iso.Exists)
                throw new FileNotFoundException(isoPath);

            // Calculate size of ISO
            long size = iso.Length;

            // Generate Data 1 using Disc Key
            GenerateD1(key);

            // Generate Data 2 using Disc ID
            GenerateD2(GenerateID(size));

            // Generate Disc PIC
            GeneratePIC(size);

            // Generate Unique Identifier using ISO CRC32
            GenerateUID(isoPath);

            // Generate the IRD hashes
            Generate(isoPath);
        }

        /// <summary>
        /// Constructor with additional region to generate a specific Disc ID
        /// </summary>
        /// <param name="isoPath">Path to the ISO</param>
        /// <param name="key">Disc Key, redump-style (AES encrypted Data 1)</param>
        /// <param name="region">Disc Region</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="FileNotFoundException"></exception>
        public ReIRD(string isoPath, byte[] key, Region region)
        {
            // Validate ISO path
            if (isoPath == null || isoPath.Length <= 0)
                throw new ArgumentNullException(nameof(isoPath));

            // Check file exists
            var iso = new FileInfo(isoPath);
            if (!iso.Exists)
                throw new FileNotFoundException(isoPath);

            // Calculate size of ISO
            long size = iso.Length;

            // Generate Unique Identifier using ISO CRC32
            GenerateUID(isoPath);

            // Generate Data 1 using Disc Key
            GenerateD1(key);

            // Generate Data 2 using Disc ID
            GenerateD2(GenerateID(size, region));

            // Generate Disc PIC
            GeneratePIC(size);

            // Generate the IRD hashes
            Generate(isoPath);
        }

        #endregion
    }
}
