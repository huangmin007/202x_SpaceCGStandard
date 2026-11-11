using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace SpaceCG.Extensions
{
    public static class ApplicationExtensions
    {
        private static Mutex _mutex;

        /// <summary>
        /// 初使化应用程序
        /// </summary>
        /// <param name="application"></param>
        public static void InitializeStandard(this Application application)
        {
            var nameSpace = application.GetType().Namespace;
            var fileName = Process.GetCurrentProcess().MainModule.FileName;
            var mutexName = $"{Path.GetFileNameWithoutExtension(fileName)}_{nameSpace}_Mutex";
            Debug.WriteLine($"MutexName: {mutexName}");

            application.ShutdownMode = ShutdownMode.OnMainWindowClose;
            _mutex = new Mutex(true, mutexName, out var isNewInstance);

            if (!isNewInstance)
            {
                MessageBox.Show($"应用程序 {fileName} 正在运行中，请勿重复启动！", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                application.Shutdown();
                return;
            }

            Trace.Listeners.Add(new Diagnostics.LoggerTraceListener());
            Trace.TraceInformation($"应用程序启动");

            application.Exit += Application_Exit;
            application.DispatcherUnhandledException += Application_DispatcherUnhandledException;
        }

        private static void Application_Exit(object sender, ExitEventArgs e)
        {
            Trace.TraceInformation($"应用程序退出");
        }

        private static void Application_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Trace.TraceError($"应用程序未处理的异常：{e.Exception}");
        }
    }
}
