using Chroma64.Emulator.Memory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chroma64.Emulator.IO
{
    class PIFRAM : BigEndianMemory
    {
        public PIFRAM() : base(0x40) { }
    }
}
