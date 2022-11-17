﻿// #if NETSTANDARD

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Imported.PeanutButter.Utils;
using PeanutButter.WindowsServiceManagement.Exceptions;

namespace PeanutButter.WindowsServiceManagement
{
    [Flags]
    public enum ControlOptions
    {
        None = 0,
        Wait = 1,
        Force = 2
    }

    public interface IWindowsServiceUtil
    {
        /// <summary>
        /// The name of the service, usable from, eg 'net stop {name}'
        /// </summary>
        string ServiceName { get; }

        /// <summary>
        /// The name displayed in the service manager (service.msc)
        /// </summary>
        string DisplayName { get; }

        /// <summary>
        /// The path to the service executable
        /// </summary>
        string ServiceExe { get; }

        /// <summary>
        /// The arguments that the service is started with
        /// </summary>
        string[] Arguments { get; }

        /// <summary>
        /// The full commandline for this service
        /// </summary>
        string Commandline { get; }

        /// <summary>
        /// The current state of this service
        /// </summary>
        ServiceState State { get; }

        /// <summary>
        /// The startup type of the service
        /// </summary>
        ServiceStartupTypes StartupType { get; }

#if NETSTANDARD
        /// <summary>
        /// Reflects the allowed transition states for the service
        /// </summary>
        ServiceState[] AllowedStates { get; }
#endif

        /// <summary>
        /// Whether or not the service is disabled
        /// -> this is a shortcut to check the startup type vs the disabled startup type
        /// </summary>
        bool IsDisabled { get; }

        /// <summary>
        /// Whether or not the service is installed
        /// -> this is a shortcut to check startup type vs unknown
        /// </summary>
        bool IsInstalled { get; }

        /// <summary>
        /// Whether or not the service is running
        /// - always false for an uninstalled service
        /// </summary>
        bool IsRunning { get; }

        /// <summary>
        /// Whether or not the service is paused
        /// - always false for an uninstalled service
        /// </summary>
        bool IsPaused { get; }

        /// <summary>
        /// Test if the service is marked for deletion.
        /// </summary>
        bool IsMarkedForDelete { get; }

        /// <summary>
        /// True if the service is in a stoppable state
        /// </summary>
        bool IsStoppable { get; }

        /// <summary>
        /// True if the service is in a startable state
        /// </summary>
        bool IsStartable { get; }

        /// <summary>
        /// The process id for a running process (0 when not running)
        /// </summary>
        // ReSharper disable once InconsistentNaming
        int ServicePID { get; }

#if NETSTANDARD
        /// <summary>
        /// Refresh the state of the service
        /// </summary>
        void Refresh();
#endif

        /// <summary>
        /// Start the service and wait for it to be running
        /// </summary>
        void Start();

        /// <summary>
        /// Start the service
        /// Explicitly wait or not for it to start
        /// </summary>
        /// <param name="wait"></param>
        void Start(bool wait);

        /// <summary>
        /// Stop the service and wait for it to be running
        /// </summary>
        void Stop();

        /// <summary>
        /// Start the service
        /// Explicitly wait or not for it to stop
        /// </summary>
        /// <param name="wait"></param>
        void Stop(bool wait);

        /// <summary>
        /// Pause the service, waiting for it to enter the paused state
        /// </summary>
        void Pause();

        /// <summary>
        /// Pause the service
        /// Explicitly wait or not for it to pause
        /// </summary>
        void Pause(bool wait);

        /// <summary>
        /// Continue the service, waiting for it to enter the continued state
        /// </summary>
        void Continue();

        /// <summary>
        /// Continue the service
        /// Explicitly wait or not for it to continue
        /// </summary>
        void Continue(bool wait);

#if NETSTANDARD
        /// <summary>
        /// Configure the service's startup type
        /// </summary>
        /// <param name="startupType"></param>
        void ConfigureStartup(ServiceStartupTypes startupType);
#endif

