﻿using Chroma64.Emulator.Memory;
using Chroma64.Util;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        public PeripheralInterface() : base(0x34) { }

        public new T Read<T>(ulong addr) where T : unmanaged
        {
            // Addresses over 0x33 are unused
            if (addr > 0x33)
                return default;

            return base.Read<T>(addr);
        }

        public new void Write<T>(ulong addr, T val) where T : unmanaged
        {
            Log.Info($"PI Write to {addr:X16}");

            // Addresses over 0x33 are unused
            if (addr > 0x33)
                return;

            base.Write<T>(addr, val);
        }

        public uint GetRegister(PI reg)
        {
            return base.Read<uint>((ulong)reg);
        }

        public void SetRegister(PI reg, uint value)
        {
            base.Write((ulong)reg, value);
        }

    }
}