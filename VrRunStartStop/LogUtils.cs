using System.Reflection;

namespace VrRunStartStop
{

    static class LogUtils
    {
        static string logFile = "";

        static public void Init(string filePath)
        {
            logFile = Path.GetDirectoryName(AppContext.BaseDirectory) + "\\" + filePath;
        }

        static public void WriteLine(string line) {
            if (logFile == "")
            {
                throw new Exception("log file path not set");
            }
            string time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            using (StreamWriter w = File.AppendText(logFile))
            {
                w.WriteLine("[" + time + "] " + line);
            }
        }

        static public void Clear() {
            if (logFile == "")
            {
                throw new Exception("log file path not set");
            }
            File.WriteAllText(logFile, "");
        }

        static public bool LogFileExists() {
            if (logFile == "")
            {
                throw new Exception("log file path not set");
            }
            return File.Exists(logFile);
        }
    }
}
