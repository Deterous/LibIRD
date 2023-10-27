using System;

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
            if (layerbreak == 0 || layerbreak >= size)
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
        /// Generates the UID field
        /// </summary>
        /// <param name="crc32">Redump ISO CRC32 hash</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentException"></exception>
        private void GenerateUID(byte[] crc32)
        {
            // Validate CRC32
            if (crc32 == null)
                throw new ArgumentNullException(nameof(crc32));
            if (crc32.Length != 8)
                throw new ArgumentException("CRC32 hash must be an byte array of length 8", nameof(crc32));

            // Redump ISO CRC32 hash is used as the Unique ID in the IRD
            UID = BitConverter.ToUInt32(crc32, 0);
        }

        #endregion

        #region Constructors

        /// <summary>
        /// Constructor with required redump-style IRD fields
        /// </summary>
        /// <param name="size">ISO Size in bytes, a multiple of 2048</param>
        /// <param name="crc32">CRC32 hash of the redump ISO</param>
        /// <param name="key">Disc Key, redump-style (AES encrypted Data 1)</param>
        public ReIRD(long size, byte[] crc32, byte[] key)
        {
            // Generate Unique Identifier using ISO CRC32
            GenerateUID(crc32);

            // Generate Data 1 using Disc Key
            GenerateD1(key);

            // Generate Data 2 using Disc ID
            GenerateD2(GenerateID(size));

            // Generate Disc PIC
            GeneratePIC(size);
        }

        /// <summary>
        /// Constructor with additional region to generate a specific Disc ID
        /// </summary>
        /// <param name="size">ISO Size in bytes, a multiple of 2048</param>
        /// <param name="crc32">CRC32 hash of the redump ISO</param>
        /// <param name="key">Disc Key, redump-style (AES encrypted Data 1)</param>
        /// <param name="region">Disc Region</param>
        public ReIRD(long size, byte[] crc32, byte[] key, Region region)
        {
            // Generate Unique Identifier using ISO CRC32
            GenerateUID(crc32);

            // Generate Data 1 using Disc Key
            GenerateD1(key);

            // Generate Data 2 using Disc ID
            GenerateD2(GenerateID(size, region));

            // Generate Disc PIC
            GeneratePIC(size);
        }

        #endregion

        #region Methods

        /// <summary>
        /// Generate and write the redump-style IRD file, after any optional IRD fields have been set
        /// </summary>
        /// <param name="discPath">Full path to the disc drive / mounted ISO</param>
        /// <param name="irdPath">Full path to IRD file to be written to</param>
        public void Create(string discPath, string irdPath)
        {
            // Generate the IRD hashes
            Generate(discPath);

            // Write to IRD file
            Write(irdPath);
        }

        #endregion
    }
}
