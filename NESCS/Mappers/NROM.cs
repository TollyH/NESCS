namespace NESCS.Mappers
{
    public class NROM(NESSystem nesSystem, Mirroring nametableMirroring, bool useLargerPrgRom, bool useLargerPrgRam, bool prgRamPresent) : IMapper
    {
        public string Name => nameof(NROM);
        public string[] OtherNames { get; } = new string[] { "iNES Mapper 000", "RROM", "SROM", "RTROM", "STROM", "HROM" };
        public string Description => "Used by many Nintendo games, among others. Is very basic, with no mapping/bank-switching capability." +
            " Supports 16/32 KiB of PRG ROM, and 8 KiB of CHR ROM. Nametables use the NES built-in 2KB CIRAM/VRAM." +
            " Can mirror nametables either horizontally or vertically, but this is fixed via a solder pad and cannot be changed." +
            " Can support 2/4 KiB of PRG RAM for Family Basic.";

        public Mirroring NametableMirroring => nametableMirroring;

        public byte[] PrgRom { get; } = new byte[useLargerPrgRom ? 0x8000 : 0x4000];
        public byte[] ChrRom { get; } = new byte[0x2000];
        public byte[] PrgRam { get; } = new byte[useLargerPrgRam ? 0x0800 : 0x1000];

        public bool PrgRamPresent { get; } = prgRamPresent;

        public byte MappedCPURead(ushort address)
        {
            return address switch
            {
                < 0x6000 => (byte)(address >> 8),  // Open bus
                <= 0x7FFF => PrgRamPresent
                    ? PrgRam[address % PrgRam.Length]
                    : (byte)(address >> 8),  // Open bus
                <= 0xFFFF => PrgRom[address % PrgRom.Length]
            };
        }

        public void MappedCPUWrite(ushort address, byte value)
        {
            switch (address)
            {
                case < 0x6000:
                    break; // Open bus
                case <= 0x7FFF:
                    if (PrgRamPresent)
                    {
                        PrgRam[address % PrgRam.Length] = value;
                    }
                    break;
                case <= 0xFFFF:
                    PrgRom[address % PrgRom.Length] = value;
                    break;
            }
        }

        public byte MappedPPURead(ushort address)
        {
            return address switch
            {
                <= 0x1FFF => ChrRom[address],
                <= 0x3EFF => nesSystem.SystemMemory.CiRam[GetMirroredCiRamAddress(address)],
                _ => (byte)(address >> 8)  // Open bus
            };
        }

        public void MappedPPUWrite(ushort address, byte value)
        {
            switch (address)
            {
                case <= 0x1FFF:
                    // ROM is read-only
                    break;
                case <= 0x3EFF:
                    nesSystem.SystemMemory.CiRam[GetMirroredCiRamAddress(address)] = value;
                    break;
            }
        }

        private int GetMirroredCiRamAddress(ushort address)
        {
            return NametableMirroring switch
            {
                Mirroring.Horizontal => address switch
                {
                    >= 0x2000 and <= 0x23FF => address - 0x2000,
                    (>= 0x2400 and <= 0x27FF)
                        or (>= 0x2800 and <= 0x2BFF) => address - 0x2400,
                    >= 0x2C00 and <= 0x2FFF => address - 0x2800,
                    _ => throw new ArgumentException("Invalid nametable address")
                },
                Mirroring.Vertical => address switch
                {
                    (>= 0x2000 and <= 0x23FF)
                        or (>= 0x2400 and <= 0x27FF) => address - 0x2000,
                    (>= 0x2800 and <= 0x2BFF)
                        or (>= 0x2C00 and <= 0x2FFF) => address - 0x2800,
                    _ => throw new ArgumentException("Invalid nametable address")
                },
                _ => throw new ArgumentException("Invalid mirroring type")
            };
        }
    }
}
