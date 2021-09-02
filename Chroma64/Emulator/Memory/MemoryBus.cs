using Chroma64.Emulator.IO;
using Chroma64.Util;

namespace Chroma64.Emulator.Memory
{
    class MemoryBus
    {
        public BigEndianMemory RDRAM = new BigEndianMemory(0x400000);
        public BigEndianMemory SP_DMEM = new BigEndianMemory(0x1000);
        public BigEndianMemory SP_IMEM = new BigEndianMemory(0x1000);

        public RDRAMInterface RI = new RDRAMInterface();
        public PeripheralInterface PI = new PeripheralInterface();

        private ROM rom;

        public MemoryBus(ROM rom)
        {
            this.rom = rom;
        }

        public T Read<T>(ulong addr) where T : unmanaged
        {
            addr = GetPhysicalAddress(addr & 0xFFFFFFFF);

            // RDRAM (Built-in)
            if (addr >= 0x00000000 && addr <= 0x003FFFFF)
                return RDRAM.Read<T>(addr & 0x3FFFFF);

            // SP DMEM
            else if (addr >= 0x04000000 && addr <= 0x04000FFF)
                return SP_DMEM.Read<T>(addr & 0xFFF);

            // SP IMEM
            else if (addr >= 0x04001000 && addr <= 0x04001FFF)
                return SP_IMEM.Read<T>(addr & 0xFFF);

            // Peripheral Interface
            else if (addr >= 0x04600000 && addr <= 0x046FFFFF)
                return PI.Read<T>(addr & 0xFFFFF);

            // RDRAM Interface
            else if (addr >= 0x04700000 && addr <= 0x047FFFFF)
                return RI.Read<T>(addr & 0xFFFFF);

            // Cartridge Domain 1 Address 2 (ROM)
            else if (addr >= 0x10000000 && addr <= 0x1FBFFFFF)
                return rom.Read<T>(addr - 0x10000000);

            Log.CriticalError($"Read from unknown address 0x{addr:X8}");
            return default;
        }

        public void Write<T>(ulong addr, T val) where T : unmanaged
        {
            addr = GetPhysicalAddress(addr & 0xFFFFFFFF);

            // RDRAM (Built-in)
            if (addr >= 0x00000000 && addr <= 0x003FFFFF)
                RDRAM.Write(addr & 0x3FFFFF, val);

            // SP DMEM
            else if (addr >= 0x04000000 && addr <= 0x04000FFF)
                SP_DMEM.Write(addr & 0xFFF, val);

            // SP IMEM
            else if (addr >= 0x04001000 && addr <= 0x04001FFF)
                SP_IMEM.Write(addr & 0xFFF, val);

            // Peripheral Interface
            else if (addr >= 0x04600000 && addr <= 0x046FFFFF)
                PI.Write(addr & 0xFFFFF, val);

            // RDRAM Interface
            else if (addr >= 0x04700000 && addr <= 0x047FFFFF)
                RI.Write(addr & 0xFFFFF, val);

            else
                Log.CriticalError($"Write to unknown address 0x{addr:X8}");
        }

        private ulong GetPhysicalAddress(ulong addr)
        {
            // KUSEG
            if (addr < 0x80000000)
            {
                Log.FatalError("Unimplemented access to KUSEG");
                return 0;
            }

            // KSEG0
            else if (addr < 0xA0000000)
                return addr - 0x80000000;

            // KSEG1
            else if (addr < 0xC0000000)
                return addr - 0xA0000000;

            // KSSEG
            else if (addr < 0xE0000000)
            {
                Log.FatalError("Unimplemented access to KSSEG");
                return 0;
            }

            // KSEG3
            else
            {
                Log.FatalError("Unimplemented access to KSEG3");
                return 0;
            }
        }
    }
}
