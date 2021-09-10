using Chroma.Input;
using Chroma.Input.GameControllers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Chroma64.Emulator.Input
{
    interface ControllerDevice
    {
        public void OnButtonPressed(ControllerButton button);
        public void OnButtonReleased(ControllerButton button);
        public void OnButtonPressed(KeyCode button);
        public void OnButtonReleased(KeyCode button);
        public void OnAxisChanged(ControllerAxis axis, float value);
        public byte[] ExecPIF(byte[] cmdBuffer);
    }
}
