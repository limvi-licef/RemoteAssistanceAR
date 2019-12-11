using System;

namespace RemoteApp
{
    class Settings
    {
        public enum ARROW_TYPE { ARROW_TYPE_ZONES = 0, ARROW_TYPE_CLICK = 1};
        public static bool m_fullFeatures = false; // If true, also shows the debug button.
        public static ARROW_TYPE m_guidanceType = ARROW_TYPE.ARROW_TYPE_CLICK;
        public static bool m_showLocal = false; // Debug to show the local webcam if value is true
        public static String m_sIPLocalUDP = "10.44.161.23";
        public static String m_sPortLocalUDP = "62503";
        public static String m_sIPRemoteUDP = "10.44.161.15";
        public static String m_sPortRemoteUDP = "63777";
    }
    
}
