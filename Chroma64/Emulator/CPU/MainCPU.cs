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
        private long[] regs = new long[32];
        private ulong pc = 0xA4000040;

        private COP0 cop0 = new COP0();
        private MemoryBus bus;

        private Dictionary<uint, Action<uint>> instrs = new Dictionary<uint, Action<uint>>();
        private Dictionary<uint, Action<uint>> instrsSpecial = new Dictionary<uint, Action<uint>>();
        private Dictionary<uint, Action<uint>> instrsRegimm = new Dictionary<uint, Action<uint>>();
        private Dictionary<uint, Action<uint>> instrsCop = new Dictionary<uint, Action<uint>>();

        // Branch Instruction Variables
        private int branchQueued = 0;
        private ulong branchTarget;

        public MainCPU(MemoryBus bus)
        {
            this.bus = bus;

            // # Simulating the PIF ROM

            // Copying 0x1000 bytes from Cartridge to SP DMEM
            for (int i = 0; i < 0x1000; i++)
                bus.Write((ulong)(0xA4000000 + i), bus.Read<byte>((ulong)(0xB0000000 + i)));

            // Initialize CPU Registers
            unchecked
            {
                regs[11] = (long)0xFFFFFFFFA4000040;
                regs[20] = 0x0000000000000001;
                regs[22] = 0x000000000000003F;
                regs[29] = (long)0xFFFFFFFFA4001FF0;
            }

            // Initialize COP0 Registers
            cop0.Registers[1] = 0x0000001F;
            cop0.Registers[12] = 0x70400004;
            cop0.Registers[15] = 0x00000B00;
            cop0.Registers[16] = 0x0006E463;

            // # Initializing Instruction LUT

            instrs = new Dictionary<uint, Action<uint>>()
            {
                { 0, InstrSpecial }, { 1, InstrRegimm }, { 16, InstrCop }, { 17, InstrCop }, { 18, InstrCop },
                { 5, MIPS_BNE },
                { 15, MIPS_LUI }, { 35, MIPS_LW },
                { 9, MIPS_ADDIU },
            };

            instrsCop = new Dictionary<uint, Action<uint>>()
            {
                { 4, MIPS_MTC0 },
            };
        }

        public void Tick(int cycles)
        {
            for (int i = 0; i < cycles; i++)
            {
                // Fetch & increment PC
                uint instr = bus.Read<uint>(pc);
                pc += 4;

                // Decode opcode & execute
                if (instr != 0)
                {
                    uint opcode = (instr & 0xFC000000) >> 26;
                    if (instrs.ContainsKey(opcode))
                        instrs[opcode](instr);
                    else
                    {
                        pc -= 4;
                        Log.FatalError($"Unimplemented Instruction 0x{instr:X8} [Opcode {opcode}] at PC = 0x{pc:X16}");
                    }
                }

                // Handle queued branches
                if (branchQueued > 0 && --branchQueued == 0)
                    pc = branchTarget;
            }
        }

        private void LogInstr(string instr, string msg)
        {
            Log.Info($"[PC = 0x{pc - 4:X8}] {instr.PadRight(6)} : {msg}");
        }

        #region CPU Register Instructions
        private void SetReg(CPUREG reg, long value)
        {
            if (reg != CPUREG.ZERO)
                regs[(int)reg] = value;
        }

        private long GetReg(CPUREG reg)
        {
            return regs[(int)reg];
        }
        #endregion

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

        private void InstrRegimm(uint instr)
        {
            uint opcode = (instr & 0x1F0000) >> 16;
            if (instrsRegimm.ContainsKey(opcode))
                instrsRegimm[opcode](instr);
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



        #region Normal Instructions
        void MIPS_LUI(uint instr)
        {
            CPUREG dest = (CPUREG)((instr & (0x1F << 16)) >> 16);
            long val = (int)((instr & 0xFFFF) << 16);
            SetReg(dest, val);

            LogInstr("LUI", $"{val:X16} -> {dest}");
        }

        void MIPS_ADDIU(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG dest = (CPUREG)((instr & (0x1F << 16)) >> 16);
            long val = (short)(instr & 0xFFFF);
            long regval = GetReg(src);
            SetReg(dest, regval + val);

            LogInstr("ADDIU", $"{src} -> {regval:X16} + {val:X16} -> {regval + val:X16} -> {dest}");
        }

        void MIPS_LW(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG dest = (CPUREG)((instr & (0x1F << 16)) >> 16);
            short offset = (short)(instr & 0xFFFF);
            long baseAddr = GetReg(src);
            ulong addr = (ulong)(baseAddr + offset);
            short val = bus.Read<short>(addr);
            SetReg(dest, val);

            LogInstr("LW", $"{src} -> {baseAddr:X16} + {offset:X4} -> [{addr:X16}] -> {val:X4} -> {dest}");
        }

        void MIPS_BNE(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG dest = (CPUREG)((instr & (0x1F << 16)) >> 16);
            ulong offset = (ulong)(((short)(instr & 0xFFFF)) << 2);
            ulong addr = pc + offset;
            long val1 = GetReg(src);
            long val2 = GetReg(dest);
            bool cond = val1 != val2;
            if (cond)
            {
                branchQueued = 2;
                branchTarget = addr;
            }

            LogInstr("BNE", $"{src} == {dest} -> {val1:X16} == {val2:X16} -> {(cond ? "" : "No ")}Branch to {addr:X8}");
        }
        #endregion

        #region Coprocessor Instructions
        void MIPS_MTC0(uint instr)
        {
            COP0REG dest = (COP0REG)((instr & (0x1F << 11)) >> 11);
            CPUREG src = (CPUREG)((instr & (0x1F << 16)) >> 16);
            cop0.SetReg(dest, GetReg(src));

            LogInstr("MTC0", $"{src} -> {GetReg(src):X16} -> {dest}");
        }
        #endregion

    }
}
