using EasyHook;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Remoting;
using System.Text;
using System.Threading.Tasks;
using TestHook.Hooks.Remote;

namespace TestHook.Injector
{
    class Program
    {
        private static TaskInterface taskInterface = null;

        static void Main(string[] args)
        {
            var exeName = @"C:\Users\jeff\Documents\GitHub\RefApp\bin\Release\RefApp.exe";
            var pid = 0;
            var proc = Process.GetProcesses().FirstOrDefault(i => i.ProcessName.StartsWith("RefApp"));
            //var proc = Process.GetProcessesByName("Notepad").FirstOrDefault();
            if (proc != null)
                pid = proc.Id;

            string channelName = null;
            BeepInterface beepInterface = new BeepInterface();
            var blah = RemoteHooking.IpcCreateServer<BeepInterface>(ref channelName, WellKnownObjectMode.SingleCall, beepInterface);

            var injectionDll = "TestHook.Hooks.dll";

            try
            {
                string outChannel = "";
                if (pid == 0)
                {
                    RemoteHooking.CreateAndInject(exeName, "", 0, InjectionOptions.DoNotRequireStrongName, injectionDll, injectionDll, out pid, channelName, "", outChannel);
                    Console.WriteLine("Created and injected process {0}", pid);
                }
                else
                {
                    RemoteHooking.Inject(pid, InjectionOptions.DoNotRequireStrongName, injectionDll, injectionDll, channelName, "", outChannel);
                    Console.WriteLine("Injected to process {0}", pid);
                }

                taskInterface = RemoteHooking.IpcConnectClient<TaskInterface>(beepInterface.RemoteTaskServerName);
                taskInterface.Beep(800, 1500);
            }
            catch (Exception ex)
            {
                var x = ex.Message;
                return;
            }

            var exitCmds = new List<string> { "X", "x", "Exit", "EXIT", "Bye", "BYE" };
            while (true)
            {
                var cmd = Console.ReadLine();
                if (exitCmds.Contains(cmd))
                    break;

                if (cmd.Contains("beep"))
                    taskInterface.Beep(100, 2000);

                if (cmd.Contains("SetWindowText"))
                {
                    var paramVals = cmd.Split(' ');
                    if(paramVals.Count() == 3)
                    {
                        var hwnd = new IntPtr(Convert.ToInt32(paramVals[1]));
                        var val = paramVals[2];
                        taskInterface.SetWindowText(hwnd, val);
                    }
                }

                if (cmd.Contains("ClickButton"))
                {
                    var paramVals = cmd.Split(' ');
                    if (paramVals.Count() == 2)
                    {
                        var hwnd = new IntPtr(Convert.ToInt32(paramVals[1]));
                        taskInterface.ClickButton(hwnd);
                    }
                }
            }
        }
    }
}