        /// <summary>
        /// Installs the service with automatic start.
        /// Will error if the service is already found by name.
        /// </summary>
        void Install();

        /// <summary>
        /// Install with the provided startup type
        /// </summary>
        /// <param name="startupType"></param>
        void Install(ServiceStartupTypes startupType);

        /// <summary>
        /// Uninstalls the service. If the service is not found by name, does nothing.
        /// </summary>
        void Uninstall();

        /// <summary>
        /// More control over uninstallation
        /// </summary>
        /// <param name="options"></param>
        void Uninstall(ControlOptions options);

        /// <summary>
        /// Installs the service as AutoStart and starts it,
        /// waiting for the service to report that it's running
        /// </summary>
        void InstallAndStart();

        /// <summary>
        /// Install and start the service as AutoStarting,
        /// with control over whether to wait for the service
        /// to have properly started
        /// </summary>
        /// <param name="wait"></param>
        void InstallAndStart(bool wait);

        /// <summary>
        /// Install and start the service, with control over whether
        /// to wait for the service to have properly started
        /// </summary>
        /// <param name="startupType"></param>
        /// <param name="wait"></param>
        void InstallAndStart(ServiceStartupTypes startupType, bool wait);

        /// <summary>
        /// Disable the service from running
        /// </summary>
        void Disable();

        /// <summary>
        /// Set the service to automatically start with the host system
        /// </summary>
        void SetAutomaticStart();

        /// <summary>
        /// Set the service to require manual start
        /// </summary>
        void SetManualStart();

        /// <summary>
        /// Kills the service, if running
        /// </summary>
        /// <returns></returns>
        KillServiceResult KillService();
    }
    
    public class WindowsServiceUtil : IWindowsServiceUtil
    {
        public static IWindowsServiceUtil GetServiceByPid(int pid)
        {
            var sci = new ServiceControlInterface();
            var serviceName = sci.FindServiceByPid(pid);
            return serviceName is null
                ? null
                : new WindowsServiceUtil(serviceName);
        }

        public static IWindowsServiceUtil GetServiceByPath(string serviceExe)
        {
            var ctl = new ServiceControlInterface();
            var matches = new List<IWindowsServiceUtil>();
            foreach (var serviceName in ctl.ListAllServices())
            {
                var possible = new WindowsServiceUtil(serviceName);
                if (possible.ServiceExe.Equals(serviceExe, StringComparison.CurrentCultureIgnoreCase))
                {
                    matches.Add(possible);
                }
            }

            if (matches.Count == 1)
            {
                return matches[0];
            }

            var msg = matches.Count == 0
                ? "No"
                : "Multiple";
            throw new ArgumentException(
                $"{msg} matches for queried service path {serviceExe}"
            );
        }

        public const int DEFAULT_SERVICE_CONTROL_TIMEOUT_SECONDS = 30;
        public const int DEFAULT_POLL_INTERVAL_MILLISECONDS = 500;

        public string ServiceName { get; }
        public string DisplayName { get; private set; }
        public ServiceState State { get; private set; } = ServiceState.Unknown;
        public ServiceStartupTypes StartupType { get; private set; } = ServiceStartupTypes.Unknown;
        public ServiceState[] AllowedStates { get; private set; }
        public bool IsDisabled => StartupType == ServiceStartupTypes.Disabled;
        public bool IsInstalled => StartupType != ServiceStartupTypes.Unknown && !IsMarkedForDelete;
        public bool IsRunning => ProcessIsStillRunning();
        public bool IsPaused => IsInstalled && State is ServiceState.Paused or ServiceState.PausePending;
        public bool IsMarkedForDelete => ReadIsMarkedForDelete();
        public bool IsStoppable => (IsRunning || IsPaused) && !IsMarkedForDelete; // once marked for delete, glhf
        public bool IsStartable => IsInstalled && !IsRunning && !IsPaused && !IsDisabled;
        public int ServicePID { get; private set; }

        public string ServiceExe { get; private set; }
        public string[] Arguments { get; private set; }

