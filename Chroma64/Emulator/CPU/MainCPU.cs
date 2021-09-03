﻿using Chroma64.Emulator.Memory;
using Chroma64.Util;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Chroma64.Emulator.CPU
{
    public enum CPUREG
    {
        ZERO, AT, V0, V1, A0, A1, A2, A3, T0, T1, T2, T3, T4, T5, T6, T7, S0, S1, S2, S3, S4, S5, S6, S7, T8, T9, K0, K1, GP, SP, S8, RA
    }

    public enum InstructionType
    {
        Normal, Special, REGIMM, COP
    }

    class MainCPU
    {
        private long[] regs = new long[32];
        private ulong pc = 0xA4000040;

        private long hi;
        private long lo;

        private ulong breakpoint = 0;
        private bool debugging = false;

        private COP0 cop0 = new COP0();
        private COP1 cop1 = new COP1();
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
                // Instruction Subset Decoders
                { 0, InstrSpecial }, { 1, InstrRegimm }, { 16, InstrCop }, { 17, InstrCop }, { 18, InstrCop },

                // Branch Instructions
                { 2, MIPS_J }, { 3, MIPS_JAL }, { 4, MIPS_BEQ }, { 5, MIPS_BNE }, { 6, MIPS_BLEZ }, { 20, MIPS_BEQL }, { 21, MIPS_BNEL }, { 7, MIPS_BGTZ },

                // Load Instructions
                { 15, MIPS_LUI }, { 32, MIPS_LB }, { 36, MIPS_LBU }, { 33, MIPS_LH }, { 37, MIPS_LHU }, { 35, MIPS_LW }, { 55, MIPS_LD },

                // Store Instructions
                { 40, MIPS_SB }, { 41, MIPS_SH }, { 43, MIPS_SW }, { 63, MIPS_SD },

                // Arithmetic Operations
                { 9, MIPS_ADDIU }, { 8, MIPS_ADDI }, { 24, MIPS_DADDI },

                // Bitwise Operations
                { 13, MIPS_ORI }, { 12, MIPS_ANDI }, { 14, MIPS_XORI },

                // Misc.
                { 47, MIPS_CACHE }, { 10, MIPS_SLTI }, { 11, MIPS_SLTIU },
            };

            instrsSpecial = new Dictionary<uint, Action<uint>>()
            {
                // Bitwise Operations
                { 36, MIPS_AND }, { 37, MIPS_OR }, { 38, MIPS_XOR }, { 2, MIPS_SRL }, { 3, MIPS_SRA }, { 6, MIPS_SRLV }, { 0, MIPS_SLL }, { 4, MIPS_SLLV },

                // Arithmetic Operations
                { 32, MIPS_ADD }, { 33, MIPS_ADDU }, { 35, MIPS_SUBU }, { 25, MIPS_MULTU }, { 24, MIPS_MULT }, { 27, MIPS_DIVU }, { 44, MIPS_DADD },

                // Control Flow
                { 8, MIPS_JR },

                // Misc.
                { 42, MIPS_SLT }, { 43, MIPS_SLTU }, { 18, MIPS_MFLO }, { 16, MIPS_MFHI }, { 19, MIPS_MTLO }, { 17, MIPS_MTHI },
            };

            instrsRegimm = new Dictionary<uint, Action<uint>>()
            {
                { 1, MIPS_BGEZ }, { 17, MIPS_BGEZAL },
            };

            instrsCop = new Dictionary<uint, Action<uint>>()
            {
                { 0, MIPS_MFC0 }, { 4, MIPS_MTC0 }, { 2, MIPS_CFC1 }, { 6, MIPS_CTC1 },

                { 16, MIPS_TLBWI },
            };
        }

        public void Tick(int cycles)
        {
            for (int i = 0; i < cycles; i++)
            {
                CheckBreakpoint();

                // Fetch & increment PC
                uint instr = bus.Read<uint>(pc);
                pc += 4;

                // Decode opcode & execute
                if (instr != 0)
                {
                    if(instr == 0b01000010000000000000000000011000)
                        MIPS_ERET();
                    else
                    {
                        uint opcode = (instr & 0xFC000000) >> 26;
                        CheckInstructionImplemented(instr, opcode, InstructionType.Normal);
                        instrs[opcode](instr);
                    }
                }
                else
                    LogInstr("NOP", "-");

                // Handle queued branches
                if (branchQueued > 0 && --branchQueued == 0)
                    pc = branchTarget;
            }
        }

        #region Debug Methods
        [Conditional("DEBUG")]
        private void LogInstr(string instr, string msg)
        {
            if (debugging)
            {
                Log.Info($"[PC = 0x{(pc - 4) & 0xFFFFFFFF:X8}] {instr.PadRight(6)} : {msg}");
                var input = Console.ReadKey();
                if (input.Key == ConsoleKey.Enter)
                    debugging = false;
            }
        }

        [Conditional("DEBUG")]
        private void CheckBreakpoint()
        {
            if ((pc & 0xFFFFFFFF) == breakpoint)
                debugging = true;
        }

        [Conditional("DEBUG")]
        private void CheckInstructionImplemented(uint instr, uint opcode, InstructionType type)
        {
            switch (type)
            {
                case InstructionType.Normal:
                    if (!instrs.ContainsKey(opcode))
                    {
                        pc -= 4;
                        Log.FatalError($"Unimplemented Instruction 0x{instr:X8} [Opcode {opcode}] at PC = 0x{pc:X16}");
                    }
                    break;
                case InstructionType.Special:
                    if (!instrsSpecial.ContainsKey(opcode))
                    {
                        pc -= 4;
                        Log.FatalError($"Unimplemented Special Instruction 0x{instr:X8} [Opcode {opcode}] at PC = 0x{pc:X16}");
                    }
                    break;
                case InstructionType.REGIMM:
                    if (!instrsRegimm.ContainsKey(opcode))
                    {
                        pc -= 4;
                        Log.FatalError($"Unimplemented REGIMM Instruction 0x{instr:X8} [Opcode {opcode}] at PC = 0x{pc:X16}");
                    }
                    break;
                case InstructionType.COP:
                    if (!instrsCop.ContainsKey(opcode))
                    {
                        pc -= 4;
                        Log.FatalError($"Unimplemented Coprocessor Instruction 0x{instr:X8} [Opcode {opcode}] at PC = 0x{pc:X16}");
                    }
                    break;
            }

        }
        #endregion

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
            uint opcode = instr & 0x3F;
            CheckInstructionImplemented(instr, opcode, InstructionType.Special);
            instrsSpecial[opcode](instr);
        }

        private void InstrRegimm(uint instr)
        {
            uint opcode = (instr & 0x1F0000) >> 16;
            CheckInstructionImplemented(instr, opcode, InstructionType.REGIMM);
            instrsRegimm[opcode](instr);
        }

        private void InstrCop(uint instr)
        {
            uint opcode = (instr & 0x3E00000) >> 21;
            CheckInstructionImplemented(instr, opcode, InstructionType.COP);
            instrsCop[opcode](instr);
        }
        #endregion

        // # Instruction Implementations

        #region Normal Instructions

        #region Branch Instructions

        void MIPS_J(uint instr)
        {
            ulong addr = (ulong)(int)((pc & 0xFC000000) | ((instr & 0x3FFFFFF) << 2));
            branchQueued = 2;
            branchTarget = addr;

            LogInstr("J", $"{addr:X16} -> PC");
        }

        void MIPS_JAL(uint instr)
        {
            ulong addr = (ulong)(int)((pc & 0xFC000000) | ((instr & 0x3FFFFFF) << 2));
            SetReg(CPUREG.RA, (long)pc + 4);
            branchQueued = 2;
            branchTarget = addr;

            LogInstr("JAL", $"{addr:X16} -> PC");
        }

        void MIPS_BEQ(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG dest = (CPUREG)((instr & (0x1F << 16)) >> 16);
            ulong offset = (ulong)(((short)(instr & 0xFFFF)) << 2);
            ulong addr = pc + offset;
            long val1 = GetReg(src);
            long val2 = GetReg(dest);
            bool cond = val1 == val2;
            if (cond)
            {
                branchQueued = 2;
                branchTarget = addr;
            }

            LogInstr("BEQ", $"{src} == {dest} -> {val1:X16} == {val2:X16} -> {(cond ? "" : "No ")}Branch to {addr:X8}");
        }

        void MIPS_BEQL(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG dest = (CPUREG)((instr & (0x1F << 16)) >> 16);
            ulong offset = (ulong)(((short)(instr & 0xFFFF)) << 2);
            ulong addr = pc + offset;
            long val1 = GetReg(src);
            long val2 = GetReg(dest);
            bool cond = val1 == val2;

            LogInstr("BEQL", $"{src} == {dest} -> {val1:X16} == {val2:X16} -> {(cond ? "" : "No ")}Branch to {addr:X8}");

            if (cond)
            {
                branchQueued = 2;
                branchTarget = addr;
            }
            else
                pc += 4;
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

            LogInstr("BNE", $"{src} != {dest} -> {val1:X16} != {val2:X16} -> {(cond ? "" : "No ")}Branch to {addr:X8}");
        }

        void MIPS_BNEL(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG dest = (CPUREG)((instr & (0x1F << 16)) >> 16);
            ulong offset = (ulong)(((short)(instr & 0xFFFF)) << 2);
            ulong addr = pc + offset;
            long val1 = GetReg(src);
            long val2 = GetReg(dest);
            bool cond = val1 != val2;

            LogInstr("BNEL", $"{src} != {dest} -> {val1:X16} != {val2:X16} -> {(cond ? "" : "No ")}Branch to {addr:X8}");

            if (cond)
            {
                branchQueued = 2;
                branchTarget = addr;
            }
            else
                pc += 4;
        }

        void MIPS_BGTZ(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 21)) >> 21);
            ulong offset = (ulong)(((short)(instr & 0xFFFF)) << 2);
            ulong addr = pc + offset;
            long val = GetReg(src);
            bool cond = val > 0;
            if (cond)
            {
                branchQueued = 2;
                branchTarget = addr;
            }

            LogInstr("BGTZ", $"{src} > 0 -> {val:X16} > 0 -> {(cond ? "" : "No ")}Branch to {addr:X8}");
        }

        void MIPS_BLEZ(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 21)) >> 21);
            ulong offset = (ulong)(((short)(instr & 0xFFFF)) << 2);
            ulong addr = pc + offset;
            long val = GetReg(src);
            bool cond = val <= 0;
            if (cond)
            {
                branchQueued = 2;
                branchTarget = addr;
            }

            LogInstr("BLEZ", $"{src} <= 0 -> {val:X16} <= 0 -> {(cond ? "" : "No ")}Branch to {addr:X8}");
        }

        #endregion

        #region Load Instructions

        void MIPS_LUI(uint instr)
        {
            CPUREG dest = (CPUREG)((instr & (0x1F << 16)) >> 16);
            long val = (int)((instr & 0xFFFF) << 16);
            SetReg(dest, val);

            LogInstr("LUI", $"{val:X16} -> {dest}");
        }

        void MIPS_LB(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG dest = (CPUREG)((instr & (0x1F << 16)) >> 16);
            short offset = (short)(instr & 0xFFFF);
            long baseAddr = GetReg(src);
            ulong addr = (ulong)(baseAddr + offset);
            sbyte val = bus.Read<sbyte>(addr);
            SetReg(dest, val);

            LogInstr("LB", $"[{src}] -> [{baseAddr:X16} + {offset:X4} = {addr:X16}] -> {val:X2} -> {dest}");
        }

        void MIPS_LBU(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG dest = (CPUREG)((instr & (0x1F << 16)) >> 16);
            short offset = (short)(instr & 0xFFFF);
            long baseAddr = GetReg(src);
            ulong addr = (ulong)(baseAddr + offset);
            byte val = bus.Read<byte>(addr);
            SetReg(dest, val);

            LogInstr("LB", $"[{src}] -> [{baseAddr:X16} + {offset:X4} = {addr:X16}] -> {val:X2} -> {dest}");
        }

        void MIPS_LH(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG dest = (CPUREG)((instr & (0x1F << 16)) >> 16);
            short offset = (short)(instr & 0xFFFF);
            long baseAddr = GetReg(src);
            ulong addr = (ulong)(baseAddr + offset);
            short val = bus.Read<short>(addr);
            SetReg(dest, val);

            LogInstr("LH", $"[{src}] -> [{baseAddr:X16} + {offset:X4} = {addr:X16}] -> {val:X4} -> {dest}");
        }

        void MIPS_LHU(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG dest = (CPUREG)((instr & (0x1F << 16)) >> 16);
            short offset = (short)(instr & 0xFFFF);
            long baseAddr = GetReg(src);
            ulong addr = (ulong)(baseAddr + offset);
            ushort val = bus.Read<ushort>(addr);
            SetReg(dest, val);

            LogInstr("LHU", $"[{src}] -> [{baseAddr:X16} + {offset:X4} = {addr:X16}] -> {val:X4} -> {dest}");
        }

        void MIPS_LW(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG dest = (CPUREG)((instr & (0x1F << 16)) >> 16);
            short offset = (short)(instr & 0xFFFF);
            long baseAddr = GetReg(src);
            ulong addr = (ulong)(baseAddr + offset);
            int val = bus.Read<int>(addr);
            SetReg(dest, val);

            LogInstr("LW", $"[{src}] -> [{baseAddr:X16} + {offset:X4} = {addr:X16}] -> {val:X8} -> {dest}");
        }

        void MIPS_LD(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG dest = (CPUREG)((instr & (0x1F << 16)) >> 16);
            short offset = (short)(instr & 0xFFFF);
            long baseAddr = GetReg(src);
            ulong addr = (ulong)(baseAddr + offset);
            long val = bus.Read<long>(addr);
            SetReg(dest, val);

            LogInstr("LD", $"[{src}] -> [{baseAddr:X16} + {offset:X4} = {addr:X16}] -> {val:X16} -> {dest}");
        }

        #endregion

        #region Store Instructions

        void MIPS_SB(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 16)) >> 16);
            CPUREG dest = (CPUREG)((instr & (0x1F << 21)) >> 21);
            short offset = (short)(instr & 0xFFFF);
            long baseAddr = GetReg(dest);
            ulong addr = (ulong)(baseAddr + offset);
            byte val = (byte)GetReg(src);
            bus.Write(addr, val);

            LogInstr("SB", $"{src} -> {val:X2} -> [{dest}] -> [{baseAddr:X16} + {offset:X4} = {addr:X16}]");
        }

        void MIPS_SH(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 16)) >> 16);
            CPUREG dest = (CPUREG)((instr & (0x1F << 21)) >> 21);
            short offset = (short)(instr & 0xFFFF);
            long baseAddr = GetReg(dest);
            ulong addr = (ulong)(baseAddr + offset);
            short val = (short)GetReg(src);
            bus.Write(addr, val);

            LogInstr("SH", $"{src} -> {val:X4} -> [{dest}] -> [{baseAddr:X16} + {offset:X4} = {addr:X16}]");
        }

        void MIPS_SW(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 16)) >> 16);
            CPUREG dest = (CPUREG)((instr & (0x1F << 21)) >> 21);
            short offset = (short)(instr & 0xFFFF);
            long baseAddr = GetReg(dest);
            ulong addr = (ulong)(baseAddr + offset);
            int val = (int)GetReg(src);
            bus.Write(addr, val);

            LogInstr("SW", $"{src} -> {val:X8} -> [{dest}] -> [{baseAddr:X16} + {offset:X4} = {addr:X16}]");
        }

        void MIPS_SD(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 16)) >> 16);
            CPUREG dest = (CPUREG)((instr & (0x1F << 21)) >> 21);
            short offset = (short)(instr & 0xFFFF);
            long baseAddr = GetReg(dest);
            ulong addr = (ulong)(baseAddr + offset);
            long val = GetReg(src);
            bus.Write(addr, val);

            LogInstr("SD", $"{src} -> {val:X16} -> [{dest}] -> [{baseAddr:X16} + {offset:X4} = {addr:X16}]");
        }

        #endregion

        #region Arithmetic Operations

        void MIPS_ADDIU(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG dest = (CPUREG)((instr & (0x1F << 16)) >> 16);
            long val = (short)(instr & 0xFFFF);
            long regval = GetReg(src);
            int res = (int)(regval + val);
            SetReg(dest, res);

            LogInstr("ADDIU", $"{src} -> {regval:X16} + {val:X16} -> {res:X16} -> {dest}");
        }

        void MIPS_ADDI(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG dest = (CPUREG)((instr & (0x1F << 16)) >> 16);
            long val = (short)(instr & 0xFFFF);
            long regval = GetReg(src);
            int res = (int)(regval + val);
            SetReg(dest, res);

            LogInstr("ADDI", $"{src} -> {regval:X16} + {val:X16} -> {res:X16} -> {dest}");
        }

        void MIPS_DADDI(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG dest = (CPUREG)((instr & (0x1F << 16)) >> 16);
            long val = (short)(instr & 0xFFFF);
            long regval = GetReg(src);
            SetReg(dest, regval + val);

            LogInstr("ADDI", $"{src} -> {regval:X16} + {val:X16} -> {regval + val:X16} -> {dest}");
        }
        #endregion

        #region Bitwise Operations

        void MIPS_ORI(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG dest = (CPUREG)((instr & (0x1F << 16)) >> 16);
            ulong val = (ushort)(instr & 0xFFFF);
            ulong regval = (ulong)GetReg(src);
            SetReg(dest, (long)(regval | val));

            LogInstr("ORI", $"{src} -> {regval:X16} | {val:X16} -> {regval | val:X16} -> {dest}");
        }

        void MIPS_ANDI(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG dest = (CPUREG)((instr & (0x1F << 16)) >> 16);
            ulong val = (ushort)(instr & 0xFFFF);
            ulong regval = (ulong)GetReg(src);
            SetReg(dest, (long)(regval & val));

            LogInstr("ANDI", $"{src} -> {regval:X16} & {val:X16} -> {regval & val:X16} -> {dest}");
        }
        void MIPS_XORI(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG dest = (CPUREG)((instr & (0x1F << 16)) >> 16);
            ulong val = (ushort)(instr & 0xFFFF);
            ulong regval = (ulong)GetReg(src);
            SetReg(dest, (long)(regval ^ val));

            LogInstr("XORI", $"{src} -> {regval:X16} | {val:X16} -> {regval ^ val:X16} -> {dest}");
        }

        #endregion

        #region Misc.

        void MIPS_CACHE(uint instr)
        {
            LogInstr("CACHE", $"Not yet implemented.");
        }

        void MIPS_SLTI(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG dest = (CPUREG)((instr & (0x1F << 16)) >> 16);
            long cp1 = GetReg(src);
            long cp2 = (short)(instr & 0xFFFF);
            long val = cp1 < cp2 ? 1 : 0;
            SetReg(dest, val);

            LogInstr("SLTI", $"{src} < {cp2:X16} -> {cp1:X16} < {cp2:X16} -> {val} -> {dest}");
        }

        void MIPS_SLTIU(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG dest = (CPUREG)((instr & (0x1F << 16)) >> 16);
            ulong cp1 = (ulong)GetReg(src);
            ulong cp2 = (ulong)(short)(instr & 0xFFFF);
            long val = cp1 < cp2 ? 1 : 0;
            SetReg(dest, val);

            LogInstr("SLTIU", $"{src} < {cp2:X16} -> {cp1:X16} < {cp2:X16} -> {val} -> {dest}");
        }

        #endregion

        #endregion

        #region Special Instructions

        #region Bitwise Operations
        void MIPS_AND(uint instr)
        {
            CPUREG op1 = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG op2 = (CPUREG)((instr & (0x1F << 16)) >> 16);
            CPUREG dest = (CPUREG)((instr & (0x1F << 11)) >> 11);
            long val1 = GetReg(op1);
            long val2 = GetReg(op2);
            long res = val1 & val2;
            SetReg(dest, res);

            LogInstr("AND", $"{op1} & {op2} -> {val1:X16} & {val2:X16} -> {res:X16} -> {dest}");
        }

        void MIPS_OR(uint instr)
        {
            CPUREG op1 = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG op2 = (CPUREG)((instr & (0x1F << 16)) >> 16);
            CPUREG dest = (CPUREG)((instr & (0x1F << 11)) >> 11);
            long val1 = GetReg(op1);
            long val2 = GetReg(op2);
            long res = val1 | val2;
            SetReg(dest, res);

            LogInstr("OR", $"{op1} | {op2} -> {val1:X16} | {val2:X16} -> {res:X16} -> {dest}");
        }

        void MIPS_XOR(uint instr)
        {
            CPUREG op1 = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG op2 = (CPUREG)((instr & (0x1F << 16)) >> 16);
            CPUREG dest = (CPUREG)((instr & (0x1F << 11)) >> 11);
            long val1 = GetReg(op1);
            long val2 = GetReg(op2);
            long res = val1 ^ val2;
            SetReg(dest, res);

            LogInstr("XOR", $"{op1} ^ {op2} -> {val1:X16} ^ {val2:X16} -> {res:X16} -> {dest}");
        }

        void MIPS_SRL(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 16)) >> 16);
            CPUREG dest = (CPUREG)((instr & (0x1F << 11)) >> 11);
            uint val = (uint)GetReg(src);
            int shift = (int)(instr & (0x1F << 6)) >> 6;
            long res = (int)(val >> shift);
            SetReg(dest, res);

            LogInstr("SRL", $"{src} >> {shift} -> {val:X16} >> {shift:X} -> {res:X16} -> {dest}");
        }

        void MIPS_SRA(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 16)) >> 16);
            CPUREG dest = (CPUREG)((instr & (0x1F << 11)) >> 11);
            int val = (int)GetReg(src);
            int shift = (int)(instr & (0x1F << 6)) >> 6;
            long res = val >> shift;
            SetReg(dest, res);

            LogInstr("SRA", $"{src} >> {shift} -> {val:X16} >> {shift:X} -> {res:X16} -> {dest}");
        }

        void MIPS_SRLV(uint instr)
        {
            CPUREG op = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG src = (CPUREG)((instr & (0x1F << 16)) >> 16);
            CPUREG dest = (CPUREG)((instr & (0x1F << 11)) >> 11);
            uint val = (uint)GetReg(src);
            int shift = (int)(GetReg(op) & 0x1F);
            long res = (int)(val >> shift);
            SetReg(dest, res);

            LogInstr("SRLV", $"{src} >> {op} -> {val:X16} >> {shift:X} -> {res:X16} -> {dest}");
        }

        void MIPS_SLL(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 16)) >> 16);
            CPUREG dest = (CPUREG)((instr & (0x1F << 11)) >> 11);
            uint val = (uint)GetReg(src);
            int shift = (int)(instr & (0x1F << 6)) >> 6;
            long res = (int)(val << shift);
            SetReg(dest, res);

            LogInstr("SLL", $"{src} << {shift} -> {val:X16} << {shift:X} -> {res:X16} -> {dest}");
        }

        void MIPS_SLLV(uint instr)
        {
            CPUREG op = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG src = (CPUREG)((instr & (0x1F << 16)) >> 16);
            CPUREG dest = (CPUREG)((instr & (0x1F << 11)) >> 11);
            uint val = (uint)GetReg(src);
            int shift = (int)(GetReg(op) & 0x1F);
            long res = (int)(val << shift);
            SetReg(dest, res);

            LogInstr("SLLV", $"{src} << {op} -> {val:X16} << {shift:X} -> {res:X16} -> {dest}");
        }
        #endregion

        #region Arithmetic Operations

        void MIPS_ADD(uint instr)
        {
            CPUREG op1 = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG op2 = (CPUREG)((instr & (0x1F << 16)) >> 16);
            CPUREG dest = (CPUREG)((instr & (0x1F << 11)) >> 11);
            int val1 = (int)GetReg(op1);
            int val2 = (int)GetReg(op2);
            int res = val1 + val2;
            SetReg(dest, res);

            LogInstr("ADD", $"{op1} + {op2} -> {val1:X16} + {val2:X16} -> {res:X16} -> {dest}");
        }

        void MIPS_ADDU(uint instr)
        {
            CPUREG op1 = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG op2 = (CPUREG)((instr & (0x1F << 16)) >> 16);
            CPUREG dest = (CPUREG)((instr & (0x1F << 11)) >> 11);
            int val1 = (int)GetReg(op1);
            int val2 = (int)GetReg(op2);
            int res = val1 + val2;
            SetReg(dest, res);

            LogInstr("ADDU", $"{op1} + {op2} -> {val1:X16} + {val2:X16} -> {res:X16} -> {dest}");
        }

        void MIPS_SUBU(uint instr)
        {
            CPUREG op1 = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG op2 = (CPUREG)((instr & (0x1F << 16)) >> 16);
            CPUREG dest = (CPUREG)((instr & (0x1F << 11)) >> 11);
            int val1 = (int)GetReg(op1);
            int val2 = (int)GetReg(op2);
            int res = val1 - val2;
            SetReg(dest, res);

            LogInstr("SUBU", $"{op1} - {op2} -> {val1:X16} - {val2:X16} -> {res:X16} -> {dest}");
        }

        void MIPS_MULTU(uint instr)
        {
            CPUREG op1 = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG op2 = (CPUREG)((instr & (0x1F << 16)) >> 16);
            ulong val1 = (uint)GetReg(op1);
            ulong val2 = (uint)GetReg(op2);
            ulong res = val1 * val2;
            long resLo = (int)res;
            long resHi = (int)(res >> 32);
            lo = resLo;
            hi = resHi;

            LogInstr("MULTU", $"{op1} * {op2} -> {val1:X8} * {val2:X8} -> ({resLo:X16} -> LO | {resHi:X16} -> HI)");
        }

        void MIPS_MULT(uint instr)
        {
            CPUREG op1 = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG op2 = (CPUREG)((instr & (0x1F << 16)) >> 16);
            long val1 = (int)GetReg(op1);
            long val2 = (int)GetReg(op2);
            long res = val1 * val2;
            long resLo = (int)res;
            long resHi = (int)(res >> 32);
            lo = resLo;
            hi = resHi;

            LogInstr("MULT", $"{op1} * {op2} -> {val1:X8} * {val2:X8} -> ({resLo:X16} -> LO | {resHi:X16} -> HI)");
        }

        void MIPS_DIVU(uint instr)
        {
            CPUREG op1 = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG op2 = (CPUREG)((instr & (0x1F << 16)) >> 16);
            ulong val1 = (uint)GetReg(op1);
            ulong val2 = (uint)GetReg(op2);
            long resLo = (long)(val1 / val2);
            long resHi = (long)(val1 % val2);
            lo = resLo;
            hi = resHi;

            LogInstr("DIVU", $"{op1} / {op2} -> {val1:X8} / {val2:X8} -> ({resLo:X16} -> LO | {resHi:X16} -> HI)");
        }

        void MIPS_DADD(uint instr)
        {
            CPUREG op1 = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG op2 = (CPUREG)((instr & (0x1F << 16)) >> 16);
            CPUREG dest = (CPUREG)((instr & (0x1F << 11)) >> 11);
            long val1 = GetReg(op1);
            long val2 = GetReg(op2);
            long res = val1 + val2;
            SetReg(dest, res);

            LogInstr("DADD", $"{op1} + {op2} -> {val1:X16} + {val2:X16} -> {res:X16} -> {dest}");
        }

        #endregion

        #region Control Flow

        void MIPS_JR(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 21)) >> 21);
            branchQueued = 2;
            branchTarget = (ulong)GetReg(src);

            LogInstr("JR", $"{branchTarget:X16} -> PC");
        }

        #endregion

        #region Misc.

        void MIPS_SLT(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG target = (CPUREG)((instr & (0x1F << 16)) >> 16);
            CPUREG dest = (CPUREG)((instr & (0x1F << 11)) >> 11);
            long cp1 = GetReg(src);
            long cp2 = GetReg(target);
            long val = cp1 < cp2 ? 1 : 0;
            SetReg(dest, val);

            LogInstr("SLT", $"{src} < {target} -> {cp1:X16} < {cp2:X16} -> {val} -> {dest}");
        }

        void MIPS_SLTU(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG target = (CPUREG)((instr & (0x1F << 16)) >> 16);
            CPUREG dest = (CPUREG)((instr & (0x1F << 11)) >> 11);
            ulong cp1 = (ulong)GetReg(src);
            ulong cp2 = (ulong)GetReg(target);
            long val = cp1 < cp2 ? 1 : 0;
            SetReg(dest, val);

            LogInstr("SLTU", $"{src} < {target} -> {cp1:X16} < {cp2:X16} -> {val} -> {dest}");
        }

        void MIPS_MFLO(uint instr)
        {
            CPUREG dest = (CPUREG)((instr & (0x1F << 11)) >> 11);
            long val = lo;
            SetReg(dest, val);

            LogInstr("MFLO", $"LO -> {val:X16} -> {dest}");
        }

        void MIPS_MFHI(uint instr)
        {
            CPUREG dest = (CPUREG)((instr & (0x1F << 11)) >> 11);
            long val = hi;
            SetReg(dest, val);

            LogInstr("MFHI", $"HI -> {val:X16} -> {dest}");
        }

        void MIPS_MTLO(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 21)) >> 21);
            long val = GetReg(src);
            lo = val;

            LogInstr("MTLO", $"{src} -> {val:X16} -> LO");
        }

        void MIPS_MTHI(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 21)) >> 21);
            long val = GetReg(src);
            hi = val;

            LogInstr("MTHI", $"{src} -> {val:X16} -> HI");
        }
        #endregion

        #endregion

        #region REGIMM Instructions

        void MIPS_BGEZ(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 21)) >> 21);
            ulong offset = (ulong)(((short)(instr & 0xFFFF)) << 2);
            ulong addr = pc + offset;
            long val = GetReg(src);
            bool cond = val >= 0;
            if (cond)
            {
                branchQueued = 2;
                branchTarget = addr;
            }

            LogInstr("BGEZ", $"{src} >= 0 -> {val:X16} >= 0 -> {(cond ? "" : "No ")}Branch to {addr:X8}");
        }

        void MIPS_BGEZAL(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 21)) >> 21);
            ulong offset = (ulong)(((short)(instr & 0xFFFF)) << 2);
            ulong addr = pc + offset;
            long val = GetReg(src);
            bool cond = val >= 0;
            if (cond)
            {
                branchQueued = 2;
                branchTarget = addr;
            }
            SetReg(CPUREG.RA, (long)pc + 4);

            LogInstr("BGEZAL", $"{src} >= 0 -> {val:X16} >= 0 -> {(cond ? "" : "No ")}Branch to {addr:X8}");
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

        void MIPS_MFC0(uint instr)
        {
            CPUREG dest = (CPUREG)((instr & (0x1F << 16)) >> 16);
            COP0REG src = (COP0REG)((instr & (0x1F << 11)) >> 11);
            SetReg(dest, cop0.GetReg(src));

            LogInstr("MFC0", $"{src} -> {cop0.GetReg(src):X16} -> {dest}");
        }

        void MIPS_CTC1(uint instr)
        {
            uint fcr = (instr & (0x1F << 11)) >> 11;
            CPUREG src = (CPUREG)((instr & (0x1F << 16)) >> 16);
            long val = GetReg(src);
            if (fcr == 31)
                cop1.FCR31 = (int)val;

            LogInstr("CTC1", $"{src} -> {val:X16} -> FCR{fcr}");
        }

        void MIPS_CFC1(uint instr)
        {
            uint fcr = (instr & (0x1F << 11)) >> 11;
            CPUREG dest = (CPUREG)((instr & (0x1F << 16)) >> 16);
            long val = fcr == 0 ? default : cop1.FCR31;
            SetReg(dest, val);

            LogInstr("CFC1", $"FCR{fcr} -> {val:X16} -> {dest}");
        }

        void MIPS_ERET()
        {
            long sr = cop0.GetReg(COP0REG.Status);
            if((sr & 0b100) != 0)
            {
                sr &= ~(0b100);
                cop0.SetReg(COP0REG.Status, sr);
                pc = (ulong)cop0.GetReg(COP0REG.ErrorEPC);
            } 
            else
            {
                sr &= ~(0b10);
                cop0.SetReg(COP0REG.Status, sr);
                pc = (ulong)cop0.GetReg(COP0REG.EPC);
            }
        }

        void MIPS_TLBWI(uint instr)
        {
            LogInstr("TLBWI", "Not yet implemented.");
        }
        #endregion

    }
}
