using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace Alpha6Ops.Desktop;

public partial class App : Application
{
    private FlightSessionCoordinator? flightSession;
    private AccountFlightIdentity? flightSessionIdentity;
#if DIAGNOSTIC_HARNESSES
    private MapV2.NativeMapInputTrace? nativeMapTrace;
#endif
    private readonly System.Collections.Generic.List<FlightSessionCoordinator> diagnosticSessions=[];

    internal static FlightSessionCoordinator GetFlightSession(string? diagnosticDirectory) => GetFlightSession(diagnosticDirectory, null);

    // One application session per signed-in account workspace. Its flights, recovery and history
    // live under that workspace's directory; callers refuse to switch while a flight is open.
    internal static FlightSessionCoordinator GetFlightSession(string? diagnosticDirectory, DesktopAccountSession? account)
    {
        var app=(App)Current;
        if(diagnosticDirectory is null)
        {
            var directory=account?.Bootstrap is not null?account.DataDirectory:CrashReporter.RootDirectory;
            var identity=account?.FlightIdentity;
            if(app.flightSession is not null&&(!string.Equals(Path.GetFullPath(app.flightSession.DataDirectory),Path.GetFullPath(directory),StringComparison.OrdinalIgnoreCase)||app.flightSessionIdentity!=identity))
            {
                app.flightSession.Dispose();app.flightSession=null;
            }
            if(app.flightSession is null)
            {
                app.flightSession=new(directory,false,new WpfFlightRuntimeDispatcher(app.Dispatcher),null,identity);
                app.flightSessionIdentity=identity;
                app.flightSession.SetPilotName(identity?.DisplayName??UserPreferencesStore.Load()?.PilotName??"");
            }
            return app.flightSession;
        }
        var diagnostic=new FlightSessionCoordinator(diagnosticDirectory,true,new WpfFlightRuntimeDispatcher(app.Dispatcher));
        app.diagnosticSessions.Add(diagnostic);
        return diagnostic;
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
#if DIAGNOSTIC_HARNESSES
        if (e.Args.Length == 1 && e.Args[0] == "--preview-pilot")
        {
            // Explicit preview never loads credentials or another pilot's stored flights.
            OpenPilotPreview();
            return;
        }
        if (e.Args.Length == 1 && e.Args[0] == "--preview-login")
        {
            // Start at login every time; optional continuation uses isolated preview data only.
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            MainWindow = new AccountWindow(null, preview: true, openPilotPreview: () => { ShutdownMode = ShutdownMode.OnExplicitShutdown; OpenPilotPreview(); });
            MainWindow.Show();
            return;
        }
        if(e.Args.Length==4&&e.Args[0]=="--map-performance-checks")
        {
            CrashReporter.Install(this,e.Args[1]);var result=await MapV2.MapPerformanceChecks.RunAsync(e.Args[1],e.Args[2],double.Parse(e.Args[3],System.Globalization.CultureInfo.InvariantCulture));Shutdown(result);return;
        }
        if(e.Args.Length==2&&e.Args[0] is "--map-v2-checks" or "--map-contention-checks" or "--map-delivery-checks")
        {
            CrashReporter.Install(this,e.Args[1]);var result=await MapV2.MapV2Checks.RunAsync(e.Args[1],e.Args[0]=="--map-contention-checks",e.Args[0]=="--map-delivery-checks");Shutdown(result);return;
        }
        if(e.Args.Length==2&&e.Args[0]=="--map-model-checks")
        {
            Directory.CreateDirectory(e.Args[1]);var checks=new System.Collections.Generic.List<object>();var failed=0;
            try { void Record(bool passed,string name){checks.Add(new{passed,name});if(!passed)failed++;} MapV2.MapModelChecks.Run(Record);await MapV2.MapV2Checks.PumpChecks(Record); }
            catch(Exception error){checks.Add(new{passed=false,name=error.ToString()});failed++;}
            File.WriteAllText(Path.Combine(e.Args[1],"model.json"),JsonSerializer.Serialize(new{passed=failed==0,failed,checks}));Shutdown(failed==0?0:1);return;
        }
        if(e.Args.Length==2&&e.Args[0]=="--observed-cruise-native-checks")
        {
            Shutdown(ObservedCruiseNativeProfileChecks.Run(e.Args[1]));return;
        }
        if(e.Args.Length==3&&e.Args[0]=="--replay-journals")
        {
            try{RecordedFlightReplay.Run(e.Args[1],e.Args[2]);Shutdown(0);}
            catch(Exception error){Directory.CreateDirectory(e.Args[2]);File.WriteAllText(Path.Combine(e.Args[2],"error.txt"),error.ToString());Shutdown(1);}
            return;
        }
        var diagnosticOutput = e.Args.Length == 2 && e.Args[0] == "--smoke-test" ? e.Args[1] : null;
        CrashReporter.Install(this,diagnosticOutput);
        if(e.Args.Length==2&&e.Args[0]=="--activation-smoke")
        {
            var output=e.Args[1];Directory.CreateDirectory(output);
            if(!SingleInstance.TryAcquire()){File.WriteAllText(Path.Combine(output,"activation-smoke.json"),"{\"passed\":false,\"reason\":\"another instance was already running\"}");Shutdown();return;}
            var smokeWindow=new MainWindow(output);MainWindow=smokeWindow;smokeWindow.Show();smokeWindow.MinimizeToTray();
            var restored=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            SingleInstance.Listen(()=>smokeWindow.Dispatcher.BeginInvoke(new Action(()=>{smokeWindow.RestoreWindow();restored.TrySetResult(smokeWindow.IsVisible&&smokeWindow.WindowState==WindowState.Normal);})));
            using var child=Process.Start(new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=true});
            var completed=await Task.WhenAny(restored.Task,Task.Delay(TimeSpan.FromSeconds(8)));var passed=completed==restored.Task&&await restored.Task;
            if(child is not null)await child.WaitForExitAsync();
            File.WriteAllText(Path.Combine(output,"activation-smoke.json"),JsonSerializer.Serialize(new{passed,restored=smokeWindow.IsVisible,childExited=child?.HasExited??false}));
            smokeWindow.ExitApplication(output);return;
        }
        if (e.Args.Length == 2 && e.Args[0] is "--simconnect-probe" or "--simulator-launch-probe")
        {
            await SimConnectProbe.RunAsync(e.Args[1], e.Args[0] == "--simulator-launch-probe");
            Shutdown();
            return;
        }
#else
        string? diagnosticOutput=null;
        CrashReporter.Install(this);
#endif
        if (diagnosticOutput is null)
        {
            if (!SingleInstance.TryAcquire()) { Shutdown(); return; }
            ProductStorage.Initialize();
            var retention=ProductionRetention.Apply(ProductStorage.Current,DateTimeOffset.UtcNow);
            if(retention.Errors.Count>0)CrashReporter.Write("production_retention",null,new{errors=retention.Errors});
        }
        DesktopAccountSession? account = null;
        if (diagnosticOutput is null)
        {
            try
            {
                var configuration = IdentityConfiguration.Load();
                if (configuration is not null)
                {
                    account = new DesktopAccountSession(configuration);
                    var login = new AccountWindow(account, restore: true);
                    login.ShowDialog();
                    if (!login.Accepted) { Shutdown(); return; }
                }
            }
            catch (Exception)
            {
                MessageBox.Show("Account configuration could not be loaded. Ask the publisher to check alpha6-identity.json. Sign-in is required for a configured build.", "Sierra 4", MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(); return;
            }
        }
        var window = diagnosticOutput is not null ? new MainWindow(diagnosticOutput) : new MainWindow(GetFlightSession(null, account), null, account);
        MainWindow = window;
        // A workspace change replaces MainWindow; activation targets whichever window is current.
        if(diagnosticOutput is null)SingleInstance.Listen(()=>Dispatcher.BeginInvoke(new Action(()=>(MainWindow as MainWindow)?.RestoreWindow())));
        if (diagnosticOutput is not null)
        {
#if DIAGNOSTIC_HARNESSES
            await DesktopSmokeTest.RunAsync(window, diagnosticOutput);
#endif
            return;
        }
        window.Show();
#if DIAGNOSTIC_HARNESSES
        if(e.Args.Length==2&&e.Args[0]=="--trace-map-input")
        {
            try{nativeMapTrace=new(window,e.Args[1]);}
            catch(Exception error){CrashReporter.Write("map_input_trace_start",error);}
        }
#endif
    }

#if DIAGNOSTIC_HARNESSES
    private void OpenPilotPreview()
    {
        var previewRoot = Path.Combine(AppContext.BaseDirectory, "pilot-preview-data", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(previewRoot);
        CrashReporter.Install(this, previewRoot);
        MainWindow = new MainWindow(GetFlightSession(previewRoot), previewRoot, null, pilotPreview: true);
        MainWindow.Show();
    }
#endif

    protected override void OnExit(ExitEventArgs e)
    {
#if DIAGNOSTIC_HARNESSES
        nativeMapTrace?.Dispose();
#endif
        flightSession?.Dispose();
        foreach(var session in diagnosticSessions)session.Dispose();
        SingleInstance.Release();
        base.OnExit(e);
    }
}
