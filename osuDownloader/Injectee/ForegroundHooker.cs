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

}
}
