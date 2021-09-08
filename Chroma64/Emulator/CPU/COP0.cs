using Chroma64.Util;
using System;
using System.Runtime.CompilerServices;

namespace Chroma64.Emulator.CPU
{
    public enum COP0REG
    {
        Index, Random, EntryLo0, EntryLo1, Context, PageMask, Wired, Seven, BadVAddr, Count, EntryHi, Compare, Status, Cause, EPC, PRId,
        Config, LLAddr, WatchLo, WatchHi, XContext, TwentyOne, TwentyTwo, TwentyThree, TwentyFour, TwentyFive, ParityError, CacheError, TagLo, TagHi, ErrorEPC, ThirtyOne
    }

    class COP0
    {
        public long[] Registers = new long[32];

        private long count = 0;

        private Random random = new Random();
        private MainCPU parent;

        public COP0(MainCPU parent)
        {
            this.parent = parent;
        }

        // TODO: 64 bit regs
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetReg(COP0REG reg, long value)
        {
            // Random
            if (reg == COP0REG.Random)
                return;

            if (reg == COP0REG.Compare)
                Registers[(int)COP0REG.Cause] &= ~(1 << 15);

            Registers[(int)reg] = value & 0xFFFFFFFF;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public long GetReg(COP0REG reg)
        {
            // Random
            if (reg == COP0REG.Random)
                return random.Next((int)GetReg(COP0REG.Wired), 0x1F);

            return Registers[(int)reg] & 0xFFFFFFFF;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Tick()
        {
            Registers[(int)COP0REG.Count] = ((++count) >> 1) & 0xFFFFFFFF;
            if (Registers[(int)COP0REG.Count] == Registers[(int)COP0REG.Compare])
                Registers[(int)COP0REG.Cause] |= 1 << 15;
        }
    }
}
