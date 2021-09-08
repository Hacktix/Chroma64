using Chroma64.Emulator.Memory;
using System.Runtime.CompilerServices;

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
        private uint intr_mask = 0;
        private MemoryBus bus;

        public MIPSInterface(MemoryBus bus) : base(0x10) {
            this.bus = bus;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public new unsafe T Read<T>(ulong addr) where T : unmanaged
        {
            // MI_INTR_MASK_REG
            if (addr >= 0xC && addr <= 0xF)
                fixed (uint* ptr = &intr_mask) return *(T*)ptr;

            // Addresses over 0xF are unused
            if (addr > 0xF)
                return default;

            return base.Read<T>(addr);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public new unsafe void Write<T>(ulong addr, T val) where T : unmanaged
        {
            // MI_INTR_MASK_REG
            if (addr >= 0xC && addr <= 0xF)
            {
                base.Write(addr, val);
                uint maskChange = base.Read<uint>(0xC);
                intr_mask = 0;
                for (int i = 0; i < 6; i++)
                {
                    if ((maskChange & 2) != 0)
                        intr_mask |= (uint)(1 << i);
                    maskChange >>= 2;
                }
                UpdateInterruptState();
            }

            // Addresses over 0xF are unused
            if (addr > 0xF)
                return;

            base.Write(addr, val);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint GetRegister(MI reg)
        {
            return base.Read<uint>((ulong)reg);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetRegister(MI reg, uint value)
        {
            base.Write((ulong)reg, value);

            if (reg == MI.INTR_REG)
                UpdateInterruptState();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UpdateInterruptState()
        {
            uint intr = GetRegister(MI.INTR_REG);

            if ((intr & intr_mask) != 0)
                bus.CPU.COP0.SetReg(CPU.COP0REG.Cause, bus.CPU.COP0.GetReg(CPU.COP0REG.Cause) | (1 << 10));
            else
                bus.CPU.COP0.SetReg(CPU.COP0REG.Cause, bus.CPU.COP0.GetReg(CPU.COP0REG.Cause) & ~(1 << 10));
        }
    }
}
