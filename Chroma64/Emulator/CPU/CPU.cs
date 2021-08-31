using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chroma64.Emulator.CPU
{
    public enum CPUREG
    {
        ZERO, AT, V0, V1, A0, A1, A2, A3, T0, T1, T2, T3, T4, T5, T6, T7, S0, S1, S2, S3, S4, S5, S6, S7, T8, T9, K0, K1, GP, SP, S8, RA
    }

    class CPU
    {
        private ulong[] regs = new ulong[32];
        private ulong pc = 0xBFC00000;

        private COP0 cop0 = new COP0();

        private void SetReg(CPUREG reg, ulong value)
        {
            if (reg != CPUREG.ZERO)
                regs[(int)reg] = value;
        }

        private ulong GetReg(CPUREG reg)
        {
            return regs[(int)reg];
        }
    }
}
