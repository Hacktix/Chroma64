using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chroma64.Emulator.CPU
{
    public enum COP0REG
    {
        Index, Random, EntryLo0, EntryLo1, Context, PageMask, Wired, Seven, BadVAddr, Count, EntryHi, Compare, Status, Cause, EPC, PRId,
        Config, LLAddr, WatchLo, WatchHi, XContext, TwentyOne, TwentyTwo, TwentyThree, TwentyFour, TwentyFive, ParityError, CacheError, TagLo, TagHi, ErrorEPC, ThirtyOne
    }

    class COP0
    {
        private Random random = new Random();
        public ulong[] Registers = new ulong[32];

        // TODO: 64 bit regs
        public void SetReg(COP0REG reg, ulong value)
        {
            // Random
            if (reg == COP0REG.Random)
                return;

            Registers[(int)reg] = value & 0xFFFFFFFF;
        }

        public ulong GetReg(COP0REG reg)
        {
            // Random
            if (reg == COP0REG.Random)
                return (ulong)random.Next((int)GetReg(COP0REG.Wired), 0x1F);

            return Registers[(int)reg] & 0xFFFFFFFF;
        }

        public void Tick()
        {

        }
    }
}
