using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace OsuDownloader.Injectee
{
class WinEventHooker
{

	/// <summary>   Invoked with foreground process name. </summary>
	public event Action<string> OnForegroundChanged;

	static Dictionary<IntPtr, WinEventHooker> Owner = new Dictionary<IntPtr, WinEventHooker>();

	IntPtr HHook;

	DispatcherTimer Timer;

	public WinEventHooker()
	{
		// Listen for name change changes across all processes/threads on current desktop...
		HHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero,
								ForegroundChangeDele, 0, 0, WINEVENT_OUTOFCONTEXT);

		if (HHook == IntPtr.Zero)
		{
			return;
		}

		Owner[HHook] = this;

		Timer = new DispatcherTimer();
		Timer.Interval = TimeSpan.FromMilliseconds(200);
		Timer.Tick += timer_Tick;
	}

	~WinEventHooker()
	{
		if (HHook != IntPtr.Zero)
			UnhookWinEvent(HHook);
	}

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
			if (hWnd != IntPtr.Zero)
			{
				uint pid;
				uint tid = GetWindowThreadProcessId(hWnd, out pid);

				if (tid == 0 && Marshal.GetLastWin32Error() != 0)
				{
					throw new ApplicationException("GetWindowThreadProcessId failed.");
				}

				var fgProc = System.Diagnostics.Process.GetProcessById((int)pid);
				var instance = Owner[hWinEventHook];
				instance.OnForegroundChanged(fgProc.ProcessName);

				// Monitor for cookie
				if (fgProc.ProcessName == "iexplore")
				{
					if (instance.Timer.IsEnabled == false)
					{
						instance.Timer.Start();
					}
				}
			}
		}
		catch (Exception e)
		{
			MainWindowViewModel.LogException(e);
		}
	}

	void timer_Tick(object sender, EventArgs e)
	{
		SweepCookie();
	}

	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>
	/// Retrieve cookie from opened IE window. It doesn't renew setting if no opened official page
	/// exists.
	/// </summary>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	private void SweepCookie()
	{
		var shellWindows = new SHDocVw.ShellWindows();

		string longestCookie = string.Empty;
		foreach (SHDocVw.WebBrowser wb in shellWindows)
		{
			var url = new Uri(wb.LocationURL);
			if (url.Host == "osu.ppy.sh")
			{
				mshtml.IHTMLDocument2 doc2 = (mshtml.IHTMLDocument2)wb.Document;
				if (longestCookie.Length < doc2.cookie.Length)
				{
					longestCookie = doc2.cookie;
				}
			}
		}

		if (longestCookie == string.Empty)
		{
			if (Timer.IsEnabled)
			{
				Timer.Stop();
			}
			return;
		}

		var setting = Properties.Settings.Default;
		if (setting.OfficialSession != longestCookie)
		{
			setting.OfficialSession = longestCookie;
		}
	}

}
}