        public string Commandline =>
            _commandline ??= new Commandline(ServiceExe, Arguments);

        private string _commandline;

        public int ServiceControlTimeoutSeconds { get; set; } = DEFAULT_SERVICE_CONTROL_TIMEOUT_SECONDS;
        public int PollIntervalMilliseconds { get; set; } = DEFAULT_POLL_INTERVAL_MILLISECONDS;

        private readonly ServiceControlInterface _ctl;

        /// <summary>
        /// Used to query existing services
        /// </summary>
        /// <param name="serviceName"></param>
        public WindowsServiceUtil(string serviceName)
        {
            _ctl = new ServiceControlInterface();
            ServiceName = serviceName;
            Refresh();
        }

        /// <summary>
        /// Used to install a service, but still
        /// provides the same query interface
        /// </summary>
        /// <param name="serviceName"></param>
        /// <param name="displayName"></param>
        /// <param name="commandline"></param>
        public WindowsServiceUtil(
            string serviceName,
            string displayName,
            string commandline
        )
        {
            _ctl = new ServiceControlInterface();
            ServiceName = serviceName;
            Refresh();
            if (IsInstalled)
            {
                return;
            }

            DisplayName = displayName;
            SetServiceCommandAndArgsFrom(commandline);
        }

        public void Refresh()
        {
            var oldProcessId = ServicePID;
            Reset();
            try
            {
                var info = _ctl.QueryAll(ServiceName);
                DisplayName = info[ServiceControlKeys.DISPLAY_NAME];
                SetExeAndArgsFrom(info);
                TryParseStartup(info);
                TrySetStateFrom(info);
                TrySetProcessIdFrom(info);
            }
            catch (ServiceNotInstalledException)
            {
                if (ProcessIsRunning(oldProcessId))
                {
                    ServicePID = oldProcessId;
                }
            }
        }

        private bool ProcessIsStillRunning()
        {
            var pid = ServicePID;
            return pid > 0 && ProcessIsRunning(pid);
        }

