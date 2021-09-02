using Chroma64.Emulator.Memory;

namespace Chroma64.Emulator.IO
{
    public enum MI
    {
        BASE_REG = 0x0,
        INIT_MODE_REG = 0x0,
        MODE_REG = 0x0,
        VERSION_REG = 0x4,
        NOOP_REG = 0x4,
        INTR_REG = 0x8,
        INTR_MASK_REG = 0xC
    }

    class MIPSInterface : BigEndianMemory
    {
        public MIPSInterface() : base(0x10) { }

        public new T Read<T>(ulong addr) where T : unmanaged
        {
            // Addresses over 0xF are unused
            if (addr > 0xF)
                return default;

            return base.Read<T>(addr);
        }

        public new void Write<T>(ulong addr, T val) where T : unmanaged
        {
            // Addresses over 0xF are unused
            if (addr > 0xF)
                return;

            base.Write(addr, val);
        }

        public uint GetRegister(MI reg)
        {
            return base.Read<uint>((ulong)reg);
        }

        public void SetRegister(MI reg, uint value)
        {
            base.Write((ulong)reg, value);
        }
    }
}
