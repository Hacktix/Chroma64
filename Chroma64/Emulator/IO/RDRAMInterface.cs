using Chroma64.Emulator.Memory;
using Chroma64.Util;

namespace Chroma64.Emulator.IO
{
    public enum RI
    {
        MODE_REG = 0x00,
        CONFIG_REG = 0x04,
        CURRENT_LOAD_REG = 0x08,
        SELECT_REG = 0x0C,
        REFRESH_REG = 0x10,
        COUNT_REG = 0x10,
        LATENCY_REG = 0x14,
        RERROR_REG = 0x18,
        WERROR_REG = 0x1C,
    }

    class RDRAMInterface : BigEndianMemory
    {
        public RDRAMInterface() : base(0x20) { }

        public new T Read<T>(ulong addr) where T : unmanaged
        {
            // RI_WERROR_REG is Write-Only
            if (addr >= (ulong)RI.WERROR_REG && addr < (ulong)(RI.WERROR_REG + 4))
                return default;

            // Addresses over 0x1F are unused
            if (addr > 0x1F)
                return default;

            return base.Read<T>(addr);
        }

        public new void Write<T>(ulong addr, T val) where T : unmanaged
        {
            // RI_RERROR_REG is Read-Only
            if (addr >= (ulong)RI.RERROR_REG && addr < (ulong)(RI.RERROR_REG + 4))
                return;

            // Write to RI_WERROR_REG clears RI_RERROR_REG
            if (addr >= (ulong)RI.WERROR_REG && addr < (ulong)(RI.WERROR_REG + 4))
            {
                base.Write((ulong)RI.RERROR_REG, 0);
                return;
            }

            // Addresses over 0x1F are unused
            if (addr > 0x1F)
                return;

            base.Write<T>(addr, val);
        }

        public uint GetRegister(RI reg)
        {
            return base.Read<uint>((ulong)reg);
        }

        public void SetRegister(RI reg, uint value)
        {
            base.Write((ulong)reg, value);
        }
    }
}