        private static bool ProcessIsRunning(int pid)
        {
            try
            {
                Process.GetProcessById(pid);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        private void TrySetProcessIdFrom(IDictionary<string, string> info)
        {
            if (!info.TryGetValue(ServiceControlKeys.PROCESS_ID, out var pidString))
            {
                return;
            }

            if (int.TryParse(pidString, out var pid))
            {
                ServicePID = pid;
            }
        }

        private void Reset()
        {
            State = ServiceState.NotFound;
            StartupType = ServiceStartupTypes.Unknown;
            ServiceExe = "";
            Arguments = new string[0];
            AllowedStates = new ServiceState[0];
            DisplayName = "";
            ServicePID = 0;
            _commandline = null;
        }

        private void StoreLastConfiguration()
        {
            _lastInstallConfig = GenerateInstallConfig();
        }

        private ServiceInstallConfig GenerateInstallConfig()
        {
            return new ServiceInstallConfig()
            {
                ServiceName = ServiceName,
                ServiceExe = ServiceExe,
                Arguments = Arguments,
                DisplayName = DisplayName,
                StartupType = StartupType
            };
        }

        private class ServiceInstallConfig
        {
            public string ServiceName { get; set; }
            public string ServiceExe { get; set; }
            public string[] Arguments { get; set; }
            public string DisplayName { get; set; }
            public ServiceStartupTypes StartupType { get; set; }
        }

        private bool ReadIsMarkedForDelete()
        {
            var valueName = "DeleteFlag";
            using var io = ProcessIO.Start(
                "reg",
                "query",
                $"HKLM\\SYSTEM\\CurrentControlSet\\Services\\{ServiceName}",
                "/v",
                valueName
            );
            io.Process.WaitForExit();
            if (io.ExitCode != 0)
            {
                return false; // key probably doesn't exist
            }

            foreach (var line in io.StandardOutput)
            {
                var parts = line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.FirstOrDefault() == valueName)
                {
                    return parts.Skip(2).FirstOrDefault() == "0x1";
                }
            }

            return false;
        }

        private void SetExeAndArgsFrom(IDictionary<string, string> info)
        {
            var binPath = info[ServiceControlKeys.BINARY_PATH_NAME];
            SetServiceCommandAndArgsFrom(binPath);
        }

        private void SetServiceCommandAndArgsFrom(
            string commandline
        )
        {
            var cli = Imported.PeanutButter.Utils.Commandline.Parse(commandline);
            ServiceExe = cli.Command;
            Arguments = cli.Args;
        }

        private void TryParseStartup(IDictionary<string, string> info)
        {
            var raw = info[ServiceControlKeys.START_TYPE];
            StartupType = ParseEnum<ServiceStartupTypes>(raw);
            if (LooksLikeDelayedAutoStart(StartupType, raw))
            {
                StartupType = ServiceStartupTypes.DelayedAutomatic;
            }
        }

        private static bool LooksLikeDelayedAutoStart(
            ServiceStartupTypes startupType,
            string raw
        )
        {
            // I'm not sure if there are localisations for the output,
            // -> the raw output for a delayed start is "2   AUTO_START  (DELAYED)"
            //    so I'm assuming that the parens are less susceptible to localisation?
            return startupType == ServiceStartupTypes.Automatic &&
                raw.Contains("(");
        }

        private void TrySetStateFrom(IDictionary<string, string> info)
        {
            State = ServiceState.Unknown;
            AllowedStates = new ServiceState[0];
            try
            {
                State = ParseState(info[ServiceControlKeys.STATE]);
                DeterminePossibleStates(State, IsDisabled, info[ServiceControlKeys.STATE]);
            }
            catch (ServiceNotInstalledException)
            {
                State = ServiceState.NotFound;
            }
            catch (Exception)
            {
                State = ServiceState.Unknown;
                throw;
            }
        }

        private void DeterminePossibleStates(
            ServiceState currentState,
            bool isDisabled,
            string s)
        {
            var parensPart = s.Split('(').Last().Trim(')');
            var states = parensPart.Split(',').Trim();
            var result = new HashSet<ServiceState>();
            foreach (var state in states)
            {
                switch (state)
                {
                    case "STOPPABLE":
                        result.Add(ServiceState.Stopped);
                        continue;
                    case "PAUSABLE":
                        result.Add(ServiceState.Paused);
                        if (currentState == ServiceState.Paused && !isDisabled)
                        {
                            result.Add(ServiceState.Running);
                        }

                        continue;
                }
            }

            if (currentState == ServiceState.Stopped && !isDisabled)
            {
                result.Add(ServiceState.Running);
            }

            AllowedStates = result.ToArray();
        }

        public void Start()
        {
            Start(wait: true);
        }

        public void Start(bool wait)
        {
            ChangeServiceState(ServiceState.Running, wait);
        }

        public void Stop()
        {
            Stop(wait: true);
        }

        public void Stop(bool wait)
        {
            ChangeServiceState(ServiceState.Stopped, wait);
        }

        public void Pause()
        {
            Pause(wait: true);
        }

        public void Pause(bool wait)
        {
            ChangeServiceState(ServiceState.Paused, wait);
        }

        public void Continue()
        {
            Continue(wait: true);
        }

        public void Continue(bool wait)
        {
            ChangeServiceState(ServiceState.Running, wait);
        }

        private static readonly Dictionary<ServiceStartupTypes, string>
            ServiceStartupTypeVerbs = new()
            {
                [ServiceStartupTypes.Automatic] = "auto",
                [ServiceStartupTypes.Disabled] = "disabled",
                [ServiceStartupTypes.Manual] = "demand",
                [ServiceStartupTypes.DelayedAutomatic] = "delayed-auto"
            };

        public void ConfigureStartup(
            ServiceStartupTypes startupType
        )
        {
            var verb = ResolveStartupVerbFor(startupType);
            _ctl.RunServiceControl("config", ServiceName, "start=", verb);
            Refresh();
        }

        private static string ResolveStartupVerbFor(
            ServiceStartupTypes startupType
        )
        {
            if (!ServiceStartupTypeVerbs.TryGetValue(startupType, out var verb))
            {
                throw new NotSupportedException(
                    $"Service startup type {startupType} is not supported"
                );
            }

            return verb;
        }

        private void ChangeServiceState(
            ServiceState toState,
            bool wait
        )
        {
            Refresh();
            if (State == toState)
            {
                return;
            }

            var transitionKey = Tuple.Create(State, toState);

            if (PendingStateMap.TryGetValue(transitionKey, out var pendingState))
            {
                if (State == pendingState)
                {
                    WaitForStateIfRequired();
                    return;
                }
            }
            else
            {
                throw new InvalidOperationException(
                    $"Pending state for requested state {toState} is unknown"
                );
            }

            if (!StateChangeVerbs.TryGetValue(transitionKey, out var verb))
            {
                throw new InvalidOperationException(
                    $"Unable to change state to {toState}: no verb set for this state"
                );
            }

            var scResult = _ctl.RunServiceControl(verb, ServiceName);
            var resultState = ParseState(scResult[ServiceControlKeys.STATE]);
            if (resultState == toState)
            {
                // already accomplished!
                return;
            }

            if (resultState != pendingState)
            {
                throw new ServiceControlException(
                    $"Unable to change state of '{ServiceName}' to {toState} (sc reports state: {scResult[ServiceControlKeys.STATE]})"
                );
            }

            WaitForStateIfRequired();

            void WaitForStateIfRequired()
            {
                if (!wait)
                {
                    return;
                }

                WaitForServiceState(toState);
            }
        }

        private static readonly Dictionary<Tuple<ServiceState, ServiceState>, ServiceState>
            PendingStateMap = new()
            {
                [Tuple.Create(ServiceState.Stopped, ServiceState.Running)] = ServiceState.StartPending,
                [Tuple.Create(ServiceState.StartPending, ServiceState.Running)] = ServiceState.StartPending,
                [Tuple.Create(ServiceState.Running, ServiceState.Stopped)] = ServiceState.StopPending,
                [Tuple.Create(ServiceState.StopPending, ServiceState.Stopped)] = ServiceState.StopPending,
                [Tuple.Create(ServiceState.Running, ServiceState.Paused)] = ServiceState.PausePending,
                [Tuple.Create(ServiceState.PausePending, ServiceState.Paused)] = ServiceState.PausePending,
                [Tuple.Create(ServiceState.Paused, ServiceState.Running)] = ServiceState.ContinuePending,
                [Tuple.Create(ServiceState.ContinuePending, ServiceState.Running)] = ServiceState.ContinuePending
            };

        private static readonly Dictionary<Tuple<ServiceState, ServiceState>, string>
            StateChangeVerbs = new()
            {
                [Tuple.Create(ServiceState.Stopped, ServiceState.Running)] = "start",
                [Tuple.Create(ServiceState.Running, ServiceState.Stopped)] = "stop",
                [Tuple.Create(ServiceState.Running, ServiceState.Paused)] = "pause",
                [Tuple.Create(ServiceState.Paused, ServiceState.Running)] = "continue"
            };

        private ServiceInstallConfig _lastInstallConfig;

        private void WaitForServiceState(
            ServiceState desiredState
        )
        {
            WaitFor(
                $"'{ServiceName}' to enter state: {desiredState}",
                () => RefreshState() == desiredState,
                ServiceControlTimeoutSeconds
            );
        }


        private ServiceState RefreshState()
        {
            Refresh();
            return State;
        }

        private void WaitFor(
            string label,
            Func<bool> func,
            int timeoutSeconds
        )
        {
            if (func())
            {
                return;
            }

            var timeout = DateTime.Now.AddSeconds(timeoutSeconds);
            // opportunistic: try a half-sleep first
            Thread.Sleep(PollIntervalMilliseconds / 2);
            while (!func())
            {
                Thread.Sleep(PollIntervalMilliseconds);
                if (DateTime.Now > timeout)
                {
                    throw new TimeoutException($"Timed out waiting for: {label} (waited {timeoutSeconds}s)");
                }
            }
        }

        private ServiceState ParseState(string s)
        {
            return ParseEnum<ServiceState>(s);
        }

        private T ParseEnum<T>(string s)
            where T : struct, Enum
        {
            var intVal = s.Split(' ').FirstOrDefault();
            return Enum.TryParse<T>(intVal, out var result)
                ? result
                : default;
        }

        public void Install()
        {
            Install(ServiceStartupTypes.Automatic);
        }

        public void Install(
            ServiceStartupTypes startupType
        )
        {
            var config = _lastInstallConfig ?? GenerateInstallConfig();
            config.StartupType = startupType;

            Install(config);
            _lastInstallConfig = null;
        }

        private void Install(ServiceInstallConfig config)
        {
            var verb = ResolveStartupVerbFor(
                config.StartupType
            );

            _ctl.RunServiceControl(
                "create",
                config.ServiceName,
                "type=", "own",
                "start=", verb,
                "error=", "normal",
                "binpath=", new Commandline(config.ServiceExe, config.Arguments),
                "displayname=", config.DisplayName
            );

            Refresh();
        }

        public void Uninstall()
        {
            Uninstall(wait: true);
        }

        public void Uninstall(bool wait)
        {
            Uninstall(
                wait
                    ? ControlOptions.Wait
                    : ControlOptions.None
            );
        }

        public void Uninstall(ControlOptions controlOptions)
        {
            if (!IsInstalled)
            {
                return;
            }

            StoreLastConfiguration();

            var force = controlOptions.HasFlag(ControlOptions.Force);
            var wait = force || controlOptions.HasFlag(ControlOptions.Wait);
            try
            {
                Stop(wait);
                DoUninstall();
            }
            catch (Exception)
            {
                if (!force)
                {
                    throw;
                }
            }

            if (!force)
            {
                VerifyUninstalled();
                return;
            }

            TryDo(() => KillService());
            TryDo(DoUninstall, onFail: Refresh);
            VerifyUninstalled();

            void VerifyUninstalled()
            {
                if (IsInstalled)
                {
                    throw new ServiceControlException(
                        $"Unable to uninstall {ServiceName}"
                    );
                }
            }

            void DoUninstall()
            {
                _ctl.RunServiceControl(
                    "delete",
                    ServiceName
                );
                Refresh();
            }
        }

        private static void TryDo(
            Action action,
            Action onFail = null
        )
        {
            try
            {
                action();
            }
            catch
            {
                TryDo(() => onFail?.Invoke());
                // suppress
            }
        }

        public void InstallAndStart()
        {
            InstallAndStart(wait: true);
        }

        public void InstallAndStart(bool wait)
        {
            InstallAndStart(ServiceStartupTypes.Automatic, wait);
        }

        public void InstallAndStart(ServiceStartupTypes startupType, bool wait)
        {
            Install(startupType);
            Start(wait);
        }

        public void Disable()
        {
            ConfigureStartup(ServiceStartupTypes.Disabled);
        }

        public void SetAutomaticStart()
        {
            ConfigureStartup(ServiceStartupTypes.Automatic);
        }

        public void SetManualStart()
        {
            ConfigureStartup(ServiceStartupTypes.Manual);
        }

        public KillServiceResult KillService()
        {
            Refresh();
            if (ServicePID == 0)
            {
                return KillServiceResult.NotRunning;
            }

            KillServiceResult result;
            try
            {
                var proc = Process.GetProcessById(ServicePID);
                proc.Kill();
                result = KillServiceResult.Killed;
            }
            catch
            {
                result = KillServiceResult.UnableToKill;
            }

            Refresh();
            return result;
        }
    }
}