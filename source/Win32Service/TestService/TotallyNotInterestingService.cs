using System;
using System.IO;
using System.Reflection;
using System.Threading;
using PeanutButter.EasyArgs.Attributes;
using PeanutButter.INI;
using PeanutButter.ServiceShell;

namespace TestService
{
    public class TotallyNotInterestingService : Shell
    {
        public const string INIFILE = "config.ini";
        public const string SECTION_DELAY = "delay";

        public static string IniFilePath =>
            Path.Combine(
                Path.GetDirectoryName(
                    new Uri(
                        typeof(TotallyNotInterestingService).Assembly.Location
                    ).LocalPath
                ) ?? ".",
                INIFILE
            );

        public static ServiceOptions Options { get; set; }

        public TotallyNotInterestingService()
        {
            var exePath = Assembly.GetEntryAssembly()?.CodeBase
                ?? "(unknown location)";
            DisplayName = Options?.DisplayName ?? "Totally Not Interesting Service at: " + exePath;
            ServiceName = Options?.Name ?? "Test Service";
            Interval = 1;
            Version.Major = 1;
        }

        protected override void RunOnce()
        {
            Log("Running once");
        }

        protected override void OnStart(string[] args)
        {
            SleepForIniConfiguredValue(
                "start",
                SECTION_DELAY,
                nameof(Options.StartDelay)
            );
        }

        private bool TryReadIniSetting<T>(
            string sectionName,
            string settingName,
            out T result
        )
        {
            Log($"read ini at {IniFilePath}");
            var ini = new INIFile(IniFilePath);
            result = default;
            if (!ini.HasSetting(sectionName, settingName))
            {
                return false;
            }

            try
            {
                result = (T) Convert.ChangeType(ini[sectionName][settingName], typeof(T));
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void SleepForIniConfiguredValue(
            string operation,
            string section,
            string setting
        )
        {
            Log($"Test if should delay for operation: {operation}");
            if (
                !TryReadIniSetting<int>(
                    section,
                    setting,
                    out var delay
                )
            )
            {
                Log(" -> no delay");
                return;
            }

            Log($" -> delay: {delay}ms");

            Thread.Sleep(delay);
        }

        protected override void OnPause()
        {
            SleepForIniConfiguredValue(
                "pause",
                SECTION_DELAY,
                nameof(Options.PauseDelay)
            );
        }

        protected override void OnStop()
        {
            SleepForIniConfiguredValue(
                "stop",
                SECTION_DELAY,
                nameof(Options.StopDelay)
            );
        }
    }

    public class ServiceOptions : IServiceCommandlineOptions
    {
        [Description("Set the short name for this service")]
        [DisableGeneratedShortName]
        public string Name { get; set; }

        [Description("Set the long name for this service")]
        [DisableGeneratedShortName]
        public string DisplayName { get; set; }

        [Description("Delay, in ms, when starting up")]
        [DisableGeneratedShortName]
        public int StartDelay { get; set; }

        [Description("Delay, in ms, when pausing")]
        [DisableGeneratedShortName]
        public int PauseDelay { get; set; }

        [Description("Delay, in ms, when stopping")]
        [DisableGeneratedShortName]
        public int StopDelay { get; set; }

        public bool Install { get; set; }
        public bool Uninstall { get; set; }
        public bool RunOnce { get; set; }
        public bool Debug { get; set; }
        public int Wait { get; set; }
        public bool ShowVersion { get; set; }
        public bool StartService { get; set; }
        public bool StopService { get; set; }
        public bool ManualStart { get; set; }
        public bool Disabled { get; set; }
    }
}