using System;

namespace RemoteApp
{
    class Settings
    {
        public enum ARROW_TYPE { ARROW_TYPE_ZONES = 0, ARROW_TYPE_CLICK = 1};
        public static bool m_fullFeatures = false; // If true, also shows the debug button.
        public static ARROW_TYPE m_guidanceType = ARROW_TYPE.ARROW_TYPE_CLICK;
        public static bool m_showLocal = false; // Debug to show the local webcam if value is true

        public static String m_sIPLocalUDP = "<local IP>";
        public static String m_sPortLocalUDP = "62503";
        public static String m_sIPRemoteUDP = "<Hololens IP>";
        public static String m_sPortRemoteUDP = "63777";
        public static String m_sLocalPeerId = "<Local computer name>";
        public static String m_sRemotePeerId = "<Hololens name>";
    }
    
}
