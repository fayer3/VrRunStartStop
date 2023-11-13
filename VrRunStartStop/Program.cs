using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Valve.VR;

namespace VrRunStartStop
{
    internal static class Program
    {

        static readonly string PATH_LOGFILE = "./OpenVRStartup.log";
        static readonly string PATH_STARTFOLDER = "./start/";
        static readonly string PATH_STOPFOLDER = "./stop/";
        static readonly string FILE_PATTERN = "*.cmd";

        private volatile static bool _isReady = false;
        private volatile static Thread? _thread;
        private volatile static bool _stopThread = false;

        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            LogUtils.Init(PATH_LOGFILE);

            // App setup
            ApplicationConfiguration.Initialize();
            NotifyIcon icon = new();
            const string resource = "VrRunStartStop.resources.logo.ico";
            var assembly = Assembly.GetExecutingAssembly();
            icon.Icon = new Icon(assembly.GetManifestResourceStream(resource));
            icon.Text = "VR run at Start/Stop";
            icon.ContextMenuStrip = new ContextMenuStrip()
            {
                Items = { new ToolStripMenuItem("Exit", null, Exit) }
            };
            icon.Visible = true;

            // Starting worker

            // no clue why this call is inverted, but I'm too lazy to figure it out
            bool firstRun = LogUtils.LogFileExists();

            LogUtils.Clear();
            LogUtils.WriteLine($"Application starting ({Assembly.GetExecutingAssembly().GetName().Version})");

            // Check if first run, if so do show instructions popup.
            if (firstRun)
            {
                _isReady = true;
            }
            else
            {
                MessageBox.Show("========================" +
                    "\n First Run Instructions " +
                    "\n========================" +
                    "\nThis app automatically sets itself to auto-launch with SteamVR." +
                    "\nWhen it runs it will in turn run all " + FILE_PATTERN + " files in the " + PATH_STARTFOLDER + " folder." +
                    "\nIf there are " + FILE_PATTERN + " files in " + PATH_STOPFOLDER + " it will stay and run those on shutdown." +
                    "\nThis message is only shown once, to see it again delete the log file." +
                    "\nPress [OK] in this window to continue execution." +
                    "\nIf there are shutdown scripts the app will remain open in the system tray.",
                    "VrRunStartStop",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                _isReady = true;
            }
            _thread = new Thread(Worker);
            if (!_thread.IsAlive) _thread.Start();
            else LogUtils.WriteLine("Error: Could not start worker thread");
            Application.Run();
        }

        private static bool _isConnected = false;

        private static void Exit(object? sender, EventArgs e)
        {
            _stopThread = true;
            Application.Exit();
        }
        private static void Worker()
        {
            var shouldRun = true;

            Thread.CurrentThread.IsBackground = true;
            while (shouldRun)
            {
                if (!_isConnected)
                {
                    Thread.Sleep(1000);
                    _isConnected = InitVR();
                }
                else if (_isReady)
                {
                    RunScripts(PATH_STARTFOLDER);
                    if (WeHaveScripts(PATH_STOPFOLDER)) WaitForQuit();
                    OpenVR.Shutdown();
                    if (!_stopThread)
                    {
                        RunScripts(PATH_STOPFOLDER);
                    }
                    shouldRun = false;
                }
                if (!shouldRun || _stopThread)
                {
                    LogUtils.WriteLine("Application exiting");
                    Application.Exit();
                }
            }
        }

        // Initializing connection to OpenVR
        private static bool InitVR()
        {
            var error = EVRInitError.None;
            OpenVR.Init(ref error, EVRApplicationType.VRApplication_Overlay);
            if (error != EVRInitError.None)
            {
                LogUtils.WriteLine($"Error: OpenVR init failed: {Enum.GetName(typeof(EVRInitError), error)}");
                return false;
            }
            else
            {
                LogUtils.WriteLine("OpenVR init success");

                // Add app manifest and set auto-launch
                var appKey = "fayer3.VrRunStartStop";
                if (!OpenVR.Applications.IsApplicationInstalled(appKey))
                {
                    var manifestError = OpenVR.Applications.AddApplicationManifest(Path.GetFullPath(AppContext.BaseDirectory + "./app.vrmanifest"), false);
                    if (manifestError == EVRApplicationError.None) LogUtils.WriteLine("Successfully installed app manifest");
                    else LogUtils.WriteLine($"Error: Failed to add app manifest: {Enum.GetName(typeof(EVRApplicationError), manifestError)}");

                    var autolaunchError = OpenVR.Applications.SetApplicationAutoLaunch(appKey, true);
                    if (autolaunchError == EVRApplicationError.None) LogUtils.WriteLine("Successfully set app to auto launch");
                    else LogUtils.WriteLine($"Error: Failed to turn on auto launch: {Enum.GetName(typeof(EVRApplicationError), autolaunchError)}");
                }
                return true;
            }
        }

        // Scripts
        private static void RunScripts(string folder)
        {
            try
            {
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                var files = Directory.GetFiles(folder, FILE_PATTERN);
                LogUtils.WriteLine($"Found: {files.Length} script(s) in {folder}");
                foreach (var file in files)
                {
                    LogUtils.WriteLine($"Executing: {file}");
                    var path = Path.Combine(Environment.CurrentDirectory, file);
                    Process p = new Process();
                    p.StartInfo.CreateNoWindow = true;
                    p.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
                    p.StartInfo.FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe");
                    p.StartInfo.Arguments = $"/C \"{path}\"";
                    p.Start();
                }
                if (files.Length == 0) LogUtils.WriteLine($"Did not find any {FILE_PATTERN} files to execute in {folder}");
            }
            catch (Exception e)
            {
                LogUtils.WriteLine($"Error: Could not load scripts from {folder}: {e.Message}");
            }
        }

        private static void WaitForQuit()
        {
            LogUtils.WriteLine("wait for the shutdown of SteamVR to run additional scripts on exit.");
            var shouldRun = true;
            while (shouldRun && !_stopThread)
            {
                var vrEvents = new List<VREvent_t>();
                var vrEvent = new VREvent_t();
                uint eventSize = (uint)Marshal.SizeOf(vrEvent);
                try
                {
                    while (OpenVR.System.PollNextEvent(ref vrEvent, eventSize))
                    {
                        vrEvents.Add(vrEvent);
                    }
                }
                catch (Exception e)
                {
                    LogUtils.WriteLine($"Could not get new events: {e.Message}");
                }

                foreach (var e in vrEvents)
                {
                    if ((EVREventType)e.eventType == EVREventType.VREvent_Quit)
                    {
                        OpenVR.System.AcknowledgeQuit_Exiting();
                        shouldRun = false;
                    }
                }
                Thread.Sleep(1000);
            }
        }

        private static bool WeHaveScripts(string folder)
        {
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            return Directory.GetFiles(folder, FILE_PATTERN).Length > 0;
        }
    }
}