using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EasyHook;
using System.Runtime.InteropServices;

namespace RemoteDown
{

public class OsuInjectee : EasyHook.IEntryPoint
{
	OsuDownloader.InvokeDownload Interface;
	LocalHook CreateFileHook;
	Queue<string> Queue = new Queue<string>();

	public OsuInjectee(RemoteHooking.IContext context, string channelName)
	{
		// connect to host...
		Interface = RemoteHooking.IpcConnectClient<OsuDownloader.InvokeDownload>(channelName);

		Interface.Ping();
	}

	public void Run(RemoteHooking.IContext context, string channelName)
	{
		// install hook...
		try
		{
			CreateFileHook = LocalHook.Create(
								 LocalHook.GetProcAddress("shell32.dll", "ShellExecuteExW"),
								 new DShellExecuteEx(ShellExecuteEx_Hooked),
								 this);

			CreateFileHook.ThreadACL.SetExclusiveACL(new int[] { 0 });
		}
		catch (Exception extInfo)
		{
			Interface.ReportException(extInfo);

			return;
		}

		Interface.IsInstalled(RemoteHooking.GetCurrentProcessId());

		RemoteHooking.WakeUpProcess();

		// wait for host process termination...
		try
		{
			while (true)
			{
				Thread.Sleep(500);

				// transmit newly monitored file accesses...
				if (Queue.Count > 0)
				{
					string[] package = null;

					lock (Queue)
					{
						package = Queue.ToArray();

						Queue.Clear();
					}

					foreach (var fileName in package)
					{
						Interface.OnBeatmapBrowse(RemoteHooking.GetCurrentProcessId(), fileName);
					}
				}
				else
					Interface.Ping();
			}
		}
		catch
		{
			// Ping() will raise an exception if host is unreachable
		}
		finally
		{
			Interface.OnTerminate();
		}
	}

	#region ShellExecuteEx pinvoke

	[StructLayout(LayoutKind.Sequential)]
	public struct SHELLEXECUTEINFO
	{
		public int cbSize;
		public uint fMask;
		public IntPtr hwnd;
		[MarshalAs(UnmanagedType.LPTStr)]
		public string lpVerb;
		[MarshalAs(UnmanagedType.LPTStr)]
		public string lpFile;
		[MarshalAs(UnmanagedType.LPTStr)]
		public string lpParameters;
		[MarshalAs(UnmanagedType.LPTStr)]
		public string lpDirectory;
		public int nShow;
		public IntPtr hInstApp;
		public IntPtr lpIDList;
		[MarshalAs(UnmanagedType.LPTStr)]
		public string lpClass;
		public IntPtr hkeyClass;
		public uint dwHotKey;
		public IntPtr hIcon;
		public IntPtr hProcess;
	}

	[DllImport("shell32.dll", CharSet = CharSet.Auto)]
	static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);

	[UnmanagedFunctionPointer(CallingConvention.StdCall,
							  CharSet = CharSet.Unicode,
							  SetLastError = true)]
	delegate bool DShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);

	static bool ShellExecuteEx_Hooked(ref SHELLEXECUTEINFO lpExecInfo)
	{
		try
		{
			if (lpExecInfo.lpFile.StartsWith("http://osu.ppy.sh/b/") ||
				lpExecInfo.lpFile.StartsWith("http://osu.ppy.sh/s/"))
			{
				OsuInjectee This = (OsuInjectee)HookRuntimeInfo.Callback;
				lock (This.Queue)
				{
					This.Queue.Enqueue(lpExecInfo.lpFile);
				}
			}

			return true;
		}
		catch
		{
		}

		//call original API...
		return ShellExecuteEx(ref lpExecInfo);
	}

	#endregion
}

}
