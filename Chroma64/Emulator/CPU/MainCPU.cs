using Chroma64.Emulator.Memory;
using Chroma64.Util;
using System;
using System.Collections.Generic;

namespace Chroma64.Emulator.CPU
{
    public enum CPUREG
    {
        ZERO, AT, V0, V1, A0, A1, A2, A3, T0, T1, T2, T3, T4, T5, T6, T7, S0, S1, S2, S3, S4, S5, S6, S7, T8, T9, K0, K1, GP, SP, S8, RA
    }

    class MainCPU
    {
        private ulong[] regs = new ulong[32];
        private ulong pc = 0xA4000040;

        private COP0 cop0 = new COP0();
        private MemoryBus bus;

        public MainCPU(MemoryBus bus)
        {
            this.bus = bus;

            // # Simulating the PIF ROM

            // Copying 0x1000 bytes from Cartridge to SP DMEM
            for (int i = 0; i < 0x1000; i++)
                bus.Write((ulong)(0xA4000000 + i), bus.Read<byte>((ulong)(0xB0000000 + i)));

            // Initialize CPU Registers
            regs[11] = 0xFFFFFFFFA4000040;
            regs[20] = 0x0000000000000001;
            regs[22] = 0x000000000000003F;
            regs[29] = 0xFFFFFFFFA4001FF0;

            // Initialize COP0 Registers
            cop0.Registers[1] = 0x0000001F;
            cop0.Registers[12] = 0x70400004;
            cop0.Registers[15] = 0x00000B00;
            cop0.Registers[16] = 0x0006E463;
        }

        private void SetReg(CPUREG reg, ulong value)
        {
            if (reg != CPUREG.ZERO)
                regs[(int)reg] = value;
        }

        private ulong GetReg(CPUREG reg)
        {
            return regs[(int)reg];
        }

        public void Tick(int cycles)
        {
            for(int i = 0; i < cycles; i++)
            {
                uint instr = bus.Read<uint>(pc);
                pc += 4;

                // TODO: Implement Instruction Decoding / Execution System
                Log.FatalError($"Unimplemented Instruction: {instr.ToString("X8")}");
            }
        }
    }
}
