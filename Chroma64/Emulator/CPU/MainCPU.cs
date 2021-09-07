using Chroma64.Emulator.Memory;
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

    class MainCPU
    {
        private long[] regs = new long[32];
        private ulong pc = 0xA4000040;

        private long hi;
        private long lo;

        private ulong breakpoint = 0x80090C80;
        private bool debugging = false;

        public COP0 COP0;
        public COP1 COP1;
        public MemoryBus Bus;

        private Dictionary<uint, Action<uint>> instrs = new Dictionary<uint, Action<uint>>();
        private Dictionary<uint, Action<uint>> instrsSpecial = new Dictionary<uint, Action<uint>>();
        private Dictionary<uint, Action<uint>> instrsRegimm = new Dictionary<uint, Action<uint>>();
        private Dictionary<uint, Action<uint>> instrsCOP0 = new Dictionary<uint, Action<uint>>();
        private Dictionary<uint, Action<uint>> instrsTLB = new Dictionary<uint, Action<uint>>();
        private Dictionary<uint, Action<uint>> instrsCOP1 = new Dictionary<uint, Action<uint>>();
        private Dictionary<uint, Action<uint>> instrsFPU = new Dictionary<uint, Action<uint>>();
        private Dictionary<uint, Action<uint>> instrsFPUBranch = new Dictionary<uint, Action<uint>>();
        private Dictionary<uint, Action<uint>> instrsCOPz = new Dictionary<uint, Action<uint>>();

        // Branch Instruction Variables
        private int branchQueued = 0;
        private ulong branchTarget;

        public MainCPU(MemoryBus bus)
        {
            this.Bus = bus;
            COP0 = new COP0(this);
            COP1 = new COP1(this);

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

            instrs = new Dictionary<uint, Action<uint>>()
            {
                // Instruction Subset Decoders
                { 0, InstrSpecial }, { 1, InstrRegimm }, { 16, InstrCop }, { 17, InstrCop }, { 18, InstrCop },

                // Branch Instructions
                { 2, MIPS_J }, { 3, MIPS_JAL }, { 4, MIPS_BEQ }, { 5, MIPS_BNE }, { 6, MIPS_BLEZ }, { 20, MIPS_BEQL }, { 21, MIPS_BNEL }, { 7, MIPS_BGTZ },

                // Load Instructions
                { 15, MIPS_LUI }, { 32, MIPS_LB }, { 36, MIPS_LBU }, { 33, MIPS_LH }, { 37, MIPS_LHU }, { 35, MIPS_LW }, { 39, MIPS_LWU }, { 55, MIPS_LD }, { 34, MIPS_LWL }, { 38, MIPS_LWR },
                { 53, MIPS_LDC1 }, { 49, MIPS_LWC1 },

                // Store Instructions
                { 40, MIPS_SB }, { 41, MIPS_SH }, { 43, MIPS_SW }, { 63, MIPS_SD }, { 42, MIPS_SWL }, { 46, MIPS_SWR },
                { 61, MIPS_SDC1 }, { 57, MIPS_SWC1 },

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
                { 36, MIPS_AND }, { 37, MIPS_OR }, { 38, MIPS_XOR }, { 39, MIPS_NOR }, { 2, MIPS_SRL }, { 3, MIPS_SRA }, { 6, MIPS_SRLV }, { 7, MIPS_SRAV }, { 0, MIPS_SLL }, { 4, MIPS_SLLV }, { 56, MIPS_DSLL }, { 60, MIPS_DSLL32 },

                // Arithmetic Operations
                { 32, MIPS_ADD }, { 33, MIPS_ADDU }, { 35, MIPS_SUBU }, { 25, MIPS_MULTU }, { 24, MIPS_MULT }, { 26, MIPS_DIV }, { 27, MIPS_DIVU }, { 44, MIPS_DADD },

                // Control Flow
                { 8, MIPS_JR }, { 9, MIPS_JALR },

                // Misc.
                { 42, MIPS_SLT }, { 43, MIPS_SLTU }, { 18, MIPS_MFLO }, { 16, MIPS_MFHI }, { 19, MIPS_MTLO }, { 17, MIPS_MTHI },
            };

            instrsRegimm = new Dictionary<uint, Action<uint>>()
            {
                { 0, MIPS_BLTZ }, { 1, MIPS_BGEZ }, { 3, MIPS_BGEZL }, { 17, MIPS_BGEZAL },
            };

            instrsCOP0 = new Dictionary<uint, Action<uint>>()
            {
                { 0, MIPS_MFC0 }, { 4, MIPS_MTC0 },
            };

            instrsTLB = new Dictionary<uint, Action<uint>>()
            {
                { 2, MIPS_TLBWI },
            };

            instrsCOP1 = new Dictionary<uint, Action<uint>>()
            {
                { 0, MIPS_MFC1 }, { 2, MIPS_CFC1 }, { 6, MIPS_CTC1 }, { 4, MIPS_MTC1 },
            };

            instrsFPU = new Dictionary<uint, Action<uint>>()
            {
                { 33, MIPS_CVT_D_FMT }, { 32, MIPS_CVT_S_FMT }, { 37, MIPS_CVT_L_FMT }, { 36, MIPS_CVT_W_FMT },
                { 9, MIPS_TRUNC_L_FMT }, { 13, MIPS_TRUNC_W_FMT },
                { 5, MIPS_ABS_FMT }, { 0, MIPS_ADD_FMT }, { 2, MIPS_MUL_FMT }, { 1, MIPS_SUB_FMT }, { 3, MIPS_DIV_FMT },
                { 6, MIPS_MOV_FMT },
            };
            for (uint i = 0b110000; i < 0x3F; i++)
                instrsFPU.Add(i, MIPS_C_COND_FMT);

            instrsFPUBranch = new Dictionary<uint, Action<uint>>()
            {
                { 0, MIPS_BC1F }, { 1, MIPS_BC1T }, { 2, MIPS_BC1FL }, { 3, MIPS_BC1TL },
            };
        }

        public void Tick(int cycles)
        {
            for (int i = 0; i < cycles; i++)
            {
                CheckBreakpoint();

                // Update VI_CURRENT
                if (Bus.VI.NeedsRender())
                {
                    if (i % (cycles / 262) == 0)
                    {
                        uint cur = Bus.VI.GetRegister(IO.VI.CURRENT_REG) + 2;
                        if (cur == 524)
                            cur = 0;
                        Bus.VI.SetRegister(IO.VI.CURRENT_REG, cur);

                        if (cur / 2 == Bus.VI.GetRegister(IO.VI.INTR_REG) / 2)
                        {
                            Log.Info("Raising VI Interrupt");
                            Bus.MI.SetRegister(IO.MI.INTR_REG, Bus.MI.GetRegister(IO.MI.INTR_REG) | 0b1000);
                        }
                    }
                }
                

                // Tick COP0 registers
                COP0.Tick();

                // Check Interrupts
                if(((COP0.GetReg(COP0REG.Status) & 0b111) == 0b001) && ((COP0.GetReg(COP0REG.Cause) & (COP0.GetReg(COP0REG.Status) & 0xFF00)) != 0))
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
                        CheckInstructionImplemented(instr, opcode, instrs);
                        try
                        {
                            instrs[opcode](instr);
                        }
                        catch(Exception e)
                        {
                            pc -= 4;
                            Log.FatalError($"Unimplemented Instruction 0x{instr:X8} [Opcode {opcode}] at PC = 0x{pc:X16} (Exception: {e.Message})");
                        }
                    }
                }
                else
                    LogInstr("NOP", "-");

                // Handle queued branches
                if (branchQueued > 0 && --branchQueued == 0)
                    pc = branchTarget;
            }
        }

        public void TriggerException(int exceptionCode)
        {
            if (branchQueued > 0)
                COP0.SetReg(COP0REG.Cause, COP0.GetReg(COP0REG.Cause) | (1 << 31));

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

            //Log.Info($"Exception with code {exceptionCode}, jumping to 0x{pc:X8}");

            branchQueued = 0;
        }

        #region Debug Methods
        [Conditional("DEBUG")]
        private void LogInstr(string instr, string msg)
        {
            if (debugging)
            {
                Log.Info($"[PC = 0x{(pc - 4) & 0xFFFFFFFF:X8}] [INSTR:0x{Bus.Read<uint>(pc - 4):X8}] {instr.PadRight(6)} : {msg}");
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
        private void CheckInstructionImplemented(uint instr, uint opcode, Dictionary<uint, Action<uint>> set)
        {
            if (!set.ContainsKey(opcode))
            {
                pc -= 4;
                Log.FatalError($"Unimplemented Instruction 0x{instr:X8} [Opcode {opcode}] at PC = 0x{pc:X16}");
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
            CheckInstructionImplemented(instr, opcode, instrsSpecial);
            instrsSpecial[opcode](instr);
        }

        private void InstrRegimm(uint instr)
        {
            uint opcode = (instr & 0x1F0000) >> 16;
            CheckInstructionImplemented(instr, opcode, instrsRegimm);
            instrsRegimm[opcode](instr);
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
                    CheckInstructionImplemented(instr, cop1opcode, instrsFPU);
                    instrsFPU[cop1opcode](instr);
                }
                else
                {
                    if (maybeOp == 0b01000)
                    {
                        uint fpuBranchOpcode = (instr & (0x3F << 16)) >> 16;
                        CheckInstructionImplemented(instr, fpuBranchOpcode, instrsFPUBranch);
                        instrsFPUBranch[fpuBranchOpcode](instr);
                    }
                    else
                    {
                        CheckInstructionImplemented(instr, maybeOp, instrsCOP1);
                        instrsCOP1[maybeOp](instr);
                    }
                }
            }
            else if (cop == 0b010000)
            {
                if ((instr & 0b11111111111111111111000000) == 0b10000000000000000000000000)
                {
                    uint opcode = instr & 0x3F;
                    CheckInstructionImplemented(instr, opcode, instrsTLB);
                    instrsTLB[opcode](instr);
                }
                else
                {
                    uint opcode = (instr & (0x3F << 21)) >> 21;
                    CheckInstructionImplemented(instr, opcode, instrsCOP0);
                    instrsCOP0[opcode](instr);
                }
            }
            else
            {
                uint opcode = (instr & (0x3F << 21)) >> 21;
                CheckInstructionImplemented(instr, opcode, instrsCOPz);
                instrsCOPz[opcode](instr);
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

            LogInstr("LB", $"[{src}] -> [{baseAddr:X16} + {offset:X4} = {addr:X16}] -> {val:X2} -> {dest}");
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

        #region FPU Loads

        void MIPS_LWC1(uint instr)
        {
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

        void MIPS_JALR(uint instr)
        {
            CPUREG src = (CPUREG)((instr & (0x1F << 11)) >> 11);
            SetReg(CPUREG.RA, (long)pc + 4);
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
            COP0.SetReg(dest, GetReg(src));

            LogInstr("MTC0", $"{src} -> {GetReg(src):X16} -> {dest}");
        }

        void MIPS_MFC0(uint instr)
        {
            CPUREG dest = (CPUREG)((instr & (0x1F << 16)) >> 16);
            COP0REG src = (COP0REG)((instr & (0x1F << 11)) >> 11);
            SetReg(dest, COP0.GetReg(src));

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
            int dest = (int)((instr & (0x1F << 11)) >> 11);
            CPUREG src = (CPUREG)((instr & (0x1F << 16)) >> 16);
            COP1.SetFGR(dest, GetReg(src));

            LogInstr("MTC1", $"{src} -> {GetReg(src):X16} -> {dest}");
        }

        void MIPS_MFC1(uint instr)
        {
            CPUREG dest = (CPUREG)((instr & (0x1F << 16)) >> 16);
            int src = (int)((instr & (0x1F << 11)) >> 11);
            SetReg(dest, COP1.GetFGR<int>(src));

            LogInstr("MFC1", $"FGR{src} -> {COP1.GetFGR<int>(src):X16} -> {dest}");
        }

        void MIPS_CTC1(uint instr)
        {
            uint fcr = (instr & (0x1F << 11)) >> 11;
            CPUREG src = (CPUREG)((instr & (0x1F << 16)) >> 16);
            long val = GetReg(src);
            if (fcr == 31)
                COP1.FCR31 = (int)val;

            LogInstr("CTC1", $"{src} -> {val:X16} -> FCR{fcr}");
        }

        void MIPS_CFC1(uint instr)
        {
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
                default:
                    Log.FatalError($"C.cond.fmt : Unimplemented condition {cond:X1} in instruction {instr:X8}");
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
