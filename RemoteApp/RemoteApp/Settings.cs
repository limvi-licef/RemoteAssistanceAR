using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RemoteApp
{
    class Settings
    {
        public enum ARROW_TYPE { ARROW_TYPE_ZONES = 0, ARROW_TYPE_CLICK = 1};
        public static bool m_fullFeatures = true; // TODO check why in this version, the arrow is not shown
        public static ARROW_TYPE m_guidanceType = ARROW_TYPE.ARROW_TYPE_CLICK;
        public static bool m_showLocal = false; // Debug to show the local webcam if value is true
    }
    
}
