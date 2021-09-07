using Chroma64.Emulator.IO;
using Chroma64.Util;

namespace Chroma64.Emulator.Memory
{
    class MemoryBus
    {
        public BigEndianMemory RDRAM = new BigEndianMemory(0x400000);
        public BigEndianMemory SP_DMEM = new BigEndianMemory(0x1000);
        public BigEndianMemory SP_IMEM = new BigEndianMemory(0x1000);

        public RDRAMInterface RI;
        public PeripheralInterface PI;
        public MIPSInterface MI;
        public SerialInterface SI;
        public AudioInterface AI;
        public VideoInterface VI;

        public ROM ROM;
        public PIFRAM PIFRAM;

        public MemoryBus(ROM rom)
        {
            ROM = rom;
            RI = new RDRAMInterface();
            PI = new PeripheralInterface(this);
            MI = new MIPSInterface();
            SI = new SerialInterface(this);
            AI = new AudioInterface();
            PIFRAM = new PIFRAM();
            VI = new VideoInterface(this);
        }

        public T Read<T>(ulong addr) where T : unmanaged
        {
            addr = GetPhysicalAddress(addr & 0xFFFFFFFF);

            // RDRAM (Built-in)
            if (addr >= 0x00000000 && addr <= 0x003FFFFF)
                return RDRAM.Read<T>(addr & 0x3FFFFF);

            // TODO: RDRAM (Expansion Pak)
            else if (addr >= 0x00400000 && addr <= 0x007FFFFF)
                return default;

            // SP DMEM
            else if (addr >= 0x04000000 && addr <= 0x04000FFF)
                return SP_DMEM.Read<T>(addr & 0xFFF);

            // SP IMEM
            else if (addr >= 0x04001000 && addr <= 0x04001FFF)
                return SP_IMEM.Read<T>(addr & 0xFFF);

            // TODO: SP Registers
            else if (addr >= 0x04040000 && addr <= 0x040FFFFF)
            {
                Log.Warning($"Unimplemented read from SP Register @ 0x{addr:X8}");
                return default;
            }

            // MIPS Interface
            else if (addr >= 0x04300000 && addr <= 0x043FFFFF)
                return MI.Read<T>(addr & 0xFFFFF);

            // Video Interface
            else if (addr >= 0x04400000 && addr <= 0x044FFFFF)
                return VI.Read<T>(addr & 0xFFFFF);

            // Audio Interface
            else if (addr >= 0x04500000 && addr <= 0x045FFFFF)
                return AI.Read<T>(addr & 0xFFFFF);

            // Peripheral Interface
            else if (addr >= 0x04600000 && addr <= 0x046FFFFF)
                return PI.Read<T>(addr & 0xFFFFF);

            // RDRAM Interface
            else if (addr >= 0x04700000 && addr <= 0x047FFFFF)
                return RI.Read<T>(addr & 0xFFFFF);

            // Serial Interface
            else if (addr >= 0x04800000 && addr <= 0x048FFFFF)
                return SI.Read<T>(addr & 0xFFFFF);

            // Cartridge Domain 1 Address 2 (ROM)
            else if (addr >= 0x10000000 && addr <= 0x1FBFFFFF)
                return ROM.Read<T>(addr - 0x10000000);

            // PIF RAM
            else if (addr >= 0x1FC007C0 && addr <= 0x1FC007FF)
                return PIFRAM.Read<T>(addr & 0x3F);

            Log.CriticalError($"Read from unknown address 0x{addr:X8}");
            return default;
        }

        public void Write<T>(ulong addr, T val) where T : unmanaged
        {
            addr = GetPhysicalAddress(addr & 0xFFFFFFFF);

            unsafe
            {
                if ((addr - (ulong)sizeof(T)) >= 0x80113808 && addr < 0x80113808)
                    Log.Info($"WRITE!!!! {val:X} to {addr:X8}");
            }

            int width = (int)(VI.GetRegister(IO.VI.WIDTH_REG) & 0xFFF);
            int yScale = (int)VI.GetRegister(IO.VI.Y_SCALE_REG);
            int height = ((15 * yScale) / 64);
            if (addr >= VI.GetRegister(IO.VI.ORIGIN_REG) && addr <= (ulong)(VI.GetRegister(IO.VI.ORIGIN_REG) + 2 * width * height))
            {
                Log.Info($"Wrote {val:X} to Framebuffer @ {addr:X8}");
            }

            // RDRAM (Built-in)
            if (addr >= 0x00000000 && addr <= 0x003FFFFF)
                RDRAM.Write(addr & 0x3FFFFF, val);

            // TODO: RDRAM (Expansion Pak)
            else if (addr >= 0x00400000 && addr <= 0x007FFFFF)
                return;

            // SP DMEM
            else if (addr >= 0x04000000 && addr <= 0x04000FFF)
                SP_DMEM.Write(addr & 0xFFF, val);

            // SP IMEM
            else if (addr >= 0x04001000 && addr <= 0x04001FFF)
                SP_IMEM.Write(addr & 0xFFF, val);

            // TODO: SP Registers
            else if (addr >= 0x04040000 && addr <= 0x040FFFFF)
            {
                Log.Warning($"Unimplemented write to SP Register @ 0x{addr:X8}");
                return;
            }

            // MIPS Interface
            else if (addr >= 0x04300000 && addr <= 0x043FFFFF)
                MI.Write(addr & 0xFFFFF, val);

            // Video Interface
            else if (addr >= 0x04400000 && addr <= 0x044FFFFF)
                VI.Write(addr & 0xFFFFF, val);

            // Audio Interface
            else if (addr >= 0x04500000 && addr <= 0x045FFFFF)
                AI.Write(addr & 0xFFFFF, val);

            // Peripheral Interface
            else if (addr >= 0x04600000 && addr <= 0x046FFFFF)
                PI.Write(addr & 0xFFFFF, val);

            // RDRAM Interface
            else if (addr >= 0x04700000 && addr <= 0x047FFFFF)
                RI.Write(addr & 0xFFFFF, val);

            // Serial Interface
            else if (addr >= 0x04800000 && addr <= 0x048FFFFF)
                SI.Write(addr & 0xFFFFF, val);

            // PIF RAM
            else if (addr >= 0x1FC007C0 && addr <= 0x1FC007FF)
                PIFRAM.Write(addr & 0x3F, val);

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
