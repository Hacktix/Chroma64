using Chroma64.Emulator.Memory;
using System;
using System.Runtime.CompilerServices;

namespace Chroma64.Emulator.IO
{
    public enum PI
    {
        DRAM_ADDR_REG = 0x00,
        CART_ADDR_REG = 0x04,
        RD_LEN_REG = 0x08,
        WR_LEN_REG = 0x0C,
        STATUS_REG = 0x10,
        BSD_DOM1_LAT_REG = 0x14,
        DOMAIN1_REG = 0x14,
        BSD_DOM1_PWD_REG = 0x18,
        BSD_DOM1_PGS_REG = 0x1C,
        BSD_DOM1_RLS_REG = 0x20,
        BSD_DOM2_LAT_REG = 0x24,
        DOMAIN2_REG = 0x24,
        BSD_DOM2_PWD_REG = 0x28,
        BSD_DOM2_PGS_REG = 0x2C,
        BSD_DOM2_RLS_REG = 0x30,
    }

    class PeripheralInterface : BigEndianMemory
    {
        private MemoryBus bus;

        public PeripheralInterface(MemoryBus bus) : base(0x34)
        {
            this.bus = bus;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public new T Read<T>(ulong addr) where T : unmanaged
        {
            if (addr >= (ulong)PI.STATUS_REG && addr < (ulong)(PI.STATUS_REG + 4))
                return default;

            // Addresses over 0x33 are unused
            if (addr > 0x33)
                return default;

            return base.Read<T>(addr);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public new void Write<T>(ulong addr, T val) where T : unmanaged
        {
            if (addr >= (ulong)PI.STATUS_REG && addr < (ulong)(PI.STATUS_REG + 4))
            {
                base.Write<T>(addr, val);
                if ((base.Read<int>((ulong)PI.STATUS_REG) & 2) != 0)
                    bus.MI.SetRegister(MI.INTR_REG, (uint)(bus.MI.GetRegister(MI.INTR_REG) & ~0b10000));
            }

            if (addr >= (ulong)PI.WR_LEN_REG && addr < (ulong)(PI.WR_LEN_REG + 4))
            {
                base.Write<T>(addr, val);
                int destAddr = (int)(GetRegister(PI.DRAM_ADDR_REG) & 0x7FFFFF);
                int srcAddr = (int)((GetRegister(PI.CART_ADDR_REG) & 0xFFFFFFFF) - 0x10000000);
                int len = (int)((GetRegister(PI.WR_LEN_REG) & 0x7FFFFF) + 1);

                Array.Copy(bus.ROM.Bytes, bus.ROM.Bytes.Length - srcAddr - len,
                    bus.RDRAM.Bytes, bus.RDRAM.Bytes.Length - destAddr - len, len);

                bus.MI.SetRegister(MI.INTR_REG, (uint)(bus.MI.GetRegister(MI.INTR_REG) | 0b10000));
                return;
            }

            // Addresses over 0x33 are unused
            if (addr > 0x33)
                return;

            base.Write<T>(addr, val);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint GetRegister(PI reg)
        {
            return base.Read<uint>((ulong)reg);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetRegister(PI reg, uint value)
        {
            base.Write((ulong)reg, value);
        }

    }
}
