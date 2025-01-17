using NESCS;

namespace NESCS_Test
{
    [TestClass]
    public sealed class CPUTests
    {
        [TestMethod]
        public void RunNesTestRom()
        {
            byte[] rom = File.ReadAllBytes("nestest.bin");

            Memory memory = new();

            for (ushort i = 0; i < rom.Length; i++)
            {
                memory[(ushort)(i + 0x8000)] = rom[i];
                memory[(ushort)(i + 0xC000)] = rom[i];
            }

            CPU cpu = new(memory)
            {
                CpuRegisters =
                {
                    PC = 0xC000
                }
            };

            bool breakNext = false;
            while (true)
            {
                cpu.ExecuteClockCycle();

                if (cpu.CpuRegisters.PC == 0xC66E)
                {
                    breakNext = true;
                }

                if (breakNext)
                {
                    break;
                }
            }

            Assert.AreEqual((ushort)0, memory.ReadTwoBytes(2));
        }
    }
}
