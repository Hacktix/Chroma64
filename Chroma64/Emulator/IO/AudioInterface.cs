using Chroma64.Emulator.Memory;
using System.Runtime.CompilerServices;

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
        public AudioInterface() : base(0x18)
        {
            SetRegister(AI.STATUS_REG, 0xC0000001);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public new T Read<T>(ulong addr) where T : unmanaged
        {
            // Addresses over 0x17 are unused
            if (addr > 0x17)
                return default;

            return base.Read<T>(addr);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public new void Write<T>(ulong addr, T val) where T : unmanaged
        {
            if (addr >= (ulong)AI.STATUS_REG && addr < (ulong)AI.STATUS_REG + 4)
                return;

            // Addresses over 0x17 are unused
            if (addr > 0x17)
                return;

            base.Write<T>(addr, val);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint GetRegister(AI reg)
        {
            return base.Read<uint>((ulong)reg);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetRegister(AI reg, uint value)
        {
            base.Write((ulong)reg, value);
        }
    }
}
