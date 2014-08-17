using EasyHook;
using SHDocVw;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.IO;
using System.Windows.Threading;

namespace OsuDownloader.Injectee
{
class ForegroundHooker : IHookerBase, IDisposable
{

	/// <summary>   Invoked with foreground process name. </summary>
	public event Action<Process> OnForegroundChanged = delegate { };

	static Dictionary<IntPtr, ForegroundHooker> Owner = new Dictionary<IntPtr, ForegroundHooker>();

	IntPtr HHook = IntPtr.Zero;

	ShellWindows ShWins;

	List<InternetExplorer> IEs = new List<InternetExplorer>();

	bool Disposed;

	public bool IsHooking
	{
		get { return HHook != IntPtr.Zero; }
	}

	public void SetHookState(bool request)
	{
		if (request && HHook == IntPtr.Zero)
		{
			// Listen for name change changes across all processes/threads on current desktop...
			HHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero,
									ForegroundChangeDele, 0, 0, WINEVENT_OUTOFCONTEXT);

			if (HHook == IntPtr.Zero)
			{
				return;
			}

			Owner[HHook] = this;
		}
		else if (request == false && HHook != IntPtr.Zero)
		{
			UnhookWinEvent(HHook);
			HHook = IntPtr.Zero;
		}
	}

	#region IDisposable and finalizer

	void Dispose(bool disposing)
	{
		if (Disposed)
			return;

		Disposed = true;

		SetHookState(false);
	}

	public void Dispose()
	{
		Dispose(true);
	}

	~ForegroundHooker()
	{
		Dispose(false);
	}

	#endregion

	delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject,
								   int idChild, uint dwEventThread, uint dwmsEventTime);

	[DllImport("user32.dll")]
	static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
										 WinEventDelegate lpfnWinEventProc, uint idProcess,
										 uint idThread, uint dwFlags);

	[DllImport("user32.dll")]
	static extern bool UnhookWinEvent(IntPtr hWinEventHook);

	[DllImport("user32.dll", SetLastError = true)]
	static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

	const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
	const uint WINEVENT_OUTOFCONTEXT = 0;

	// Need to ensure delegate is not collected while we're using it,
	// storing it in a class field is simplest way to do this.
	static WinEventDelegate ForegroundChangeDele = new WinEventDelegate(WinEventProc);

	static void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hWnd, int idObject,
							 int idChild, uint dwEventThread, uint dwmsEventTime)
	{
		try
		{
			uint pid;
			if (hWnd != IntPtr.Zero && GetWindowThreadProcessId(hWnd, out pid) != 0)
			{
				var fgProc = Process.GetProcessById((int)pid);
				var instance = Owner[hWinEventHook];
				instance.OnForegroundChanged(fgProc);
			}
		}
		catch (Exception e)
		{
			MainWindowViewModel.LogException(e);
		}
	}

	/// <summary>   Monitor all internet explorer. </summary>
	public void MonitorIE()
	{
		ShWins = new ShellWindows();
		ShWins.WindowRegistered += OnShellWindowRegistered;
		OnShellWindowRegistered(0);
	}

	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>   Monitor specified internet explorers. </summary>
	///
	/// <param name="procs" type="Process[]">   Internet explorer targetted. </param>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	public void MonitorIE(Process[] procs)
	{
		var shellWindows = new SHDocVw.ShellWindows();
		foreach (object shellWin in shellWindows)
		{
			try
			{
				if (shellWin is InternetExplorer == false)
					continue;

				var ie = (InternetExplorer)shellWin;
				int pid = GetProcessIdFromHwnd(ie);
				var proc = Process.GetProcessById(pid);
				if (procs.Contains(proc))
				{
					ie.NavigateComplete2 += RefreshIfOfficialPage;
				}
			}
			catch { }
		}
	}

	private static int GetProcessIdFromHwnd(InternetExplorer ie)
	{
		uint pid;
		GetWindowThreadProcessId(new IntPtr(ie.HWND), out pid);
		return (int)pid;
	}

	void OnShellWindowRegistered(int lCookie)
	{
		var currentIEs = new List<InternetExplorer>();

		foreach (object shellWin in ShWins)
		{
			try
			{
				if (shellWin is InternetExplorer == false)
					continue;

				var ie = (InternetExplorer)shellWin;
				currentIEs.Add(ie);
				if (IEs.Contains(ie) == false)
				{
					ie.NavigateComplete2 += InjectIfOfficialPage;
				}
			}
			catch { }
		}

		IEs = currentIEs;
	}

	void RefreshIfOfficialPage(object pDisp, ref object url)
	{
		try
		{
			Debug.WriteLine(url);
			Uri uri = new Uri(url as string);
			if (uri.Host == "osu.ppy.sh")
			{
				Properties.Settings.Default.OfficialSession = GetCookie();
			}
		}
		catch (Exception e)
		{
			MainWindowViewModel.LogException(e);
		}
	}

	static string GetCookie()
	{
		const int INTERNET_COOKIE_HTTPONLY = 0x2000;
		const int flag = INTERNET_COOKIE_HTTPONLY;

		uint size = 0;
		InternetGetCookieEx("http://osu.ppy.sh/", null, null, ref size, flag, IntPtr.Zero);

		var buffer = new StringBuilder((int)size);
		InternetGetCookieEx("http://osu.ppy.sh/", null, buffer, ref size, flag, IntPtr.Zero);

		return buffer.ToString();
	}

	void InjectIfOfficialPage(object pDisp, ref object url)
	{
		try
		{
			Uri uri = new Uri(url as string);
			Debug.WriteLine(uri);
			if (uri.Host == "osu.ppy.sh")
			{
				var ie = (InternetExplorer)pDisp;

				uint pid;
				GetWindowThreadProcessId(new IntPtr(ie.HWND), out pid);

				bool isOwner;
				var mutex = new Mutex(true, "osu! Beatmap Downloader IE login mutex id_" + pid, out isOwner);
				if (isOwner == false)
				{
					return;
				}
				mutex.ReleaseMutex();

				var fullName = GetType().Assembly.Location;
				var fullName64 = Path.Combine(Path.GetDirectoryName(fullName), "osuDownloader64.exe");

				RemoteHooking.Inject((int)pid, InjectionOptions.DoNotRequireStrongName, fullName, fullName64);
			}
		}
		catch (Exception e)
		{
			MainWindowViewModel.LogException(e);
		}
	}

	[DllImport("wininet.dll", CharSet = CharSet.Auto, SetLastError = true)]
	static extern bool InternetGetCookieEx(string pchURL, string pchCookieName, StringBuilder pchCookieData,
										   ref uint pcchCookieData, int dwFlags, IntPtr lpReserved);
}
}
