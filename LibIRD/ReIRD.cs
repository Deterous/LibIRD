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
    /// Create a reproducible, redump-style IRD file from an ISO and a Disc Key
    /// </summary>
    public class ReIRD : IRD
    {
        #region Properties

        /// <summary>
        /// ISO file size
        /// </summary>
        private long Size { get; set; }

        #endregion

        #region Constructors

        /// <summary>
        /// Constructor using .getkey.log for Disc Key
        /// </summary>
        /// <param name="isoPath">Path to the ISO</param>
        /// <param name="getKeyLog">Path to the GetKey log file</param>
        /// <param name="layerbreak">Layerbreak value, in sectors</param>
        /// <exception cref="InvalidDataException"></exception>
        public ReIRD(string isoPath, string getKeyLog, long? layerbreak = null) : base(isoPath, getKeyLog, true)
        {
            // Generate Unique Identifier using ISO CRC32
            UID = GenerateUID(isoPath);

            // Determine ISO file size
            Size = CalculateSize(isoPath);

            // Generate Data 2 using Disc ID
            DiscID = GenerateID(Size);

            // Generate Disc PIC
            byte[] pic = GeneratePIC(Size, layerbreak * SectorSize);

            // Check that GetKey log matches expected PIC
            if (!((ReadOnlySpan<byte>)PIC).SequenceEqual(pic))
                throw new InvalidDataException("Unexpected PIC in .getkey.log");
        }

        /// <summary>
        /// Constructor with optional additional region to generate a specific Disc ID
        /// </summary>
        /// <param name="isoPath">Path to the ISO</param>
        /// <param name="key">Disc Key, redump-style (AES encrypted Data 1)</param>
        /// <param name="layerbreak">Layerbreak value, in sectors</param>
        /// <param name="region">Disc Region</param>
        public ReIRD(string isoPath, byte[] key, long? layerbreak = null, Region region = Region.NONE) : base()
        {
            // Generate Unique Identifier using ISO CRC32
            UID = GenerateUID(isoPath);

            // Determine ISO file size
            Size = CalculateSize(isoPath);

            // Set Disc Key
            DiscKey = key;

            // Generate Data 2 using Disc ID
            DiscID = GenerateID(Size, region);

            // Generate Disc PIC
            PIC = GeneratePIC(Size, layerbreak * SectorSize);

            // Generate IRD fields
            GenerateIRD(isoPath, true);
        }

        #endregion

        #region Methods

        /// <summary>
        /// Generates a Disc ID given a size and region, where region is a single byte
        /// </summary>
        /// <returns>Valid Disc ID, byte array length 16</returns>
        private static byte[] GenerateID(long size, Region region = Region.NONE)
        {
            if (size > BDLayerSize) // if BD-50, Disc ID is fixed
                return [ 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF,
                                   0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
            else // else if BD-25, Disc ID has a byte referring to disc region
            {
                return [ 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xFF,
                         0x00, 0x02, 0x00, (byte)region, 0x00, 0x00, 0x00, 0x01 ];
            }
        }

        /// <summary>
        /// Generates the PIC data for a given ISO size in bytes
        /// </summary>
        /// <param name="size">Total ISO size in number of bytes</param>
        /// <param name="layerbreak">Layer break value, byte at which disc layers are split across</param>
        /// <param name="exactIRD">True to generate a PIC in 3k3y style (0x03 at 115th byte for BD-50 discs)</param>
        /// <exception cref="ArgumentException"></exception>
        private static byte[] GeneratePIC(long size, long? layerbreak = null, bool exactIRD = false)
        {
            // Validate size
            if (size <= 0 || (size % SectorSize) != 0)
                throw new ArgumentException("ISO Size in bytes must be a positive integer multiple of 2048", nameof(size));
            // Validate layerbreak
            if (layerbreak != null)
            {
                if (layerbreak <= 0 || (layerbreak >= size))
                    throw new ArgumentException("Layerbreak in bytes must be a positive integer less than the ISO Size", nameof(size));
                if (layerbreak >= 2 * BDLayerSize || layerbreak % SectorSize != 0)
                    throw new ArgumentException("Unexpected layerbreak value", nameof(size));
            }
            // If layerbreak value was not set, assume it is a non-hybrid disc with default layerbreak
            long layer_break = layerbreak ?? BDLayerSize;

            // Generate the PIC based on the size and layerbreak of the ISO
            byte[] pic;
            if (size > BDLayerSize) // if BD-50
            {
                // Layer 0 start sector = 0x01000000
             	long l0_start_sector = 1048576;
			
			    // Layer 0 end sector = start sector + layerbreak - 2
			    long l0_end_sector = (layer_break / SectorSize) + l0_start_sector - 2;
			    // Convert end sector location to hex values for PIC
			    byte[] l0es = [(byte)((l0_end_sector >> 24) & 0xFF),
						       (byte)((l0_end_sector >> 16) & 0xFF),
						       (byte)((l0_end_sector >> 8) & 0xFF),
						       (byte)((l0_end_sector >> 0) & 0xFF)];

                // Layer 1 start sector = end of disc (0x01EFFFFE) - layerbreak + 2
                long l1_start_sector = 32505854 - (layer_break / SectorSize) + 2;
			    // Convert start of start sector location to hex values for PIC
			    byte[] l1ss = [(byte)((l1_start_sector >> 24) & 0xFF),
						       (byte)((l1_start_sector >> 16) & 0xFF),
						       (byte)((l1_start_sector >> 8) & 0xFF),
						       (byte)((l1_start_sector >> 0) & 0xFF)];
			
			    // Total sectors used = num_sectors + Layer 0 start + sectors_between_layers (usually 0x01358C00 - 0x00CA73FE - 3)
			    long total_sectors = (size / SectorSize) + l0_start_sector + (l1_start_sector - l0_end_sector - 3);
			    byte[] ts = BitConverter.GetBytes((uint) total_sectors);

			    // Define the PIC
			    pic = [
				    // Initial portion of PIC (24 bytes)
				    // [4098 bytes] [2x 0x00] ["DI"]   [v1] [10units] [DI num]
				    0x10, 0x02, 0x00, 0x00, 0x44, 0x49, 0x01, 0x10, 0x00, 0x00, 0x20, 0x00,
				    //   ["BDR"]         [2 layers]
				    0x42, 0x44, 0x4F, 0x01, 0x21, 0x01, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00,
				    // Total sectors used on disc (4 bytes)
				    ts[3], ts[2], ts[1], ts[0],
				    // 1st Layer sector start location (4 bytes)
				    0x00, 0x10, 0x00, 0x00,
				    // 1st Layer sector end location (4 bytes), 0x00CA73FE for default BD layerbreak of 12219392
				    l0es[0], l0es[1], l0es[2], l0es[3],
				    // 32 bytes of zeros
				    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
				    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
				    // Initial portion of PIC again, for 2nd layer
				    // ["DI"]  [v1] [11unit][DI num]
				    0x44, 0x49, 0x01, 0x11, 0x00, 0x01, 0x20, 0x00,
				    //   ["BDR"]           [2 layers]
				    0x42, 0x44, 0x4F, 0x01, 0x21, 0x01, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00,
				    // Total sectors used on disc
				    ts[3], ts[2], ts[1], ts[0],
				    // 2nd Layer sector start location, 0x01358C00 for default BD layerbreak of 12219392
				    l1ss[0], l1ss[1], l1ss[2], l1ss[3],
				    // 2nd Layer sector end location
				    0x01, 0xEF, 0xFF, 0xFE,
				    // Remaining 32 bytes are zeroes
				    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 ];
			    // 3k3y style: 0x03 at last byte
			    if (exactIRD)
				    pic[114] = 0x03;

		    }
            else // if BD-25
            {
                // Total sectors used on disc: num_sectors + layer_sector_end (0x00100000) - 1
                byte[] total_sectors = BitConverter.GetBytes((uint)(size / SectorSize + 1048575));
                // Layer sector end location: num_sectors + layer_sector_end (0x00100000) - 2
                byte[] end_sector = BitConverter.GetBytes((uint)(size / SectorSize + 1048574));

                // Define the PIC
                pic = [
                    // Initial portion of PIC (24 bytes)
                    0x10, 0x02, 0x00, 0x00, 0x44, 0x49, 0x01, 0x08, 0x00, 0x00, 0x20, 0x00,
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
                    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 ];
            }
            
            return pic;
        }

        /// <summary>
        /// Generates the UID field by computing the CRC32 hash of the ISO
        /// </summary>
        /// <param name="isoPath">Path to the ISO</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="FileNotFoundException"></exception>
        private static uint GenerateUID(string isoPath)
        {
            // Validate ISO path
            ArgumentNullException.ThrowIfNull(isoPath, nameof(isoPath));

            // Check file exists
            var iso = new FileInfo(isoPath);
            if (!iso.Exists)
                throw new FileNotFoundException(nameof(isoPath));

            // Compute CRC32 hash
            byte[] crc32;
            using (FileStream fs = File.OpenRead(isoPath))
            {
                Crc32 hasher = new();
                hasher.Append(fs);
                crc32 = hasher.GetCurrentHash();
            }

            // Redump ISO CRC32 hash is used as the Unique ID in the reproducible IRD
            return BitConverter.ToUInt32(crc32, 0);
        }

        /// <summary>
        /// Calculates ISO file size
        /// </summary>
        /// <param name="isoPath">Path to the ISO</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="FileNotFoundException"></exception>
        private static long CalculateSize(string isoPath)
        {
            // Validate ISO path
            ArgumentNullException.ThrowIfNull(isoPath, nameof(isoPath));

            // Check file exists
            var iso = new FileInfo(isoPath);
            if (!iso.Exists)
                throw new FileNotFoundException(nameof(isoPath));

            // Calculate file size
            return iso.Length;
        }

        #endregion
    }
}
