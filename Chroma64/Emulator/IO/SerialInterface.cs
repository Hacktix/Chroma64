using Chroma64.Emulator.Memory;
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
        public SerialInterface() : base(0x1C) { }
        public new T Read<T>(ulong addr) where T : unmanaged
        {
            // Addresses over 0x1B are unused
            if (addr > 0x1B)
                return default;

            return base.Read<T>(addr);
        }

        public new void Write<T>(ulong addr, T val) where T : unmanaged
        {
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
