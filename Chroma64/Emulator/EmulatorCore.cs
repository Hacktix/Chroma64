using Chroma64.Emulator.Memory;
using Chroma64.Emulator.CPU;
using Chroma.Graphics;
using Chroma64.Emulator.Input;
using Chroma.Input.GameControllers;
using Chroma.Input;
using Chroma.Diagnostics.Logging;

namespace Chroma64.Emulator
{
    class EmulatorCore
    {
        private static readonly int TICKS_PER_FRAME = (int)(1562500 / 1.5);

        private ROM rom;
        private MemoryBus bus;
        private MainCPU cpu;

        private Log _log = LogManager.GetForCurrentAssembly();

        public EmulatorCore(string romPath)
        {
            rom = new ROM(romPath);
            bus = new MemoryBus(rom);
            cpu = new MainCPU(bus);
        }

        #region Input
        public int RegisterController(ControllerDevice controller, int port)
        {
            if (port >= 0 && port < 4)
            {
                bus.PIF.Controllers[port] = controller;
                _log.Info($"{controller.GetType().Name} connected to channel {port}");
                return port;
            }
            return -1;
        }

        public int RegisterController(ControllerDevice controller)
        {
            for (int i = 0; i < 4; i++)
            {
                if (bus.PIF.Controllers[i] == null)
                {
                    RegisterController(controller, i);
                    return i;
                }
            }
            return -1;
        }

        public void UnregisterController(int port)
        {
            if (port >= 0 && port < 4)
            {
                _log.Info($"{bus.PIF.Controllers[port].GetType().Name} disconnected from channel {port}");
                bus.PIF.Controllers[port] = null;
            }
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
