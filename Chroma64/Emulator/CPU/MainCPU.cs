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
        private ulong hilo;

        private ulong breakpoint = 0;
        private bool debugging = false;

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
                // Instruction Subset Decoders
                { 0, InstrSpecial }, { 1, InstrRegimm }, { 16, InstrCop }, { 17, InstrCop }, { 18, InstrCop },

                // Branch Instructions
                { 4, MIPS_BEQ }, { 5, MIPS_BNE }, { 21, MIPS_BNEL },

                // Load Instructions
                { 15, MIPS_LUI }, { 35, MIPS_LW },

                // Store Instructions
                { 43, MIPS_SW },

                // Arithmetic Operations
                { 9, MIPS_ADDIU }, { 8, MIPS_ADDI },

                // Bitwise Operations
                { 13, MIPS_ORI }, { 12, MIPS_ANDI },

                // Misc.
                { 47, MIPS_CACHE },
            };

            instrsSpecial = new Dictionary<uint, Action<uint>>()
            {
                // Bitwise Operations
                { 36, MIPS_AND }, { 37, MIPS_OR }, { 38, MIPS_XOR }, { 6, MIPS_SRLV }, { 4, MIPS_SLLV },

                // Arithmetic Operations
                { 32, MIPS_ADD }, { 33, MIPS_ADDU }, { 35, MIPS_SUBU }, { 25, MIPS_MULTU },

                // Control Flow
                { 8, MIPS_JR },

                // Misc.
                { 43, MIPS_SLTU }, { 18, MIPS_MFLO },
            };

            instrsRegimm = new Dictionary<uint, Action<uint>>()
            {
                { 17, MIPS_BGEZAL },
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
                if ((pc & 0xFFFFFFFF) == breakpoint)
                    debugging = true;

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
                else
                    LogInstr("NOP", "-");

                // Handle queued branches
                if (branchQueued > 0 && --branchQueued == 0)
                    pc = branchTarget;
            }
        }

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

        #region CPU Register Instructions
        private void SetReg(CPUREG reg, long value)
        {
            if ((((ulong)value & 0xFFFFFFFF00000000) != 0) && (((ulong)value & 0xFFFFFFFF00000000) != 0xFFFFFFFF00000000))
                debugging = true;

            if (reg != CPUREG.ZERO)
                regs[(int)reg] = value;
        }

        private long GetReg(CPUREG reg)
        {
            return regs[(int)reg];
        }

        private int lo
        {
            get { return (int)(hilo & 0xFFFFFFFF); }
            set { hilo = (hilo & 0xFFFFFFFF00000000) | (uint)value; }
        }

        private int hi
        {
            get { return (int)((hilo & 0xFFFFFFFF00000000) >> 32); }
            set { hilo = (hilo & 0xFFFFFFFF) | (((uint)value) << 32); }
        }
        #endregion

        #region Sub-Instruction Decoders
        private void InstrSpecial(uint instr)
        {
            uint opcode = instr & 0x3F;
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
                Log.FatalError($"Unimplemented REGIMM Instruction 0x{instr:X8} [Opcode {opcode}] at PC = 0x{pc:X16}");
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

        // # Instruction Implementations

        #region Normal Instructions

        #region Branch Instructions

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

        #endregion

        #region Load Instructions

        void MIPS_LUI(uint instr)
        {
            CPUREG dest = (CPUREG)((instr & (0x1F << 16)) >> 16);
            long val = (int)((instr & 0xFFFF) << 16);
            SetReg(dest, val);

            LogInstr("LUI", $"{val:X16} -> {dest}");
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

        #endregion

        #region Store Instructions

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

        #endregion

        #region Arithmetic Operations

        void MIPS_ADDIU(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG dest = (CPUREG)((instr & (0x1F << 16)) >> 16);
            long val = (short)(instr & 0xFFFF);
            long regval = GetReg(src);
            SetReg(dest, regval + val);

            LogInstr("ADDIU", $"{src} -> {regval:X16} + {val:X16} -> {regval + val:X16} -> {dest}");
        }

        void MIPS_ADDI(uint instr)
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

        #endregion

        #region Misc.

        void MIPS_CACHE(uint instr)
        {
            LogInstr("CACHE", $"Not yet implemented.");
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
            hilo = res;

            LogInstr("MULTU", $"{op1} * {op2} -> {val1:X8} * {val2:X8} -> {res:X16} -> HILO");
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
        #endregion

        #endregion

        #region REGIMM Instructions

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
        #endregion

    }
}
