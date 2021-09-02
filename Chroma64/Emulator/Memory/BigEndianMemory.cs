namespace Chroma64.Emulator.Memory
{
    unsafe class BigEndianMemory
    {
        public byte[] Bytes;

        public BigEndianMemory(byte[] bytes)
        {
            this.Bytes = bytes;
        }

        public BigEndianMemory(int size)
        {
            Bytes = new byte[size];
        }

        public T Read<T>(ulong addr) where T : unmanaged
        {
            fixed (byte* romPtr = Bytes)
                return *(T*)(romPtr + Bytes.Length - addr - sizeof(T));
        }

        public void Write<T>(ulong addr, T val) where T : unmanaged
        {
            fixed (byte* romPtr = Bytes)
            {
                T* ptr = (T*)(romPtr + Bytes.Length - addr - sizeof(T));
                *ptr = val;
            }
        }

    }
}
