namespace Chroma64.Emulator.Memory
{
    unsafe class BigEndianMemory
    {
        protected byte[] bytes;

        public BigEndianMemory(byte[] bytes)
        {
            this.bytes = bytes;
        }

        public BigEndianMemory(int size)
        {
            bytes = new byte[size];
        }

        public T Read<T>(ulong addr) where T : unmanaged
        {
            fixed (byte* romPtr = bytes)
                return *(T*)(romPtr + bytes.Length - addr - sizeof(T));
        }

        public void Write<T>(ulong addr, T val) where T : unmanaged
        {
            fixed (byte* romPtr = bytes)
            {
                T* ptr = (T*)(romPtr + bytes.Length - addr - sizeof(T));
                *ptr = val;
            }
        }

    }
}
