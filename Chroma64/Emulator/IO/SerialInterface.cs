using Chroma64.Emulator.Memory;
using System;
using System.Runtime.CompilerServices;

namespace Chroma64.Emulator.IO
{
    public enum SI
    {
        BASE_REG = 0x00,
        DRAM_ADDR_REG = 0x00,
        PIF_ADDR_RD64B_REG = 0x04,
        PIF_ADDR_WR64B_REG = 0x10,
        STATUS_REG = 0x18,
    }

    class SerialInterface : BigEndianMemory
    {
        private MemoryBus bus;

        public SerialInterface(MemoryBus bus) : base(0x1C)
        {
            this.bus = bus;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public new T Read<T>(ulong addr) where T : unmanaged
        {
            // Addresses over 0x1B are unused
            if (addr > 0x1B)
                return default;

            return base.Read<T>(addr);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public new void Write<T>(ulong addr, T val) where T : unmanaged
        {
            if (addr >= (ulong)SI.PIF_ADDR_RD64B_REG && addr < (ulong)SI.PIF_ADDR_RD64B_REG + 4)
            {
                ulong dest = (ulong)(GetRegister(SI.DRAM_ADDR_REG) & 0xFFFFFF);
                bus.PIF.Exec();
                Array.Copy(bus.PIF.Bytes, 0, bus.RDRAM.Bytes, bus.RDRAM.Bytes.Length - (int)dest - 64, 64);
                bus.MI.SetRegister(MI.INTR_REG, bus.MI.GetRegister(MI.INTR_REG) | 0b10);
                SetRegister(SI.STATUS_REG, 1 << 12);
                return;
            }

            if (addr >= (ulong)SI.PIF_ADDR_WR64B_REG && addr < (ulong)SI.PIF_ADDR_WR64B_REG + 4)
            {
                ulong src = GetRegister(SI.DRAM_ADDR_REG) & 0xFFFFFF;
                Array.Copy(bus.RDRAM.Bytes, bus.RDRAM.Bytes.Length - (int)src - 64, bus.PIF.Bytes, 0, 64);
                bus.MI.SetRegister(MI.INTR_REG, bus.MI.GetRegister(MI.INTR_REG) | 0b10);
                SetRegister(SI.STATUS_REG, 1 << 12);
                return;
            }

            if (addr >= (ulong)SI.STATUS_REG && addr < (ulong)SI.STATUS_REG + 4)
            {
                bus.MI.SetRegister(MI.INTR_REG, (uint)(bus.MI.GetRegister(MI.INTR_REG) & ~0b10));
                SetRegister(SI.STATUS_REG, 0);
                return;
            }

            // Addresses over 0x1B are unused
            if (addr > 0x1B)
                return;

            base.Write(addr, val);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint GetRegister(SI reg)
        {
            return base.Read<uint>((ulong)reg);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetRegister(SI reg, uint value)
        {
            base.Write((ulong)reg, value);
        }
    }
}
