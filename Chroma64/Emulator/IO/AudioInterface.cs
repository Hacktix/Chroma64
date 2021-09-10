using Chroma.Audio;
using Chroma.Audio.Sources;
using Chroma64.Emulator.Memory;
using System;
using System.Runtime.CompilerServices;
using System.Threading;

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
        private Waveform wave;
        private MemoryBus bus;

        private int dmaCount = 0;
        private uint[] dmaAddr = new uint[2];
        private int[] dmaLen = new int[2];

        public AudioInterface(MemoryBus bus) : base(0x18)
        {
            this.bus = bus;
            wave = new Waveform(new AudioFormat(SampleFormat.S16, ByteOrder.BigEndian), PushSamples);
            wave.Play();
        }

        private void PushSamples(Span<byte> buffer, AudioFormat format)
        {
            buffer.Clear();

            if (dmaCount > 0)
            {
                int len = Math.Min(buffer.Length, dmaLen[0]);
                byte[] data = new byte[len];
                Array.Copy(bus.RDRAM.Bytes, bus.RDRAM.Bytes.Length - (int)dmaAddr[0] - len, data, 0, len);
                Array.Reverse(data);
                data.AsSpan().CopyTo(buffer);

                if (len < buffer.Length)
                {
                    for (int i = len; i < buffer.Length; i++)
                    {
                        buffer[i++] = data[data.Length - 2];
                        buffer[i] = data[data.Length - 1];
                    }
                }

                dmaAddr[0] += (uint)len;
                dmaLen[0] -= len;

                if (dmaLen[0] == 0)
                {
                    bus.MI.SetRegister(MI.INTR_REG, bus.MI.GetRegister(MI.INTR_REG) | 0b100);
                    if (--dmaCount > 0)
                    {
                        dmaAddr[0] = dmaAddr[1];
                        dmaLen[0] = dmaLen[1];
                        SetRegister(AI.STATUS_REG, 0);
                    }
                    else
                        SetRegister(AI.STATUS_REG, 0);
                }
            }
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
            if (addr >= (ulong)AI.DRAM_ADDR_REG && addr < (ulong)AI.DRAM_ADDR_REG + 4)
            {
                base.Write(addr, val);
                if (dmaCount < 2)
                    dmaAddr[dmaCount] = GetRegister(AI.DRAM_ADDR_REG);
                return;
            }

            if (addr >= (ulong)AI.LEN_REG && addr < (ulong)AI.LEN_REG + 4)
            {
                base.Write(addr, val);
                uint len = GetRegister(AI.LEN_REG) & 0x3FFF8;
                if (dmaCount < 2 && len > 0)
                {
                    dmaLen[dmaCount++] = (int)len;
                    SetRegister(AI.STATUS_REG, dmaCount > 1 ? 0xC1100001 : 0);
                }
                return;
            }

            if (addr >= (ulong)AI.DACRATE_REG && addr < (ulong)AI.DACRATE_REG + 4)
            {
                base.Write(addr, val);
                int dacrate = (int)(48681812 / ((GetRegister(AI.DACRATE_REG) & 0x3FFF) + 1));
                /*if (wave.Frequency != dacrate)
                {
                    wave.Dispose();
                    wave = new Waveform(new AudioFormat(SampleFormat.S16, ByteOrder.BigEndian), PushSamples, ChannelMode.Stereo, dacrate);
                    wave.Play();
                }*/
                return;
            }

            if (addr >= (ulong)AI.STATUS_REG && addr < (ulong)AI.STATUS_REG + 4)
            {
                bus.MI.SetRegister(MI.INTR_REG, (uint)(bus.MI.GetRegister(MI.INTR_REG) & ~0b100));
                return;
            }

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
