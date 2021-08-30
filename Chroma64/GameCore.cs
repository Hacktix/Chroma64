using Chroma;
using Chroma64.Emulator.Memory;
using System;
using System.IO;

namespace Chroma64
{
    internal class GameCore : Game
    {
        public GameCore(string[] args) : base(new(false, false))
        {
            if(args.Length >= 0 && File.Exists(args[0]))
            {
                // TODO: Actually do something here, this just loads the ROM for testing purposes
                new ROM(args[0]);
            }
        }
    }
}