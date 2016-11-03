using EasyHook;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Drawing;
using TestHook.Hooks.Remote;
using System.Threading.Tasks;
using System.Linq;
using System.Runtime.Remoting;

namespace TestHook.Hooks
{
    public class DemoHook : EasyHook.IEntryPoint
    {
        private BeepInterface remInterface = null;
        
        Stack<String> Queue = new Stack<String>();
        List<LocalHook> lHooks = new List<LocalHook>();

        public DemoHook(
            RemoteHooking.IContext InContext,
            string InChannelName, string hookName, string outChannelName)
        {
            string x = null;
            RemoteHooking.IpcCreateServer<TaskInterface>(ref x, WellKnownObjectMode.SingleCall);

            // connect to host...
            remInterface = RemoteHooking.IpcConnectClient<BeepInterface>(InChannelName);
            remInterface.RemoteTaskServerName = x;
            remInterface.Debug("Constructor");
            remInterface.Debug($"Hook Name = {hookName}");
        }

        public void Run(
            RemoteHooking.IContext InContext,
            string InChannelName, string hookName, string outChannelName)
        {
            var newHookName = hookName.ToUpper();
            try
            {
                var beepProcAddr = LocalHook.GetProcAddress("kernel32.dll", "Beep");
                var beepDel = new BeepDelegate(BeepHook);
                var beepHook = LocalHook.Create(beepProcAddr, beepDel, this);
                beepHook.ThreadACL.SetExclusiveACL(new int[] { 0 });
                lHooks.Add(beepHook);
                remInterface.Debug("Beep Hooked = " + beepHook.IsThreadIntercepted(0));

                var sendMessageProcAddr = LocalHook.GetProcAddress("user32.dll", "SendMessageW");
                var sendMessageDel = new SendMessageDelegate(SendMessageHook);
                var sendMessageHook = LocalHook.Create(sendMessageProcAddr, sendMessageDel, this);
                lHooks.Add(sendMessageHook);
                sendMessageHook.ThreadACL.SetExclusiveACL(new int[] { 0 });
                remInterface.Debug("SendMessage Hooked = " + sendMessageHook.IsThreadIntercepted(0));

                var drawTextExAddr = LocalHook.GetProcAddress("user32.dll", "DrawTextExA");
                var drawTextExDel = new DrawTextExDelegate(DrawTextExHook);
                var textHook = LocalHook.Create(drawTextExAddr, drawTextExDel, this);
                lHooks.Add(textHook);
                textHook.ThreadACL.SetExclusiveACL(new int[] { 0 });
                remInterface.Debug("Text Hooked = " + textHook.IsThreadIntercepted(0));

                var setTextProcAddr = LocalHook.GetProcAddress("user32.dll", "SetWindowTextW");
                var setTextDel = new SetWindowTextDelegate(SetWindowTextHook);
                var setTextHook = LocalHook.Create(setTextProcAddr, setTextDel, this);
                lHooks.Add(setTextHook);
                setTextHook.ThreadACL.SetExclusiveACL(new int[] { 0 });
                remInterface.Debug("SetText Hooked = " + setTextHook.IsThreadIntercepted(0));

                remInterface.IsInstalled(RemoteHooking.GetCurrentProcessId());

                RemoteHooking.WakeUpProcess();

                // wait for host process termination...
                try
                {
                    while (true)
                    {
                        Thread.Sleep(5000);
                        remInterface.Ping();
                    }
                }
                catch
                {
                    //Ignore
                }
            }
            catch (Exception ExtInfo)
            {
                remInterface.ReportException(ExtInfo);
                return;
            }
            finally
            {
                foreach (var hook in lHooks.Where(w => w != null))
                {
                    hook.Dispose();
                }

                NativeAPI.LhUninstallAllHooks();
                NativeAPI.LhWaitForPendingRemovals();
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool Beep(uint dwFreq, uint dwDuration);

        [return: MarshalAs(UnmanagedType.Bool)]
        delegate bool BeepDelegate(uint dwFreq, uint dwDuration);

        [return: MarshalAs(UnmanagedType.Bool)]
        bool BeepHook(uint dwFreq, uint dwDuration)
        {
            try
            {
                remInterface.Debug($"Beep ({dwFreq},{dwDuration})");
            }
            catch
            {
                //Ignore
            }

            return Beep(dwFreq, dwDuration);
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern IntPtr SendMessage(IntPtr hWnd, UInt32 Msg, IntPtr wParam, IntPtr lParam);

        delegate IntPtr SendMessageDelegate(IntPtr hWnd, UInt32 Msg, IntPtr wParam, IntPtr lParam);

        IntPtr SendMessageHook(IntPtr hWnd, UInt32 Msg, IntPtr wParam, IntPtr lParam)
        {
            string blah = string.Empty;
            if (Msg == 0x000C)
            {
                blah = Marshal.PtrToStringAuto(lParam);
            }

            Task.Factory.StartNew(() =>
            {
                try
                {
                    if (string.IsNullOrWhiteSpace(blah) == false)
                        remInterface.Debug("SendMessage Text = " + blah);

                    remInterface.HandleSendMessage(hWnd, Msg, wParam, lParam);
                }
                catch (Exception)
                {
                    //Ignore
                }
            });

            return SendMessage(hWnd, Msg, wParam, lParam);
        }

        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        static extern IntPtr SendMessageW(IntPtr hWnd, UInt32 Msg, IntPtr wParam, [MarshalAs(UnmanagedType.LPWStr)] string lParam);

        delegate IntPtr SendMessageDelegateW(IntPtr hWnd, UInt32 Msg, IntPtr wParam, [MarshalAs(UnmanagedType.LPWStr)] string lParam);

        IntPtr SendMessageHookW(IntPtr hWnd, UInt32 Msg, IntPtr wParam, string lParam)
        {
            Task.Factory.StartNew(() =>
            {
                try
                {
                    remInterface.Debug($"SendMessageW ({lParam})");
                }
                catch (Exception)
                {
                    //Ignore
                }
            });

            return SendMessageW(hWnd, Msg, wParam, lParam);
        }


        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern bool SetWindowText(IntPtr hwnd, IntPtr lpString);

        [return: MarshalAs(UnmanagedType.Bool)]
        delegate bool SetWindowTextDelegate(IntPtr hwnd, IntPtr lpString);

        [return: MarshalAs(UnmanagedType.Bool)]
        bool SetWindowTextHook(IntPtr hwnd, IntPtr lpString)
        {
            Task.Factory.StartNew(() =>
            {
                var tmp = Marshal.PtrToStringAuto(lpString);
                try
                {
                    remInterface.Debug($"SetText ({hwnd},'{tmp}')");
                }
                catch (Exception)
                {
                    //Ignore
                }
            });

            return SetWindowText(hwnd, lpString);
        }

        [DllImport("user32.dll", SetLastError = true)]
        static extern int DrawTextEx(IntPtr hdc, string lpchText, int cchText,
                ref RECT lprc, uint dwDTFormat, ref DRAWTEXTPARAMS lpDTParams);

        private delegate int DrawTextExDelegate(IntPtr hdc, string lpchText, int cchText,
                ref RECT lprc, uint dwDTFormat, ref DRAWTEXTPARAMS lpDTParams);

        private int DrawTextExHook(IntPtr hdc, string lpchText, int cchText, ref RECT lprc,
                                             uint dwDTFormat, ref DRAWTEXTPARAMS lpDTParams)
        {
            try
            {
                remInterface.Debug($"IntPtr {hdc}, string {lpchText}, int {cchText}");
            }
            catch
            {
                //Ignore
            }

            return DrawTextEx(hdc, lpchText, cchText, ref lprc, dwDTFormat, ref lpDTParams);
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DRAWTEXTPARAMS
        {
            public uint cbSize;
            public int iTabLength;
            public int iLeftMargin;
            public int iRightMargin;
            public uint uiLengthDrawn;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left, Top, Right, Bottom;

            public RECT(int left, int top, int right, int bottom)
            {
                Left = left;
                Top = top;
                Right = right;
                Bottom = bottom;
            }

            public RECT(System.Drawing.Rectangle r) : this(r.Left, r.Top, r.Right, r.Bottom) { }

            public int X
            {
                get { return Left; }
                set { Right -= (Left - value); Left = value; }
            }

            public int Y
            {
                get { return Top; }
                set { Bottom -= (Top - value); Top = value; }
            }

            public int Height
            {
                get { return Bottom - Top; }
                set { Bottom = value + Top; }
            }

            public int Width
            {
                get { return Right - Left; }
                set { Right = value + Left; }
            }

            public System.Drawing.Point Location
            {
                get { return new System.Drawing.Point(Left, Top); }
                set { X = value.X; Y = value.Y; }
            }

            public System.Drawing.Size Size
            {
                get { return new System.Drawing.Size(Width, Height); }
                set { Width = value.Width; Height = value.Height; }
            }

            public static implicit operator System.Drawing.Rectangle(RECT r)
            {
                return new System.Drawing.Rectangle(r.Left, r.Top, r.Width, r.Height);
            }

            public static implicit operator RECT(System.Drawing.Rectangle r)
            {
                return new RECT(r);
            }

            public static bool operator ==(RECT r1, RECT r2)
            {
                return r1.Equals(r2);
            }

            public static bool operator !=(RECT r1, RECT r2)
            {
                return !r1.Equals(r2);
            }

            public bool Equals(RECT r)
            {
                return r.Left == Left && r.Top == Top && r.Right == Right && r.Bottom == Bottom;
            }

            public override bool Equals(object obj)
            {
                if (obj is RECT)
                    return Equals((RECT)obj);
                else if (obj is System.Drawing.Rectangle)
                    return Equals(new RECT((System.Drawing.Rectangle)obj));
                return false;
            }

            public override int GetHashCode()
            {
                return ((System.Drawing.Rectangle)this).GetHashCode();
            }

            public override string ToString()
            {
                return string.Format(System.Globalization.CultureInfo.CurrentCulture, "{{Left={0},Top={1},Right={2},Bottom={3}}}", Left, Top, Right, Bottom);
            }
        }
    }
}
