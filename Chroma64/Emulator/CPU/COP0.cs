using System;

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
        public long[] Registers = new long[32];

        // TODO: 64 bit regs
        public void SetReg(COP0REG reg, long value)
        {
            // Random
            if (reg == COP0REG.Random)
                return;

            Registers[(int)reg] = value & 0xFFFFFFFF;
        }

        public long GetReg(COP0REG reg)
        {
            // Random
            if (reg == COP0REG.Random)
                return random.Next((int)GetReg(COP0REG.Wired), 0x1F);

            return Registers[(int)reg] & 0xFFFFFFFF;
        }

        public void Tick()
        {

        }
    }
}
