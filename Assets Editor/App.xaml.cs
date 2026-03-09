using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace Assets_Editor;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application {
    public App() {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnCurrentDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e) {
        try {
            string path = WriteCrashReport(e.Exception, "DispatcherUnhandledException");
            ErrorManager.ShowError($"The application has encountered an unexpected error and will close.\n\nCrash report:\n{path}");
        } catch {
            // ignore any crash-logger failures
        } finally {
            e.Handled = true;
            Current.Shutdown();
        }
    }

    private static void OnCurrentDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e) {
        try {
            Exception ex = e.ExceptionObject as Exception ?? new Exception("Unknown unhandled exception");
            WriteCrashReport(ex, "AppDomainUnhandledException");
        } catch {
            // ignore any crash-logger failures
        } finally {
            Current.Shutdown();
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e) {
        try {
            WriteCrashReport(e.Exception, "UnobservedTaskException");
        } catch {
            // ignore any crash-logger failures
        } finally {
            e.SetObserved();
        }
    }

    private static string WriteCrashReport(Exception exception, string source) {
        string baseDir = AppDomain.CurrentDomain.BaseDirectory ?? ".";
        string crashDir = Path.Combine(baseDir, "crash-logs");
        Directory.CreateDirectory(crashDir);

        string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        string fileName = $"crash-{timestamp}.txt";
        string fullPath = Path.Combine(crashDir, fileName);

        StringBuilder sb = new();
        sb.AppendLine($"Timestamp: {DateTime.Now:O}");
        sb.AppendLine($"Source: {source}");
        sb.AppendLine();
        sb.AppendLine("Exception:");
        sb.AppendLine(exception.ToString());

        Exception? inner = exception.InnerException;
        while (inner != null) {
            sb.AppendLine();
            sb.AppendLine("Inner Exception:");
            sb.AppendLine(inner.ToString());
            inner = inner.InnerException;
        }

        File.WriteAllText(fullPath, sb.ToString());
        return fullPath;
    }
}
