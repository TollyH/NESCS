namespace NESCS.Mappers
{
    public class NROM(NESSystem nesSystem, Mirroring nametableMirroring, bool useLargerPrgRom, bool useLargerPrgRam, bool prgRamPresent) : IMapper
    {
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
                    // ROM is read-only
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
            address &= 0xFFF;
            return NametableMirroring switch
            {
                Mirroring.Horizontal => address switch
                {
                    <= 0x3FF => address - 0x000,
                    <= 0xBFF => address - 0x400,
                    <= 0xFFF => address - 0x800,
                    _ => throw new ArgumentException("Invalid nametable address")
                },
                Mirroring.Vertical => address switch
                {
                    <= 0x7FF => address - 0x000,
                    <= 0xFFF => address - 0x800,
                    _ => throw new ArgumentException("Invalid nametable address")
                },
                _ => throw new ArgumentException("Invalid mirroring type, must be either Horizontal or Vertical.")
            };
        }
    }
}
