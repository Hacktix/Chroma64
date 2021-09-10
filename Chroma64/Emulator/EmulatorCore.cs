using Chroma64.Emulator.Memory;
using Chroma64.Emulator.CPU;
using Chroma.Graphics;
using Chroma64.Emulator.Input;
using Chroma.Input.GameControllers;
using Chroma.Input;

namespace Chroma64.Emulator
{
    class EmulatorCore
    {
        private static readonly int TICKS_PER_FRAME = (int)(1562500 / 1.5);

        private ROM rom;
        private MemoryBus bus;
        private MainCPU cpu;

        public EmulatorCore(string romPath)
        {
            rom = new ROM(romPath);
            bus = new MemoryBus(rom);
            cpu = new MainCPU(bus);
        }

        #region Input
        public void RegisterController(int port, ControllerDevice controller)
        {
            if (port < 4)
                bus.PIF.Controllers[port] = controller;
        }

        public void UnregisterController(int port)
        {
            if (port < 4)
                bus.PIF.Controllers[port] = null;
        }

        public void OnButtonPressed(int port, ControllerButton button)
        {
            if (port < 4 && bus.PIF.Controllers[port] != null)
                bus.PIF.Controllers[port].OnButtonPressed(button);
        }

        public void OnButtonPressed(int port, KeyCode button)
        {
            if (port < 4 && bus.PIF.Controllers[port] != null)
                bus.PIF.Controllers[port].OnButtonPressed(button);
        }

        public void OnButtonReleased(int port, ControllerButton button)
        {
            if (port < 4 && bus.PIF.Controllers[port] != null)
                bus.PIF.Controllers[port].OnButtonReleased(button);
        }

        public void OnButtonReleased(int port, KeyCode button)
        {
            if (port < 4 && bus.PIF.Controllers[port] != null)
                bus.PIF.Controllers[port].OnButtonReleased(button);
        }

        public void OnAxisChanged(int port, ControllerAxis axis, float value)
        {
            if (port < 4 && bus.PIF.Controllers[port] != null)
                bus.PIF.Controllers[port].OnAxisChanged(axis, value);
        }
        #endregion

        public void TickFrame()
        {
            cpu.Tick(TICKS_PER_FRAME);

            // TODO: Tick components here
        }

        #region Rendering
        public bool NeedsRender()
        {
            return bus.VI.NeedsRender();
        }

        public void SetFramebufferTexture(ref Texture tex)
        {
            bus.VI.SetFramebuffer(ref tex);
        }
        #endregion
    }
}
