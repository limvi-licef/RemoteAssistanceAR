﻿using System;
using System.IO;
using System.Diagnostics;

namespace RemoteApp
{
    class Logger
    {
        public static Logger m_logger = new Logger();

        private StreamWriter m_swLogFile;

        private Logger()
        {
            Debug.WriteLine("[Viewer::Logger] Temp directory path: " + Path.GetTempPath());
            String filePath = Path.GetTempPath() + DateTime.Now.Year + "-" + DateTime.Now.Month + "-" + DateTime.Now.Day + "_" + DateTime.Now.Hour + "-" + DateTime.Now.Minute + "_log.txt";
            m_swLogFile = File.CreateText(filePath);
        }

        public void addLog(string toLog)
        {
            m_swLogFile.WriteLine(DateTime.Now + " " + toLog);
            m_swLogFile.Flush();
        }
    }
}
