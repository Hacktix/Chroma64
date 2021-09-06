using Chroma64.Emulator.Memory;
using Chroma64.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        public new T Read<T>(ulong addr) where T : unmanaged
        {
            // Addresses over 0x1B are unused
            if (addr > 0x1B)
                return default;

            return base.Read<T>(addr);
        }

        public new void Write<T>(ulong addr, T val) where T : unmanaged
        {
            if (addr >= (ulong)SI.PIF_ADDR_RD64B_REG && addr < (ulong)SI.PIF_ADDR_RD64B_REG + 4)
            {
                ulong dest = (ulong)GetRegister(SI.DRAM_ADDR_REG);
                Log.Info($"SI DMA to RDRAM:{dest:X6}");
                for (ulong i = dest; i < dest + 64; i += sizeof(ulong))
                    bus.Write(0x80000000 + i, ulong.MaxValue);
                bus.MI.SetRegister(MI.INTR_REG, (uint)(bus.MI.GetRegister(MI.INTR_REG) | 0b10));
                SetRegister(SI.STATUS_REG, 1 << 12);
                return;
            }

            if (addr >= (ulong)SI.PIF_ADDR_WR64B_REG && addr < (ulong)SI.PIF_ADDR_WR64B_REG + 4)
            {
                ulong src = (ulong)GetRegister(SI.DRAM_ADDR_REG);

                string data = "";
                for (ulong i = src; i < src + 64; i += sizeof(ulong))
                    data += $"{bus.Read<ulong>(0x80000000 + i):X16}";

                Log.Info($"SI DMA from RDRAM:{src:X6} | Data: ${data}");
                bus.MI.SetRegister(MI.INTR_REG, (uint)(bus.MI.GetRegister(MI.INTR_REG) | 0b10));
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

        public uint GetRegister(SI reg)
        {
            return base.Read<uint>((ulong)reg);
        }

        public void SetRegister(SI reg, uint value)
        {
            base.Write((ulong)reg, value);
        }
    }
}
