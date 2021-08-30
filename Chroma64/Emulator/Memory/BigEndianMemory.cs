namespace Chroma64.Emulator.Memory
{
    unsafe abstract class BigEndianMemory
    {
        protected byte[] bytes;

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
