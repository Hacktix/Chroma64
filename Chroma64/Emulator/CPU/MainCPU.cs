﻿using Chroma.Diagnostics.Logging;
using Chroma64.Emulator.Memory;
using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

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

        private long hi;
        private long lo;

        private ulong breakpoint = 0;
        private bool debugging = false;

        public COP0 COP0;
        public COP1 COP1;
        public MemoryBus Bus;

        private Action<uint>[] instrs = new Action<uint>[0x40];
        private Action<uint>[] instrsSpecial = new Action<uint>[0x40];
        private Action<uint>[] instrsRegimm = new Action<uint>[0x40];
        private Action<uint>[] instrsCOP0 = new Action<uint>[0x40];
        private Action<uint>[] instrsTLB = new Action<uint>[0x40];
        private Action<uint>[] instrsCOP1 = new Action<uint>[0x40];
        private Action<uint>[] instrsFPU = new Action<uint>[0x40];
        private Action<uint>[] instrsFPUBranch = new Action<uint>[0x40];
        private Action<uint>[] instrsCOPz = new Action<uint>[0x40];

        // Branch Instruction Variables
        private int branchQueued = 0;
        private ulong branchTarget;

        private Log log = LogManager.GetForCurrentAssembly();

        public MainCPU(MemoryBus bus)
        {
            this.Bus = bus;
            COP0 = new COP0(this);
            COP1 = new COP1(this);
            bus.CPU = this;

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
            COP0.Registers[1] = 0x0000001F;
            COP0.Registers[12] = 0x34000000;
            COP0.Registers[15] = 0x00000B00;
            COP0.Registers[16] = 0x7006E463;

            // # Initializing Instruction LUT

            #region Normal Instructions
            // Instruction Subset Decoders
            instrs[0] = InstrSpecial;
            instrs[1] = InstrRegimm;
            instrs[16] = instrs[17] = instrs[18] = InstrCop;

            // Branch Instructions
            instrs[2] = MIPS_J;
            instrs[3] = MIPS_JAL;
            instrs[4] = MIPS_BEQ;
            instrs[5] = MIPS_BNE;
            instrs[6] = MIPS_BLEZ;
            instrs[7] = MIPS_BGTZ;
            instrs[20] = MIPS_BEQL;
            instrs[21] = MIPS_BNEL;
            instrs[22] = MIPS_BLEZL;
            instrs[23] = MIPS_BGTZL;

            // Load Instructions
            instrs[15] = MIPS_LUI;
            instrs[26] = MIPS_LDL;
            instrs[27] = MIPS_LDR;
            instrs[32] = MIPS_LB;
            instrs[33] = MIPS_LH;
            instrs[34] = MIPS_LWL;
            instrs[35] = MIPS_LW;
            instrs[36] = MIPS_LBU;
            instrs[37] = MIPS_LHU;
            instrs[38] = MIPS_LWR;
            instrs[39] = MIPS_LWU;
            instrs[49] = MIPS_LWC1;
            instrs[53] = MIPS_LDC1;
            instrs[55] = MIPS_LD;

            // Store Instructions
            instrs[40] = MIPS_SB;
            instrs[41] = MIPS_SH;
            instrs[42] = MIPS_SWL;
            instrs[43] = MIPS_SW;
            instrs[46] = MIPS_SWR;
            instrs[57] = MIPS_SWC1;
            instrs[61] = MIPS_SDC1;
            instrs[63] = MIPS_SD;

            // Arithmetic Operations
            instrs[8] = MIPS_ADDI;
            instrs[9] = MIPS_ADDIU;
            instrs[24] = MIPS_DADDI;
            instrs[25] = MIPS_DADDIU;

            // Bitwise Operations
            instrs[12] = MIPS_ANDI;
            instrs[13] = MIPS_ORI;
            instrs[14] = MIPS_XORI;

            // Misc.
            instrs[10] = MIPS_SLTI;
            instrs[11] = MIPS_SLTIU;
            instrs[47] = MIPS_CACHE;
            #endregion

            #region Special Instructions
            // Bitwise Operations
            instrsSpecial[0] = MIPS_SLL;
            instrsSpecial[2] = MIPS_SRL;
            instrsSpecial[3] = MIPS_SRA;
            instrsSpecial[4] = MIPS_SLLV;
            instrsSpecial[6] = MIPS_SRLV;
            instrsSpecial[7] = MIPS_SRAV;
            instrsSpecial[36] = MIPS_AND;
            instrsSpecial[37] = MIPS_OR;
            instrsSpecial[38] = MIPS_XOR;
            instrsSpecial[39] = MIPS_NOR;
            instrsSpecial[56] = MIPS_DSLL;
            instrsSpecial[58] = MIPS_DSRL;
            instrsSpecial[60] = MIPS_DSLL32;
            instrsSpecial[63] = MIPS_DSRA32;

            // Arithmetic Operations
            instrsSpecial[24] = MIPS_MULT;
            instrsSpecial[25] = MIPS_MULTU;
            instrsSpecial[26] = MIPS_DIV;
            instrsSpecial[27] = MIPS_DIVU;
            instrsSpecial[29] = MIPS_DMULTU;
            instrsSpecial[31] = MIPS_DDIVU;
            instrsSpecial[32] = MIPS_ADD;
            instrsSpecial[33] = MIPS_ADDU;
            instrsSpecial[34] = MIPS_SUB;
            instrsSpecial[35] = MIPS_SUBU;
            instrsSpecial[44] = MIPS_DADD;
            instrsSpecial[45] = MIPS_DADDU;
            instrsSpecial[47] = MIPS_DSUBU;

            // Control Flow
            instrsSpecial[8] = MIPS_JR;
            instrsSpecial[9] = MIPS_JALR;

            // Misc.
            instrsSpecial[16] = MIPS_MFHI;
            instrsSpecial[17] = MIPS_MTHI;
            instrsSpecial[18] = MIPS_MFLO;
            instrsSpecial[19] = MIPS_MTLO;
            instrsSpecial[42] = MIPS_SLT;
            instrsSpecial[43] = MIPS_SLTU;
            #endregion

            #region REGIMM Instructions
            instrsRegimm[0] = MIPS_BLTZ;
            instrsRegimm[1] = MIPS_BGEZ;
            instrsRegimm[2] = MIPS_BLTZL;
            instrsRegimm[3] = MIPS_BGEZL;
            instrsRegimm[17] = MIPS_BGEZAL;
            #endregion

            #region COP0 Instructions
            instrsCOP0[0] = MIPS_MFC0;
            instrsCOP0[4] = MIPS_MTC0;
            #endregion

            #region TLB Instructions
            instrsTLB[2] = MIPS_TLBWI;
            #endregion

            #region COP1 Instructions
            instrsCOP1[0] = MIPS_MFC1;
            instrsCOP1[2] = MIPS_CFC1;
            instrsCOP1[4] = MIPS_MTC1;
            instrsCOP1[6] = MIPS_CTC1;
            #endregion

            #region FPU Instructions
            instrsFPU[0] = MIPS_ADD_FMT;
            instrsFPU[1] = MIPS_SUB_FMT;
            instrsFPU[2] = MIPS_MUL_FMT;
            instrsFPU[3] = MIPS_DIV_FMT;
            instrsFPU[5] = MIPS_ABS_FMT;
            instrsFPU[6] = MIPS_MOV_FMT;
            instrsFPU[9] = MIPS_TRUNC_L_FMT;
            instrsFPU[13] = MIPS_TRUNC_W_FMT;
            instrsFPU[32] = MIPS_CVT_S_FMT;
            instrsFPU[33] = MIPS_CVT_D_FMT;
            instrsFPU[36] = MIPS_CVT_W_FMT;
            instrsFPU[37] = MIPS_CVT_L_FMT;

            for (uint i = 0b110000; i < 0x3F; i++)
                instrsFPU[i] = MIPS_C_COND_FMT;
            #endregion

            #region FPU Branch Instructions
            instrsFPUBranch[0] = MIPS_BC1F;
            instrsFPUBranch[1] = MIPS_BC1T;
            instrsFPUBranch[2] = MIPS_BC1FL;
            instrsFPUBranch[3] = MIPS_BC1TL;
            #endregion
        }

        public void Tick(int cycles)
        {
            for (int i = 0; i < cycles; i++)
            {
                // DEBUG Build Only: Check if Breakpoint is reached
                CheckBreakpoint();

                // Tick other hardware registers
                COP0.Tick();
                if ((i & 1) == 0)
                    Bus.VI.Tick();

                // Check Interrupts
                if (((COP0.GetReg(COP0REG.Status) & 0b111) == 0b001) && ((COP0.GetReg(COP0REG.Cause) & (COP0.GetReg(COP0REG.Status) & 0xFF00)) != 0))
                    TriggerException(0);

                // Fetch & increment PC
                uint instr = Bus.Read<uint>(pc);
                pc += 4;

                // Decode opcode & execute
                if (instr != 0)
                {
                    if (instr == 0x42000018)
                        MIPS_ERET();
                    else
                    {
                        uint opcode = (instr & 0xFC000000) >> 26;
#if DEBUG
                        try
                        {
                            instrs[opcode](instr);
                        }
                        catch (Exception e)
                        {
                            pc -= 4;
                            log.Error($"Exception @ Instruction 0x{instr:X8} [Opcode {opcode}] at PC = 0x{pc:X16}");
                            log.Exception(e);
                            Console.ReadKey();
                            Environment.Exit(-1);
                        }
#else
                        instrs[opcode](instr);
#endif
                    }
                }
                else
                    LogInstr("NOP", "-");

                // Handle queued branches
                if (branchQueued > 0 && --branchQueued == 0)
                    pc = branchTarget;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void TriggerException(int exceptionCode, uint copNo = 0)
        {
            if (branchQueued > 0)
                COP0.SetReg(COP0REG.Cause, COP0.GetReg(COP0REG.Cause) | (1 << 31));

            COP0.SetReg(COP0REG.Cause, (COP0.GetReg(COP0REG.Cause) & ~(3 << 28)) | ((copNo & 3) << 28));

            if ((COP0.GetReg(COP0REG.Status) & 0b10) == 0)
            {
                if (branchQueued > 0)
                    COP0.SetReg(COP0REG.EPC, (long)(pc - 4));
                else
                    COP0.SetReg(COP0REG.EPC, (long)pc);
            }
            COP0.Registers[(int)COP0REG.Status] |= 2;

            COP0.SetReg(COP0REG.Cause, (COP0.GetReg(COP0REG.Cause) & (~0b1111100)) | (uint)((exceptionCode & 0x1F) << 2));

            // TODO: Differentiate between TLB and other exceptions
            if ((COP0.GetReg(COP0REG.Status) & (1 << 22)) == 0)
                pc = 0x80000180;
            else
                pc = 0xBFC00380;

            branchQueued = 0;
        }

        #region Debug Methods
        [Conditional("DEBUG_LOG")]
        private void LogInstr(string instr, string msg)
        {
            if (debugging)
            {
                log.Info($"[PC = 0x{(pc - 4) & 0xFFFFFFFF:X8}] [INSTR:0x{Bus.Read<uint>(pc - 4):X8}] {instr.PadRight(6)} : {msg}");
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
        #endregion

        #region CPU Register Instructions
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SetReg(CPUREG reg, long value)
        {
            if (reg != CPUREG.ZERO)
                regs[(int)reg] = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long GetReg(CPUREG reg)
        {
            return regs[(int)reg];
        }
        #endregion

        #region Sub-Instruction Decoders
        private void InstrSpecial(uint instr)
        {
            uint opcode = instr & 0x3F;
#if DEBUG
            try
            {
                instrsSpecial[opcode](instr);
            }
            catch (Exception e)
            {
                pc -= 4;
                log.Error($"Exception @ Special Instruction 0x{instr:X8} [Opcode {opcode}] at PC = 0x{pc:X16}");
                log.Exception(e);
                Console.ReadKey();
                Environment.Exit(-1);
            }
#else
            instrsSpecial[opcode](instr);
#endif
        }

        private void InstrRegimm(uint instr)
        {
            uint opcode = (instr & 0x1F0000) >> 16;
#if DEBUG
            try
            {
                instrsRegimm[opcode](instr);
            }
            catch (Exception e)
            {
                pc -= 4;
                log.Error($"Exception @ REGIMM Instruction 0x{instr:X8} [Opcode {opcode}] at PC = 0x{pc:X16}");
                log.Exception(e);
                Console.ReadKey();
                Environment.Exit(-1);
            }
#else
            instrsRegimm[opcode](instr);
#endif
        }

        private void InstrCop(uint instr)
        {
            uint cop = (uint)((instr & (0x3F << 26)) >> 26);

            if (cop == 0b010001)
            {
                uint maybeOp = (instr & (0x1F << 21)) >> 21;
                if (maybeOp == 0b10000 || maybeOp == 0b10101 || maybeOp == 0b10100 || maybeOp == 0b10001)
                {
                    uint cop1opcode = instr & 0x3F;
#if DEBUG
                    try
                    {
                        instrsFPU[cop1opcode](instr);
                    }
                    catch (Exception e)
                    {
                        pc -= 4;
                        log.Error($"Exception @ FPU Instruction 0x{instr:X8} [Opcode {cop1opcode}] at PC = 0x{pc:X16}");
                        log.Exception(e);
                        Console.ReadKey();
                        Environment.Exit(-1);
                    }
#else
                    instrsFPU[cop1opcode](instr);
#endif
                }
                else
                {
                    if (maybeOp == 0b01000)
                    {
                        uint fpuBranchOpcode = (instr & (0x3F << 16)) >> 16;
#if DEBUG
                        try
                        {
                            instrsFPUBranch[fpuBranchOpcode](instr);
                        }
                        catch (Exception e)
                        {
                            pc -= 4;
                            log.Error($"Exception @ FPU Branch Instruction 0x{instr:X8} [Opcode {fpuBranchOpcode}] at PC = 0x{pc:X16}");
                            log.Exception(e);
                            Console.ReadKey();
                            Environment.Exit(-1);
                        }
#else
                        instrsFPUBranch[fpuBranchOpcode](instr);
#endif
                    }
                    else
                    {
#if DEBUG
                        try
                        {
                            instrsCOP1[maybeOp](instr);
                        }
                        catch (Exception e)
                        {
                            pc -= 4;
                            log.Error($"Exception @ COP1 Instruction 0x{instr:X8} [Opcode {maybeOp}] at PC = 0x{pc:X16}");
                            log.Exception(e);
                            Console.ReadKey();
                            Environment.Exit(-1);
                        }
#else
                        instrsCOP1[maybeOp](instr);
#endif
                    }
                }
            }
            else if (cop == 0b010000)
            {
                if ((instr & 0b11111111111111111111000000) == 0b10000000000000000000000000)
                {
                    uint opcode = instr & 0x3F;
#if DEBUG
                    try
                    {
                        instrsTLB[opcode](instr);
                    }
                    catch (Exception e)
                    {
                        pc -= 4;
                        log.Error($"Exception @ TLB Instruction 0x{instr:X8} [Opcode {opcode}] at PC = 0x{pc:X16}");
                        log.Exception(e);
                        Console.ReadKey();
                        Environment.Exit(-1);
                    }
#else
                    instrsTLB[opcode](instr);
#endif
                }
                else
                {
                    uint opcode = (instr & (0x3F << 21)) >> 21;
#if DEBUG
                    try
                    {
                        instrsCOP0[opcode](instr);
                    }
                    catch (Exception e)
                    {
                        pc -= 4;
                        log.Error($"Exception @ COP0 Instruction 0x{instr:X8} [Opcode {opcode}] at PC = 0x{pc:X16}");
                        log.Exception(e);
                        Console.ReadKey();
                        Environment.Exit(-1);
                    }
#else
                    instrsCOP0[opcode](instr);
#endif
                }
            }
            else
            {
                uint opcode = (instr & (0x3F << 21)) >> 21;
#if DEBUG
                try
                {
                    instrsCOPz[opcode](instr);
                }
                catch (Exception e)
                {
                    pc -= 4;
                    log.Error($"Exception @ COPz Instruction 0x{instr:X8} [Opcode {opcode}] at PC = 0x{pc:X16}");
                    log.Exception(e);
                    Console.ReadKey();
                    Environment.Exit(-1);
                }
#else
                instrsCOPz[opcode](instr);
#endif
            }


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

        void MIPS_BGTZL(uint instr)
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
            else
                pc += 4;

            LogInstr("BGTZL", $"{src} > 0 -> {val:X16} > 0 -> {(cond ? "" : "No ")}Branch to {addr:X8}");
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

        void MIPS_BLEZL(uint instr)
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
            else
                pc += 4;

            LogInstr("BLEZL", $"{src} <= 0 -> {val:X16} <= 0 -> {(cond ? "" : "No ")}Branch to {addr:X8}");
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
            sbyte val = Bus.Read<sbyte>(addr);
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
            byte val = Bus.Read<byte>(addr);
            SetReg(dest, val);

            LogInstr("LBU", $"[{src}] -> [{baseAddr:X16} + {offset:X4} = {addr:X16}] -> {val:X2} -> {dest}");
        }

        void MIPS_LH(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG dest = (CPUREG)((instr & (0x1F << 16)) >> 16);
            short offset = (short)(instr & 0xFFFF);
            long baseAddr = GetReg(src);
            ulong addr = (ulong)(baseAddr + offset);
            short val = Bus.Read<short>(addr);
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
            ushort val = Bus.Read<ushort>(addr);
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
            int val = Bus.Read<int>(addr);
            SetReg(dest, val);

            LogInstr("LW", $"[{src}] -> [{baseAddr:X16} + {offset:X4} = {addr:X16}] -> {val:X8} -> {dest}");
        }

        void MIPS_LWU(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG dest = (CPUREG)((instr & (0x1F << 16)) >> 16);
            short offset = (short)(instr & 0xFFFF);
            long baseAddr = GetReg(src);
            ulong addr = (ulong)(baseAddr + offset);
            uint val = Bus.Read<uint>(addr);
            SetReg(dest, val);

            LogInstr("LWU", $"[{src}] -> [{baseAddr:X16} + {offset:X4} = {addr:X16}] -> {val:X8} -> {dest}");
        }

        void MIPS_LD(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG dest = (CPUREG)((instr & (0x1F << 16)) >> 16);
            short offset = (short)(instr & 0xFFFF);
            long baseAddr = GetReg(src);
            ulong addr = (ulong)(baseAddr + offset);
            long val = Bus.Read<long>(addr);
            SetReg(dest, val);

            LogInstr("LD", $"[{src}] -> [{baseAddr:X16} + {offset:X4} = {addr:X16}] -> {val:X16} -> {dest}");
        }

        void MIPS_LWL(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG dest = (CPUREG)((instr & (0x1F << 16)) >> 16);
            short offset = (short)(instr & 0xFFFF);
            long baseAddr = GetReg(src);
            ulong addr = (ulong)(baseAddr + offset);

            uint word = Bus.Read<uint>(addr - (addr % 4));
            uint sword = word << (int)(8 * (addr % 4));
            long res = (int)(sword | (GetReg(dest) & ((1 << (8 * (int)(addr % 4))) - 1)));
            SetReg(dest, res);

            LogInstr("LWL", $"[{src}] -> [{baseAddr:X16} + {offset:X4} = {addr:X16}] -> {sword:X8} -> {res:X16} -> {dest}");
        }

        void MIPS_LWR(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG dest = (CPUREG)((instr & (0x1F << 16)) >> 16);
            short offset = (short)(instr & 0xFFFF);
            long baseAddr = GetReg(src);
            ulong addr = (ulong)(baseAddr + offset);

            uint word = Bus.Read<uint>(addr - (addr % 4));
            uint sword = word >> (int)(8 * (3 - (addr % 4)));
            long res = (int)(sword | (GetReg(dest) & ((addr % 4) == 3 ? 0 : (0xFFFFFFFF << (8 * ((int)(addr % 4) + 1))))));
            SetReg(dest, res);

            LogInstr("LWR", $"[{src}] -> [{baseAddr:X16} + {offset:X4} = {addr:X16}] -> {sword:X8} -> {res:X16} -> {dest}");
        }

        void MIPS_LDL(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG dest = (CPUREG)((instr & (0x1F << 16)) >> 16);
            short offset = (short)(instr & 0xFFFF);
            long baseAddr = GetReg(src);
            ulong addr = (ulong)(baseAddr + offset);

            ulong dword = Bus.Read<ulong>(addr - (addr % 8));
            ulong sdword = dword << (int)(8 * (addr % 8));
            long res = (long)sdword | (GetReg(dest) & ((1L << (8 * ((int)(addr % 8)))) - 1));
            SetReg(dest, res);

            LogInstr("LDL", $"[{src}] -> [{baseAddr:X16} + {offset:X4} = {addr:X16}] -> {sdword:X8} -> {res:X16} -> {dest}");
        }

        void MIPS_LDR(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG dest = (CPUREG)((instr & (0x1F << 16)) >> 16);
            short offset = (short)(instr & 0xFFFF);
            long baseAddr = GetReg(src);
            ulong addr = (ulong)(baseAddr + offset);

            ulong dword = Bus.Read<ulong>(addr - (addr % 8));
            ulong sdword = dword >> (int)(8 * (7 - (addr % 8)));
            long res = (long)sdword | (long)((ulong)GetReg(dest) & ((addr % 8) == 7 ? 0 : (0xFFFFFFFFFFFFFFFF << (8 * ((int)(addr % 8) + 1)))));
            SetReg(dest, res);

            LogInstr("LDR", $"[{src}] -> [{baseAddr:X16} + {offset:X4} = {addr:X16}] -> {sdword:X16} -> {res:X16} -> {dest}");
        }

        #region FPU Loads

        void MIPS_LWC1(uint instr)
        {
            if ((COP0.GetReg(COP0REG.Status) & (1 << 29)) == 0)
            {
                TriggerException(11, 1);
                return;
            }
            CPUREG src = (CPUREG)((instr & (0x1F << 21)) >> 21);
            int dest = (int)((instr & (0x1F << 16)) >> 16);
            short offset = (short)(instr & 0xFFFF);
            long baseAddr = GetReg(src);
            ulong addr = (ulong)(baseAddr + offset);
            int val = Bus.Read<int>(addr);
            COP1.SetFGR(dest, val);

            LogInstr("LWC1", $"[{src}] -> [{baseAddr:X16} + {offset:X4} = {addr:X16}] -> {val:X16} -> FGR{dest}");
        }

        void MIPS_LDC1(uint instr)
        {
            if ((COP0.GetReg(COP0REG.Status) & (1 << 29)) == 0)
            {
                TriggerException(11, 1);
                return;
            }
            CPUREG src = (CPUREG)((instr & (0x1F << 21)) >> 21);
            int dest = (int)((instr & (0x1F << 16)) >> 16);
            short offset = (short)(instr & 0xFFFF);
            long baseAddr = GetReg(src);
            ulong addr = (ulong)(baseAddr + offset);
            long val = Bus.Read<long>(addr);
            COP1.SetFGR(dest, val);

            LogInstr("LDC1", $"[{src}] -> [{baseAddr:X16} + {offset:X4} = {addr:X16}] -> {val:X16} -> FGR{dest}");
        }

        #endregion

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
            Bus.Write(addr, val);

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
            Bus.Write(addr, val);

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
            Bus.Write(addr, val);

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
            Bus.Write(addr, val);

            LogInstr("SD", $"{src} -> {val:X16} -> [{dest}] -> [{baseAddr:X16} + {offset:X4} = {addr:X16}]");
        }

        void MIPS_SWL(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG dest = (CPUREG)((instr & (0x1F << 16)) >> 16);
            short offset = (short)(instr & 0xFFFF);
            long baseAddr = GetReg(src);
            ulong addr = (ulong)(baseAddr + offset);

            uint val = (uint)(GetReg(dest) & 0xFFFFFFFF);
            uint sval = val >> 8 * (int)(addr % 4);
            uint word = Bus.Read<uint>(addr - (addr % 4));
            uint mword = word & (uint)(0xFFFFFFFFL << (8 * ((int)(4 - (addr % 4)))));
            Bus.Write(addr - (addr % 4), sval | mword);

            LogInstr("SWL", $"{src} -> {sval | mword:X16} -> [{dest}] -> [{baseAddr:X16} + {offset:X4} = {addr:X16}]");
        }

        void MIPS_SWR(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG dest = (CPUREG)((instr & (0x1F << 16)) >> 16);
            short offset = (short)(instr & 0xFFFF);
            long baseAddr = GetReg(src);
            ulong addr = (ulong)(baseAddr + offset);

            uint val = (uint)(GetReg(dest) & 0xFFFFFFFF);
            uint sval = val << (8 * ((int)(3 - (addr % 4))));
            uint word = Bus.Read<uint>(addr - (addr % 4));
            uint mword = word & (uint)((1L << (8 * ((int)(3 - addr % 4)))) - 1);
            Bus.Write(addr - (addr % 4), sval | mword);

            LogInstr("SWR", $"{src} -> {sval | mword:X16} -> [{dest}] -> [{baseAddr:X16} + {offset:X4} = {addr:X16}]");
        }

        #region FPU Stores

        void MIPS_SWC1(uint instr)
        {
            if ((COP0.GetReg(COP0REG.Status) & (1 << 29)) == 0)
            {
                TriggerException(11, 1);
                return;
            }
            int src = (int)((instr & (0x1F << 16)) >> 16);
            CPUREG dest = (CPUREG)((instr & (0x1F << 21)) >> 21);
            short offset = (short)(instr & 0xFFFF);
            long baseAddr = GetReg(dest);
            ulong addr = (ulong)(baseAddr + offset);
            int val = COP1.GetFGR<int>(src);
            Bus.Write(addr, val);

            LogInstr("SWC1", $"FGR{src} -> {val:X16} -> [{dest}] -> [{baseAddr:X16} + {offset:X4} = {addr:X16}]");
        }

        void MIPS_SDC1(uint instr)
        {
            if ((COP0.GetReg(COP0REG.Status) & (1 << 29)) == 0)
            {
                TriggerException(11, 1);
                return;
            }
            int src = (int)((instr & (0x1F << 16)) >> 16);
            CPUREG dest = (CPUREG)((instr & (0x1F << 21)) >> 21);
            short offset = (short)(instr & 0xFFFF);
            long baseAddr = GetReg(dest);
            ulong addr = (ulong)(baseAddr + offset);
            long val = COP1.GetFGR<long>(src);
            Bus.Write(addr, val);

            LogInstr("SDC1", $"FGR{src} -> {val:X16} -> [{dest}] -> [{baseAddr:X16} + {offset:X4} = {addr:X16}]");
        }

        #endregion

        #endregion

        #region Arithmetic Operations

        void MIPS_ADDIU(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG dest = (CPUREG)((instr & (0x1F << 16)) >> 16);
            long val = (short)(instr & 0xFFFF);
            long regval = GetReg(src);
            long res = (int)(regval + val);
            SetReg(dest, res);

            LogInstr("ADDIU", $"{src} -> {regval:X16} + {val:X16} -> {res:X16} -> {dest}");
        }

        void MIPS_ADDI(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG dest = (CPUREG)((instr & (0x1F << 16)) >> 16);
            long val = (short)(instr & 0xFFFF);
            long regval = GetReg(src);
            long res = (int)(regval + val);
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

        void MIPS_DADDIU(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG dest = (CPUREG)((instr & (0x1F << 16)) >> 16);
            long val = (short)(instr & 0xFFFF);
            long regval = GetReg(src);
            SetReg(dest, regval + val);

            LogInstr("DADDIU", $"{src} -> {regval:X16} + {val:X16} -> {regval + val:X16} -> {dest}");
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

        void MIPS_NOR(uint instr)
        {
            CPUREG op1 = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG op2 = (CPUREG)((instr & (0x1F << 16)) >> 16);
            CPUREG dest = (CPUREG)((instr & (0x1F << 11)) >> 11);
            long val1 = GetReg(op1);
            long val2 = GetReg(op2);
            long res = (~val1) & (~val2);
            SetReg(dest, res);

            LogInstr("NOR", $"~{op1} & ~{op2} -> ~{val1:X16} & ~{val2:X16} -> {res:X16} -> {dest}");
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

        void MIPS_SRAV(uint instr)
        {
            CPUREG op = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG src = (CPUREG)((instr & (0x1F << 16)) >> 16);
            CPUREG dest = (CPUREG)((instr & (0x1F << 11)) >> 11);
            int val = (int)GetReg(src);
            int shift = (int)(GetReg(op) & 0x1F);
            long res = val >> shift;
            SetReg(dest, res);

            LogInstr("SRAV", $"{src} >> {op} -> {val:X16} >> {shift:X} -> {res:X16} -> {dest}");
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

        void MIPS_DSLL(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 16)) >> 16);
            CPUREG dest = (CPUREG)((instr & (0x1F << 11)) >> 11);
            ulong val = (ulong)GetReg(src);
            int shift = (int)(instr & (0x1F << 6)) >> 6;
            long res = (long)(val << shift);
            SetReg(dest, res);

            LogInstr("DSLL", $"{src} << {shift} -> {val:X16} << {shift:X} -> {res:X16} -> {dest}");
        }

        void MIPS_DSLL32(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 16)) >> 16);
            CPUREG dest = (CPUREG)((instr & (0x1F << 11)) >> 11);
            ulong val = (ulong)GetReg(src);
            int shift = (int)(((instr & (0x1F << 6)) >> 6) + 32);
            long res = (long)(val << shift);
            SetReg(dest, res);

            LogInstr("DSLL32", $"{src} << {shift} -> {val:X16} << {shift:X} -> {res:X16} -> {dest}");
        }

        void MIPS_DSRA32(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 16)) >> 16);
            CPUREG dest = (CPUREG)((instr & (0x1F << 11)) >> 11);
            long val = GetReg(src);
            int shift = (int)(((instr & (0x1F << 6)) >> 6) + 32);
            long res = val >> shift;
            SetReg(dest, res);

            LogInstr("DSRA32", $"{src} << {shift} -> {val:X16} << {shift:X} -> {res:X16} -> {dest}");
        }

        void MIPS_DSRL(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 16)) >> 16);
            CPUREG dest = (CPUREG)((instr & (0x1F << 11)) >> 11);
            ulong val = (ulong)GetReg(src);
            int shift = (int)(instr & (0x1F << 6)) >> 6;
            long res = (long)(val >> shift);
            SetReg(dest, res);

            LogInstr("DSRL", $"{src} >> {shift} -> {val:X16} >> {shift:X} -> {res:X16} -> {dest}");
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
            long res = (int)(val1 + val2);
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
            long res = (int)(val1 + val2);
            SetReg(dest, res);

            LogInstr("ADDU", $"{op1} + {op2} -> {val1:X16} + {val2:X16} -> {res:X16} -> {dest}");
        }

        void MIPS_SUB(uint instr)
        {
            CPUREG op1 = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG op2 = (CPUREG)((instr & (0x1F << 16)) >> 16);
            CPUREG dest = (CPUREG)((instr & (0x1F << 11)) >> 11);
            int val1 = (int)GetReg(op1);
            int val2 = (int)GetReg(op2);
            long res = (int)(val1 - val2);
            SetReg(dest, res);

            LogInstr("SUBU", $"{op1} - {op2} -> {val1:X16} - {val2:X16} -> {res:X16} -> {dest}");
        }

        void MIPS_SUBU(uint instr)
        {
            CPUREG op1 = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG op2 = (CPUREG)((instr & (0x1F << 16)) >> 16);
            CPUREG dest = (CPUREG)((instr & (0x1F << 11)) >> 11);
            int val1 = (int)GetReg(op1);
            int val2 = (int)GetReg(op2);
            long res = (int)(val1 - val2);
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

        void MIPS_DIV(uint instr)
        {
            CPUREG op1 = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG op2 = (CPUREG)((instr & (0x1F << 16)) >> 16);
            long val1 = (int)GetReg(op1);
            long val2 = (int)GetReg(op2);
            long resLo = val2 == 0 ? (val1 < 0 ? 1 : -1) : (val1 / val2);
            long resHi = val2 == 0 ? val1 : (val1 % val2);
            lo = resLo;
            hi = resHi;

            LogInstr("DIV", $"{op1} / {op2} -> {val1:X8} / {val2:X8} -> ({resLo:X16} -> LO | {resHi:X16} -> HI)");
        }

        void MIPS_DIVU(uint instr)
        {
            CPUREG op1 = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG op2 = (CPUREG)((instr & (0x1F << 16)) >> 16);
            ulong val1 = (uint)GetReg(op1);
            ulong val2 = (uint)GetReg(op2);
            long resLo = val2 == 0 ? long.MaxValue : (long)(val1 / val2);
            long resHi = val2 == 0 ? (long)val1 : (long)(val1 % val2);
            lo = resLo;
            hi = resHi;

            LogInstr("DIVU", $"{op1} / {op2} -> {val1:X8} / {val2:X8} -> ({resLo:X16} -> LO | {resHi:X16} -> HI)");
        }

        void MIPS_DMULTU(uint instr)
        {
            CPUREG op1 = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG op2 = (CPUREG)((instr & (0x1F << 16)) >> 16);
            ulong val1 = (ulong)GetReg(op1);
            ulong val2 = (ulong)GetReg(op2);
            ulong resLo;
            hi = (long)Math.BigMul(val1, val2, out resLo);
            lo = (long)resLo;

            LogInstr("DMULTU", $"{op1} * {op2} -> {val1:X16} * {val2:X16} -> ({resLo:X16} -> LO | {hi:X16} -> HI)");
        }

        void MIPS_DDIVU(uint instr)
        {
            CPUREG op1 = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG op2 = (CPUREG)((instr & (0x1F << 16)) >> 16);
            ulong val1 = (ulong)GetReg(op1);
            ulong val2 = (ulong)GetReg(op2);
            ulong resLo = val2 == 0 ? ulong.MaxValue : (val1 / val2);
            ulong resHi = val2 == 0 ? val1 : (val1 % val2);
            lo = (long)resLo;
            hi = (long)resHi;

            LogInstr("DDIVU", $"{op1} / {op2} -> {val1:X8} / {val2:X8} -> ({resLo:X16} -> LO | {resHi:X16} -> HI)");
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

        void MIPS_DADDU(uint instr)
        {
            CPUREG op1 = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG op2 = (CPUREG)((instr & (0x1F << 16)) >> 16);
            CPUREG dest = (CPUREG)((instr & (0x1F << 11)) >> 11);
            long val1 = GetReg(op1);
            long val2 = GetReg(op2);
            long res = val1 + val2;
            SetReg(dest, res);

            LogInstr("DADDU", $"{op1} + {op2} -> {val1:X16} + {val2:X16} -> {res:X16} -> {dest}");
        }

        void MIPS_DSUBU(uint instr)
        {
            CPUREG op1 = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG op2 = (CPUREG)((instr & (0x1F << 16)) >> 16);
            CPUREG dest = (CPUREG)((instr & (0x1F << 11)) >> 11);
            long val1 = GetReg(op1);
            long val2 = GetReg(op2);
            long res = val1 - val2;
            SetReg(dest, res);

            LogInstr("DSUBU", $"{op1} + {op2} -> {val1:X16} - {val2:X16} -> {res:X16} -> {dest}");
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

        void MIPS_JALR(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 21)) >> 21);
            CPUREG dest = (CPUREG)((instr & (0x1F << 11)) >> 11);
            SetReg(dest, (long)pc + 4);
            branchQueued = 2;
            branchTarget = (ulong)GetReg(src);

            LogInstr("JALR", $"{branchTarget:X16} -> PC");
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

        void MIPS_BLTZ(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 21)) >> 21);
            ulong offset = (ulong)(((short)(instr & 0xFFFF)) << 2);
            ulong addr = pc + offset;
            long val = GetReg(src);
            bool cond = val < 0;
            if (cond)
            {
                branchQueued = 2;
                branchTarget = addr;
            }

            LogInstr("BLTZ", $"{src} < 0 -> {val:X16} >= 0 -> {(cond ? "" : "No ")}Branch to {addr:X8}");
        }

        void MIPS_BLTZL(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 21)) >> 21);
            ulong offset = (ulong)(((short)(instr & 0xFFFF)) << 2);
            ulong addr = pc + offset;
            long val = GetReg(src);
            bool cond = val < 0;
            if (cond)
            {
                branchQueued = 2;
                branchTarget = addr;
            }
            else
                pc += 4;

            LogInstr("BLTZL", $"{src} < 0 -> {val:X16} >= 0 -> {(cond ? "" : "No ")}Branch to {addr:X8}");
        }

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

        void MIPS_BGEZL(uint instr)
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
            else
                pc += 4;

            LogInstr("BGEZL", $"{src} >= 0 -> {val:X16} >= 0 -> {(cond ? "" : "No ")}Branch to {addr:X8}");
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

        #region COP0 Instructions

        void MIPS_MTC0(uint instr)
        {
            COP0REG dest = (COP0REG)((instr & (0x1F << 11)) >> 11);
            CPUREG src = (CPUREG)((instr & (0x1F << 16)) >> 16);
            COP0.SetReg(dest, (int)GetReg(src));

            LogInstr("MTC0", $"{src} -> {GetReg(src):X16} -> {dest}");
        }

        void MIPS_MFC0(uint instr)
        {
            CPUREG dest = (CPUREG)((instr & (0x1F << 16)) >> 16);
            COP0REG src = (COP0REG)((instr & (0x1F << 11)) >> 11);
            SetReg(dest, (int)COP0.GetReg(src));

            LogInstr("MFC0", $"{src} -> {COP0.GetReg(src):X16} -> {dest}");
        }

        #endregion

        #region TLB Instructions

        void MIPS_TLBWI(uint instr)
        {
            LogInstr("TLBWI", "Not yet implemented.");
        }

        #endregion

        #region COP1 Instructions

        void MIPS_MTC1(uint instr)
        {
            if ((COP0.GetReg(COP0REG.Status) & (1 << 29)) == 0)
            {
                TriggerException(11, 1);
                return;
            }
            int dest = (int)((instr & (0x1F << 11)) >> 11);
            CPUREG src = (CPUREG)((instr & (0x1F << 16)) >> 16);
            COP1.SetFGR(dest, (int)GetReg(src));

            LogInstr("MTC1", $"{src} -> {GetReg(src):X16} -> {dest}");
        }

        void MIPS_MFC1(uint instr)
        {
            if ((COP0.GetReg(COP0REG.Status) & (1 << 29)) == 0)
            {
                TriggerException(11, 1);
                return;
            }
            CPUREG dest = (CPUREG)((instr & (0x1F << 16)) >> 16);
            int src = (int)((instr & (0x1F << 11)) >> 11);
            SetReg(dest, COP1.GetFGR<int>(src));

            LogInstr("MFC1", $"FGR{src} -> {COP1.GetFGR<int>(src):X16} -> {dest}");
        }

        void MIPS_CTC1(uint instr)
        {
            if ((COP0.GetReg(COP0REG.Status) & (1 << 29)) == 0)
            {
                TriggerException(11, 1);
                return;
            }
            uint fcr = (instr & (0x1F << 11)) >> 11;
            CPUREG src = (CPUREG)((instr & (0x1F << 16)) >> 16);
            long val = GetReg(src);
            if (fcr == 31)
                COP1.FCR31 = (int)val;

            LogInstr("CTC1", $"{src} -> {val:X16} -> FCR{fcr}");
        }

        void MIPS_CFC1(uint instr)
        {
            if ((COP0.GetReg(COP0REG.Status) & (1 << 29)) == 0)
            {
                TriggerException(11, 1);
                return;
            }
            uint fcr = (instr & (0x1F << 11)) >> 11;
            CPUREG dest = (CPUREG)((instr & (0x1F << 16)) >> 16);
            long val = fcr == 0 ? default : COP1.FCR31;
            SetReg(dest, val);

            LogInstr("CFC1", $"FCR{fcr} -> {val:X16} -> {dest}");
        }

        #endregion

        #region FPU Instructions

        void MIPS_CVT_D_FMT(uint instr)
        {
            int src = (int)((instr & (0x1F << 11)) >> 11);
            int dest = (int)((instr & (0x1F << 16)) >> 16);
            int fmt = (int)((instr & (0x1F << 21)) >> 21);
            switch (fmt)
            {
                case 0b10000:
                    COP1.CVT_D_S(src, dest);
                    break;
                case 0b10100:
                    COP1.CVT_D_W(src, dest);
                    break;
                case 0b10101:
                    COP1.CVT_D_L(src, dest);
                    break;
            }
        }

        void MIPS_CVT_S_FMT(uint instr)
        {
            int src = (int)((instr & (0x1F << 11)) >> 11);
            int dest = (int)((instr & (0x1F << 16)) >> 16);
            int fmt = (int)((instr & (0x1F << 21)) >> 21);
            switch (fmt)
            {
                case 0b10100:
                    COP1.CVT_S_W(src, dest);
                    break;
                case 0b10101:
                    COP1.CVT_S_L(src, dest);
                    break;
                case 0b10001:
                    COP1.CVT_S_D(src, dest);
                    break;
            }
        }

        void MIPS_CVT_L_FMT(uint instr)
        {
            int src = (int)((instr & (0x1F << 11)) >> 11);
            int dest = (int)((instr & (0x1F << 16)) >> 16);
            int fmt = (int)((instr & (0x1F << 21)) >> 21);
            switch (fmt)
            {
                case 0b10000:
                    COP1.CVT_L_S(src, dest);
                    break;
                case 0b10001:
                    COP1.CVT_L_D(src, dest);
                    break;
            }
        }

        void MIPS_CVT_W_FMT(uint instr)
        {
            int src = (int)((instr & (0x1F << 11)) >> 11);
            int dest = (int)((instr & (0x1F << 16)) >> 16);
            int fmt = (int)((instr & (0x1F << 21)) >> 21);
            switch (fmt)
            {
                case 0b10000:
                    COP1.CVT_W_S(src, dest);
                    break;
                case 0b10001:
                    COP1.CVT_W_D(src, dest);
                    break;
            }
        }

        void MIPS_ABS_FMT(uint instr)
        {
            int src = (int)((instr & (0x1F << 11)) >> 11);
            int dest = (int)((instr & (0x1F << 16)) >> 16);
            int fmt = (int)((instr & (0x1F << 21)) >> 21);
            switch (fmt)
            {
                case 0b10001:
                    COP1.ABS_D(src, dest);
                    break;
                case 0b10000:
                    COP1.ABS_S(src, dest);
                    break;
            }
        }

        void MIPS_ADD_FMT(uint instr)
        {
            int dest = (int)((instr & (0x1F << 6)) >> 6);
            int op1 = (int)((instr & (0x1F << 11)) >> 11);
            int op2 = (int)((instr & (0x1F << 16)) >> 16);
            int fmt = (int)((instr & (0x1F << 21)) >> 21);
            switch (fmt)
            {
                case 0b10001:
                    COP1.ADD_D(op1, op2, dest);
                    break;
                case 0b10000:
                    COP1.ADD_S(op1, op2, dest);
                    break;
            }
        }

        void MIPS_SUB_FMT(uint instr)
        {
            int dest = (int)((instr & (0x1F << 6)) >> 6);
            int op1 = (int)((instr & (0x1F << 11)) >> 11);
            int op2 = (int)((instr & (0x1F << 16)) >> 16);
            int fmt = (int)((instr & (0x1F << 21)) >> 21);
            switch (fmt)
            {
                case 0b10001:
                    COP1.SUB_D(op1, op2, dest);
                    break;
                case 0b10000:
                    COP1.SUB_S(op1, op2, dest);
                    break;
            }
        }

        void MIPS_MUL_FMT(uint instr)
        {
            int dest = (int)((instr & (0x1F << 6)) >> 6);
            int op1 = (int)((instr & (0x1F << 11)) >> 11);
            int op2 = (int)((instr & (0x1F << 16)) >> 16);
            int fmt = (int)((instr & (0x1F << 21)) >> 21);
            switch (fmt)
            {
                case 0b10001:
                    COP1.MUL_D(op1, op2, dest);
                    break;
                case 0b10000:
                    COP1.MUL_S(op1, op2, dest);
                    break;
            }
        }

        void MIPS_DIV_FMT(uint instr)
        {
            int dest = (int)((instr & (0x1F << 6)) >> 6);
            int op1 = (int)((instr & (0x1F << 11)) >> 11);
            int op2 = (int)((instr & (0x1F << 16)) >> 16);
            int fmt = (int)((instr & (0x1F << 21)) >> 21);
            switch (fmt)
            {
                case 0b10001:
                    COP1.DIV_D(op1, op2, dest);
                    break;
                case 0b10000:
                    COP1.DIV_S(op1, op2, dest);
                    break;
            }
        }

        void MIPS_TRUNC_L_FMT(uint instr)
        {
            int dest = (int)((instr & (0x1F << 6)) >> 6);
            int src = (int)((instr & (0x1F << 11)) >> 11);
            int fmt = (int)((instr & (0x1F << 21)) >> 21);
            switch (fmt)
            {
                case 0b10001:
                    COP1.TRUNC_L_D(src, dest);
                    break;
                case 0b10000:
                    COP1.TRUNC_L_S(src, dest);
                    break;
            }
        }

        void MIPS_TRUNC_W_FMT(uint instr)
        {
            int dest = (int)((instr & (0x1F << 6)) >> 6);
            int src = (int)((instr & (0x1F << 11)) >> 11);
            int fmt = (int)((instr & (0x1F << 21)) >> 21);
            switch (fmt)
            {
                case 0b10001:
                    COP1.TRUNC_W_D(src, dest);
                    break;
                case 0b10000:
                    COP1.TRUNC_W_S(src, dest);
                    break;
            }
        }

        void MIPS_C_COND_FMT(uint instr)
        {
            int cond = (int)(instr & 0xF);
            int op1 = (int)((instr & (0x1F << 11)) >> 11);
            int op2 = (int)((instr & (0x1F << 16)) >> 16);
            int fmt = (int)((instr & (0x1F << 21)) >> 21);
            switch (cond)
            {
                case 0x2:
                    switch (fmt)
                    {
                        case 0b10001:
                            COP1.C_EQ_D(op1, op2);
                            break;
                        case 0b10000:
                            COP1.C_EQ_S(op1, op2);
                            break;
                    }
                    break;
                case 0xE:
                    switch (fmt)
                    {
                        case 0b10001:
                            COP1.C_LE_D(op1, op2);
                            break;
                        case 0b10000:
                            COP1.C_LE_S(op1, op2);
                            break;
                    }
                    break;
                case 0xC:
                    switch (fmt)
                    {
                        case 0b10001:
                            COP1.C_LT_D(op1, op2);
                            break;
                        case 0b10000:
                            COP1.C_LT_S(op1, op2);
                            break;
                    }
                    break;
                default:
                    log.Error($"C.cond.fmt : Unimplemented condition {cond:X2} in instruction {instr:X8}");
                    break;
            }
        }

        void MIPS_MOV_FMT(uint instr)
        {
            int dest = (int)((instr & (0x1F << 6)) >> 6);
            int src = (int)((instr & (0x1F << 11)) >> 11);
            int fmt = (int)((instr & (0x1F << 21)) >> 21);
            COP1.SetFGR(dest, COP1.GetFGR<ulong>(src));
        }

        #endregion

        #region FPU Branch Instructions

        void MIPS_BC1T(uint instr)
        {
            if ((COP0.GetReg(COP0REG.Status) & (1 << 29)) == 0)
            {
                TriggerException(11, 1);
                return;
            }
            ulong offset = (ulong)(((short)(instr & 0xFFFF)) << 2);
            ulong addr = pc + offset;
            bool cond = COP1.GetCondition();
            if (cond)
            {
                branchQueued = 2;
                branchTarget = addr;
            }

            LogInstr("BC1T", $"{(cond ? "" : "No ")}Branch to {addr:X8}");
        }

        void MIPS_BC1TL(uint instr)
        {
            if ((COP0.GetReg(COP0REG.Status) & (1 << 29)) == 0)
            {
                TriggerException(11, 1);
                return;
            }
            ulong offset = (ulong)(((short)(instr & 0xFFFF)) << 2);
            ulong addr = pc + offset;
            bool cond = COP1.GetCondition();
            if (cond)
            {
                branchQueued = 2;
                branchTarget = addr;
            }
            else
                pc += 4;

            LogInstr("BC1TL", $"{(cond ? "" : "No ")}Branch to {addr:X8}");
        }

        void MIPS_BC1F(uint instr)
        {
            if ((COP0.GetReg(COP0REG.Status) & (1 << 29)) == 0)
            {
                TriggerException(11, 1);
                return;
            }
            ulong offset = (ulong)(((short)(instr & 0xFFFF)) << 2);
            ulong addr = pc + offset;
            bool cond = !COP1.GetCondition();
            if (cond)
            {
                branchQueued = 2;
                branchTarget = addr;
            }

            LogInstr("BC1F", $"{(cond ? "" : "No ")}Branch to {addr:X8}");
        }

        void MIPS_BC1FL(uint instr)
        {
            if ((COP0.GetReg(COP0REG.Status) & (1 << 29)) == 0)
            {
                TriggerException(11, 1);
                return;
            }
            ulong offset = (ulong)(((short)(instr & 0xFFFF)) << 2);
            ulong addr = pc + offset;
            bool cond = !COP1.GetCondition();
            if (cond)
            {
                branchQueued = 2;
                branchTarget = addr;
            }
            else
                pc += 4;

            LogInstr("BC1FL", $"{(cond ? "" : "No ")}Branch to {addr:X8}");
        }

        #endregion

        #endregion

        void MIPS_ERET()
        {
            long sr = COP0.GetReg(COP0REG.Status);
            if ((sr & 0b100) != 0)
            {
                sr &= ~(0b100);
                COP0.SetReg(COP0REG.Status, sr);
                pc = (ulong)COP0.GetReg(COP0REG.ErrorEPC);
            }
            else
            {
                sr &= ~(0b10);
                COP0.SetReg(COP0REG.Status, sr);
                pc = (ulong)COP0.GetReg(COP0REG.EPC);
            }

            LogInstr("ERET", $"{pc:X16} -> PC");
        }

    }
}
