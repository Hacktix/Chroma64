﻿using Chroma.Diagnostics.Logging;
using Chroma64.Emulator.CPU;
using Chroma64.Emulator.IO;
using System;
using System.Runtime.CompilerServices;

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
        public PIF PIF;

        public MainCPU CPU;

        private Log log = LogManager.GetForCurrentAssembly();

        public MemoryBus(ROM rom)
        {
            ROM = rom;
            RI = new RDRAMInterface();
            PI = new PeripheralInterface(this);
            MI = new MIPSInterface(this);
            SI = new SerialInterface(this);
            AI = new AudioInterface(this);
            PIF = new PIF();
            VI = new VideoInterface(this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
                log.Warning($"Unimplemented read from SP Register @ 0x{addr:X8}");
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
                return PIF.Read<T>(addr & 0x3F);

            log.Error($"Read from unknown address 0x{addr:X8}");
            Console.ReadKey();
            Environment.Exit(-1);
            return default;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write<T>(ulong addr, T val) where T : unmanaged
        {
            addr = GetPhysicalAddress(addr & 0xFFFFFFFF);

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
                log.Warning($"Unimplemented write to SP Register @ 0x{addr:X8}");
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
                PIF.Write(addr & 0x3F, val);

            else
            {
                log.Error($"Write to unknown address 0x{addr:X8}");
                Console.ReadKey();
                Environment.Exit(-1);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ulong GetPhysicalAddress(ulong addr)
        {
            // KUSEG
            if (addr < 0x80000000)
                throw new Exception("Unimplemented access to KUSEG");

            // KSEG0
            else if (addr < 0xA0000000)
                return addr - 0x80000000;

            // KSEG1
            else if (addr < 0xC0000000)
                return addr - 0xA0000000;

            // KSSEG
            else if (addr < 0xE0000000)
                throw new Exception("Unimplemented access to KSSEG");

            // KSEG3
            else
                throw new Exception("Unimplemented access to KSEG3");
        }
    }
}
