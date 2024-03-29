﻿using Chroma.Graphics;
using Chroma64.Emulator.Memory;
using System;
using System.Runtime.CompilerServices;

namespace Chroma64.Emulator.IO
{
    public enum VI
    {
        BASE_REG = 0x00, STATUS_REG = 0x00, CONTROL_REG = 0x00,
        ORIGIN_REG = 0x04, DRAM_ADDR_REG = 0x04,
        WIDTH_REG = 0x08, H_WIDTH_REG = 0x08,
        INTR_REG = 0x0C, V_INTR_REG = 0x0C,
        CURRENT_REG = 0x10, V_CURRENT_LINE_REG = 0x10,
        BURST_REG = 0x14, TIMING_REG = 0x14,
        V_SYNC_REG = 0x18,
        H_SYNC_REG = 0x1C,
        LEAP_REG = 0x20, H_SYNC_LEAP_REG = 0x20,
        H_START_REG = 0x24, H_VIDEO_REG = 0x24,
        V_START_REG = 0x28, V_VIDEO_REG = 0x28,
        V_BURST_REG = 0x2C,
        X_SCALE_REG = 0x30,
        Y_SCALE_REG = 0x34,
    }

    class VideoInterface : BigEndianMemory
    {
        private static readonly int VI_CURRENT_UPDATE_FREQ = 1562500 / 524;

        private MemoryBus bus;
        private int viCurrentUpdate = VI_CURRENT_UPDATE_FREQ;

        public VideoInterface(MemoryBus bus) : base(0x38)
        {
            this.bus = bus;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public new T Read<T>(ulong addr) where T : unmanaged
        {
            // Addresses over 0x37 are unused
            if (addr > 0x37)
                return default;

            return base.Read<T>(addr);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public new void Write<T>(ulong addr, T val) where T : unmanaged
        {
            if (addr >= (ulong)VI.CURRENT_REG && addr < (ulong)VI.CURRENT_REG + 4)
            {
                bus.MI.SetRegister(MI.INTR_REG, (uint)(bus.MI.GetRegister(MI.INTR_REG) & ~0b1000));
                return;
            }

            // Addresses over 0x37 are unused
            if (addr > 0x37)
                return;

            base.Write<T>(addr, val);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public uint GetRegister(VI reg)
        {
            return base.Read<uint>((ulong)reg);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetRegister(VI reg, uint value)
        {
            base.Write((ulong)reg, value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Tick()
        {
            // Update VI_CURRENT
            if (--viCurrentUpdate == 0)
            {
                viCurrentUpdate = VI_CURRENT_UPDATE_FREQ;

                uint cur = GetRegister(VI.CURRENT_REG) + 2;
                if (cur == 524)
                    cur = 0;
                SetRegister(VI.CURRENT_REG, cur);

                if (cur / 2 == GetRegister(VI.INTR_REG) / 2)
                    bus.MI.SetRegister(MI.INTR_REG, bus.MI.GetRegister(MI.INTR_REG) | 0b1000);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool NeedsRender()
        {
            return (GetRegister(VI.CONTROL_REG) & 0b11) > 1;
        }

        public void SetFramebuffer(ref Texture tex)
        {
            int displayMode = (int)(GetRegister(VI.CONTROL_REG) & 0b11);

            int width = (int)(GetRegister(VI.WIDTH_REG) & 0xFFF);
            int yScale = (int)(GetRegister(VI.Y_SCALE_REG) & 0xFFF);
            int height = ((15 * yScale) / 64);

            int origin = (int)(GetRegister(VI.DRAM_ADDR_REG) & 0x3FFFFF);
            switch (displayMode)
            {
                case 3:
                    byte[] bpp32 = new byte[width * height * 4];
                    Array.Copy(bus.RDRAM.Bytes, bus.RDRAM.Bytes.Length - origin - bpp32.Length, bpp32, 0, bpp32.Length);
                    Array.Reverse(bpp32);

                    if (tex == null || tex.Width != width || tex.Height != height)
                        tex = new Texture(width, height, PixelFormat.RGBA);
                    tex.SetPixelData(bpp32);
                    break;
                case 2:
                    byte[] bpp16 = new byte[width * height * 3];
                    byte[] tmpData = new byte[width * height * 2];
                    Array.Copy(bus.RDRAM.Bytes, bus.RDRAM.Bytes.Length - origin - tmpData.Length, tmpData, 0, tmpData.Length);
                    Array.Reverse(tmpData);

                    int dataCnt = 0;
                    int zeroCnt = 0;
                    for (int i = 0; i < tmpData.Length; i += 2)
                    {
                        short color = (short)((tmpData[i] << 8) | tmpData[i + 1]);
                        if (color != 0)
                            zeroCnt++;
                        bpp16[dataCnt++] = (byte)(255 * ((double)((color & (0x1F << 11)) >> 11) / 0x1F));
                        bpp16[dataCnt++] = (byte)(255 * ((double)((color & (0x1F << 6)) >> 6) / 0x1F));
                        bpp16[dataCnt++] = (byte)(255 * ((double)((color & (0x1F << 1)) >> 1) / 0x1F));
                    }

                    if (tex == null || tex.Width != width || tex.Height != height)
                        tex = new Texture(width, height, PixelFormat.RGB);
                    tex.SetPixelData(bpp16);
                    break;
            }
        }
    }
}
