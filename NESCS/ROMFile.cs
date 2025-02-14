using NESCS.Mappers;

namespace NESCS
{
    public class ROMFile
    {
        public static readonly byte[] MagicBytes = new byte[4] { 0x4E, 0x45, 0x53, 0x1A };  // "NES<EOF>"

        public byte[] PrgRom { get; }

        public byte[] ChrRom { get; }

        public Mirroring NametableMirroring { get; }

        public bool PrgRamPresent { get; }

        public bool AlternativeNametableLayout { get; }

        public int MapperNumber { get; }

        /// <summary>
        /// Loads data from a ROM file in the iNES format.
        /// </summary>
        /// <param name="file">
        /// The raw bytes of the ROM file.
        /// </param>
        public ROMFile(Span<byte> file)
        {
            if (!file[..4].SequenceEqual(MagicBytes))
            {
                throw new ArgumentException("File does not start with the correct header bytes.");
            }

            byte flags6 = file[6];
            byte flags7 = file[7];

            NametableMirroring = (flags6 & 1) != 0 ? Mirroring.Vertical : Mirroring.Horizontal;
            PrgRamPresent = (flags6 & 0b10) != 0;

            if ((flags6 & 0b100) != 0)
            {
                throw new NotSupportedException("ROM files with trainer data are currently unsupported.");
            }

            AlternativeNametableLayout = (flags6 & 0b1000) != 0;

            MapperNumber = (flags6 & 0b11110000) >> 4;
            MapperNumber |= flags7 & 0b11110000;

            if ((flags7 & 0b11) != 0)
            {
                throw new NotSupportedException("ROM files for the VS System, PlayChoice-10, and other non-NES/Famicom are currently unsupported.");
            }

            int prgRomSize;
            int chrRomSize;
            if ((flags7 & 0b1100) == 0b1000)
            {
                // NES 2.0 format
                byte flags8 = file[8];
                MapperNumber |= (flags8 & 0b1111) << 8;

                if ((flags8 & 0b11110000) != 0)
                {
                    throw new NotSupportedException("Submappers are currently unsupported.");
                }

                byte prgChrSizeByte = file[9];

                byte prgLsb = file[4];
                int prgMsb = prgChrSizeByte & 0b1111;
                if (prgMsb == 0xF)
                {
                    // Exponential format
                    prgRomSize = (1 << (prgLsb >> 2)) * ((prgLsb & 0b11) * 2 + 1);
                }
                else
                {
                    // 16 KiB chunk format
                    prgRomSize = (prgLsb | (prgMsb << 8)) * 16384;
                }

                byte chrLsb = file[5];
                int chrMsb = (prgChrSizeByte & 0b11110000) >> 4;
                if (chrMsb == 0xF)
                {
                    // Exponential format
                    chrRomSize = (1 << (chrLsb >> 2)) * ((chrLsb & 0b11) * 2 + 1);
                }
                else
                {
                    // 8 KiB chunk format
                    chrRomSize = (chrLsb | (chrMsb << 8)) * 8192;
                }
            }
            else
            {
                // iNES format
                // PRG ROM size is specified in 16 KiB chunks
                prgRomSize = file[4] * 16384;
                // CHR ROM size is specified in 8 KiB chunks
                chrRomSize = file[5] * 8192;
            }

            PrgRom = new byte[prgRomSize];

            ChrRom = new byte[chrRomSize];

            int chrRomStart = PrgRom.Length + 16;

            file[16..chrRomStart].CopyTo(PrgRom);
            file[chrRomStart..].CopyTo(ChrRom);
        }

        public IMapper InitializeNewMapper(NESSystem nesSystem)
        {
            switch (MapperNumber)
            {
                // NROM
                case 0:
                    // The only official mapper 0 games to use PRG RAM (Family Basic) either use both larger PRG ROM and larger PRG RAM or neither.
                    bool prgLarger = PrgRom.Length > 16384;
                    NROM nromMapper = new(nesSystem, NametableMirroring, prgLarger, prgLarger, PrgRamPresent);
                    PrgRom.CopyTo(nromMapper.PrgRom, 0);
                    ChrRom.CopyTo(nromMapper.ChrRom, 0);
                    return nromMapper;
                default:
                    throw new NotSupportedException($"Mapper with ID {MapperNumber} is currently unsupported.");
            }
        }
    }
}
