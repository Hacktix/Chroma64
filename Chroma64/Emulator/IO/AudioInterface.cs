using Chroma64.Emulator.Memory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chroma64.Emulator.IO
{
    public enum AI
    {
        BASE_REG = 0x00,
        DRAM_ADDR_REG = 0x00,
        LEN_REG = 0x04,
        CONTROL_REG = 0x08,
        STATUS_REG = 0x0C,
        DACRATE_REG = 0x10,
        BITRATE_REG = 0x14,
    }

    class AudioInterface : BigEndianMemory
    {
        public AudioInterface() : base(0x18) { }
        public new T Read<T>(ulong addr) where T : unmanaged
        {
            // Addresses over 0x17 are unused
            if (addr > 0x17)
                return default;

            return base.Read<T>(addr);
        }

        public new void Write<T>(ulong addr, T val) where T : unmanaged
        {
            // Addresses over 0x17 are unused
            if (addr > 0x17)
                return;

            base.Write<T>(addr, val);
        }

        public uint GetRegister(AI reg)
        {
            return base.Read<uint>((ulong)reg);
        }

        public void SetRegister(AI reg, uint value)
        {
            base.Write((ulong)reg, value);
        }
    }
}
