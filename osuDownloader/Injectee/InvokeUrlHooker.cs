using EasyHook;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace OsuDownloader.Injectee
{

class InvokeUrlHooker : IHookerBase, IDisposable
{
	static string DownloadDir = "C:\\";

	public bool IsHooking
	{
		get
		{
			return ShellExecuteExHook != null;
		}
	}

	/// <summary>   ShellExecuteEx hook. Intercept url opens. </summary>
	LocalHook ShellExecuteExHook;
	/// <summary>   ShowWindow function hook. This is necessary during fullscreen mode. </summary>
	LocalHook ShowWindowHook;

	/// <summary>   true if disposed. </summary>
	private bool Disposed;

	public void SetHookState(bool request)
	{
		if (request)
		{
			if (ShellExecuteExHook != null)
				SetHookState(false);

			ShellExecuteExHook = LocalHook.Create(
									 LocalHook.GetProcAddress("shell32.dll", "ShellExecuteExW"),
									 new DShellExecuteEx(ShellExecuteEx_Hooked), this);

			ShowWindowHook = LocalHook.Create(
								 LocalHook.GetProcAddress("user32.dll", "ShowWindow"),
								 new DShowWindow(ShowWindow_Hooked), this);

			ResetHookAcl(HookManager.HookingThreadIds.ToArray());
		}
		else
		{
			if (ShellExecuteExHook != null)
			{
				ShellExecuteExHook.Dispose();
				ShellExecuteExHook = null;
			}
			if (ShowWindowHook != null)
			{
				ShowWindowHook.Dispose();
				ShowWindowHook = null;
			}
		}
	}

	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>   Resets the hook ACL described by hookThreadIds. </summary>
	///
	/// <param name="hookThreadIds">    List of identifiers for the hook threads. </param>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	public void ResetHookAcl(int[] hookThreadIds)
	{
		if (ShellExecuteExHook != null)
		{
			var threadIds = new List<int>();
			foreach (ProcessThread thread in Process.GetCurrentProcess().Threads)
			{
				threadIds.Add(thread.Id);
			}
			ShellExecuteExHook.ThreadACL.SetInclusiveACL(threadIds.ToArray());
		}
		if (ShowWindowHook != null)
		{
			ShowWindowHook.ThreadACL.SetExclusiveACL(hookThreadIds);
		}
	}

	#region IDisposable and finalizer

	void Dispose(bool disposing)
	{
		if (Disposed)
			return;

		Disposed = true;

		if (disposing)
		{
			SetHookState(false);
		}
	}

	public void Dispose()
	{
		Dispose(true);
		GC.SuppressFinalize(this);
	}

	~InvokeUrlHooker()
	{
		Dispose(false);
	}

	#endregion


	#region ShowWindow pinvoke

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	static extern bool ShowWindow(IntPtr hWnd, ShowWindowCommands nCmdShow);

	[UnmanagedFunctionPointer(CallingConvention.StdCall)]
	delegate bool DShowWindow(IntPtr hWnd, ShowWindowCommands nCmdShow);

	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>
	/// Hook function which corresponds to ShowWIndow. It seems osu minimize own window after
	/// ShellExecuteEx during fullscreen mode. So it's necessary.
	/// </summary>
	///
	/// <param name="hWnd">     Window handle. </param>
	/// <param name="nCmdShow"> ShowWindowCommands. </param>
	///
	/// <returns>   Always true. </returns>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	static bool ShowWindow_Hooked(IntPtr hWnd, ShowWindowCommands nCmdShow)
	{
		if (nCmdShow == ShowWindowCommands.Minimize)
		{
			return true;
		}
		return ShowWindow(hWnd, nCmdShow);
	}

	#endregion

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
		public ShowWindowCommands nShow;
		public IntPtr hInstApp;
		public IntPtr lpIDList;
		[MarshalAs(UnmanagedType.LPTStr)]
		public string lpClass;
		public IntPtr hkeyClass;
		public uint dwHotKey;
		public IntPtr hIcon;
		public IntPtr hProcess;
	}

	[DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);

	[UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode, SetLastError = true)]
	delegate bool DShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);

	static bool ShellExecuteEx_Hooked(ref SHELLEXECUTEINFO lpExecInfo)
	{
		try
		{
			var uri = new Uri(lpExecInfo.lpFile);

			//"https?://osu\.ppy\.sh/[bsd]/"
			//"https?://bloodcat.com/osu/m/"
			if (uri.Host == "osu.ppy.sh" && "b/d/s/".Contains(uri.Segments[1]) ||
				uri.Host == "bloodcat.com" && uri.AbsolutePath.StartsWith("/osu/m/"))
			{
				string request = lpExecInfo.lpFile;

				var worker = new Thread(() => new OszDownloader().AutomaticDownload(request));
				worker.SetApartmentState(ApartmentState.STA);
				worker.Start();

				return true;
			}
		}
		catch (Exception e)
		{
		}

		//call original API...
		return ShellExecuteEx(ref lpExecInfo);
	}

	#endregion
}

public enum ShowWindowCommands
{
	/// <summary>   Hides the window and activates another window. </summary>
	Hide = 0,

	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>
	/// Activates and displays a window. If the window is minimized or maximized, the system restores
	/// it to its original size and position. An application should specify this flag when displaying
	/// the window for the first time.
	/// </summary>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	Normal = 1,

	/// <summary>   Activates the window and displays it as a minimized window. </summary>
	ShowMinimized = 2,

	/// <summary>   Maximizes the specified window. </summary>
	Maximize = 3,

	/// <summary>   Activates the window and displays it as a maximized window. </summary>
	ShowMaximized = 3,

	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>
	/// Displays a window in its most recent size and position. This value is similar to
	/// <see cref="Win32.ShowWindowCommand.Normal"/>, except the window is not activated.
	/// </summary>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	ShowNoActivate = 4,

	/// <summary>   Activates the window and displays it in its current size and position. </summary>
	Show = 5,

	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>
	/// Minimizes the specified window and activates the next top-level window in the Z order.
	/// </summary>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	Minimize = 6,

	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>
	/// Displays the window as a minimized window. This value is similar to
	/// <see cref="Win32.ShowWindowCommand.ShowMinimized"/>, except the
	/// window is not activated.
	/// </summary>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	ShowMinNoActive = 7,

	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>
	/// Displays the window in its current size and position. This value is similar to
	/// <see cref="Win32.ShowWindowCommand.Show"/>, except the window is not activated.
	/// </summary>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	ShowNA = 8,

	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>
	/// Activates and displays the window. If the window is minimized or maximized, the system
	/// restores it to its original size and position. An application should specify this flag when
	/// restoring a minimized window.
	/// </summary>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	Restore = 9,

	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>
	/// Sets the show state based on the SW_* value specified in the STARTUPINFO structure passed to
	/// the CreateProcess function by the program that started the application.
	/// </summary>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	ShowDefault = 10,

	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>
	/// <b>Windows 2000/XP:</b> Minimizes a window, even if the thread that owns the window is not
	/// responding. This flag should only be used when minimizing windows from a different thread.
	/// </summary>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	ForceMinimize = 11
}

}
