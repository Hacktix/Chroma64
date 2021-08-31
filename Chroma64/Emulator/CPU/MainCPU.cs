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

        private Dictionary<uint, Action<uint>> instrs = new Dictionary<uint, Action<uint>>();
        private Dictionary<uint, Action<uint>> instrsSpecial = new Dictionary<uint, Action<uint>>();
        private Dictionary<uint, Action<uint>> instrsBranch = new Dictionary<uint, Action<uint>>();
        private Dictionary<uint, Action<uint>> instrsCop = new Dictionary<uint, Action<uint>>();

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

            // # Initializing Instruction LUT
            instrs = new Dictionary<uint, Action<uint>>()
            {
                { 0, InstrSpecial }, { 1, InstrBranch }, { 16, InstrCop }, { 17, InstrCop }, { 18, InstrCop },
            };
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
            for (int i = 0; i < cycles; i++)
            {
                uint instr = bus.Read<uint>(pc);
                pc += 4;

                uint opcode = (instr & 0xFC000000) >> 26;
                if (instrs.ContainsKey(opcode))
                    instrs[opcode](instr);
                else
                {
                    pc -= 4;
                    Log.FatalError($"Unimplemented Instruction 0x{instr:X8} [Opcode {opcode}] at PC = 0x{pc:X16}");
                }
            }
        }

        #region Sub-Instruction Decoders
        private void InstrSpecial(uint instr)
        {
            uint opcode = (instr & 0x1F0000) >> 16;
            if (instrsSpecial.ContainsKey(opcode))
                instrsSpecial[opcode](instr);
            else
            {
                pc -= 4;
                Log.FatalError($"Unimplemented Special Instruction 0x{instr:X8} [Opcode {opcode}] at PC = 0x{pc:X16}");
            }
        }

        private void InstrBranch(uint instr)
        {
            uint opcode = (instr & 0x1F0000) >> 16;
            if (instrsBranch.ContainsKey(opcode))
                instrsBranch[opcode](instr);
            else
            {
                pc -= 4;
                Log.FatalError($"Unimplemented Branch Instruction 0x{instr:X8} [Opcode {opcode}] at PC = 0x{pc:X16}");
            }
        }

        private void InstrCop(uint instr)
        {
            uint opcode = (instr & 0x3E00000) >> 21;
            if (instrsCop.ContainsKey(opcode))
                instrsCop[opcode](instr);
            else
            {
                pc -= 4;
                Log.FatalError($"Unimplemented Coprocessor Instruction 0x{instr:X8} [Opcode {opcode}] at PC = 0x{pc:X16}");
            }
        }
        #endregion
    }
}
