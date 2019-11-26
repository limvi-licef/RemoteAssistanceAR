using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Viewer
{
    class Settings
    {
        public enum ARROW_TYPE { ARROW_TYPE_ZONES = 0, ARROW_TYPE_JOYSTICK = 1, ARROW_TYPE_CLICK = 2};
        public static bool m_fullFeatures = true;
        public static ARROW_TYPE m_useJoystick = ARROW_TYPE.ARROW_TYPE_CLICK;
    }
    
}
