using EasyHook;
using SHDocVw;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace OsuDownloader
{

class IEHooker : Injectee.IHookerBase, IDisposable
{

	bool Disposed;

	ShellWindows ShWins;

	List<InternetExplorer> IEs = new List<InternetExplorer>();

	public bool IsHooking
	{
		get { return ShWins != null; }
	}

	public void SetHookState(bool request)
	{
		if (request)
		{
			ShWins = new ShellWindows();
			ShWins.WindowRegistered += OnShellWindowRegistered;
			OnShellWindowRegistered(0);
		}
		else
		{
			ShWins = null;
			IEs.Clear();
		}
	}

	~IEHooker()
	{
		Dispose(false);
	}

	public void Dispose()
	{
		Dispose(true);
	}

	void Dispose(bool disposing)
	{
		if (Disposed)
			return;

		Disposed = true;

		SetHookState(false);
	}

	private static int GetProcessIdFromIE(InternetExplorer ie)
	{
		uint pid;
		GetWindowThreadProcessId(new IntPtr(ie.HWND), out pid);
		return (int)pid;
	}

	void OnShellWindowRegistered(int lCookie)
	{
		var currentIEs = new List<InternetExplorer>();

		var activeIEs = from ie in ShWins.OfType<InternetExplorer>()
						where ie.Name.ToUpper().IndexOf("INTERNET EXPLORER") != -1
						select ie;

		foreach (var ie in activeIEs)
		{
			try
			{
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

	void InjectIfOfficialPage(object pDisp, ref object url)
	{
		try
		{
			Uri uri = new Uri(url as string);
			Debug.WriteLine(uri);
			if (uri.Host == "osu.ppy.sh")
			{
				var ie = (InternetExplorer)pDisp;
				int pid = GetProcessIdFromIE(ie);
				var ieProc = Process.GetProcessById(pid);

				if (RemoteHooking.IsX64Process(pid))
				{
					var iexplores = Process.GetProcessesByName("iexplore");
					var ieChildProcs = from iexplore in iexplores
									   where ParentProcessUtilities.GetParentProcess(iexplore.Handle).Id == pid
									   where RemoteHooking.IsX64Process(iexplore.Id) == false
									   orderby iexplore.Id
									   select iexplore;
					ieProc = ieChildProcs.First();
				}

				var fullName = GetType().Assembly.Location;
				RemoteHooking.Inject(ieProc.Id, InjectionOptions.DoNotRequireStrongName, fullName, fullName);
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

	[DllImport("user32.dll", SetLastError = true)]
	static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

}

////////////////////////////////////////////////////////////////////////////////////////////////////
/// <summary>
/// A utility class to determine a process parent. http://stackoverflow.com/a/3346055.
/// </summary>
////////////////////////////////////////////////////////////////////////////////////////////////////
[StructLayout(LayoutKind.Sequential)]
public struct ParentProcessUtilities
{
	// These members must match PROCESS_BASIC_INFORMATION
	internal IntPtr Reserved1;
	internal IntPtr PebBaseAddress;
	internal IntPtr Reserved2_0;
	internal IntPtr Reserved2_1;
	internal IntPtr UniqueProcessId;
	internal IntPtr InheritedFromUniqueProcessId;

	[DllImport("ntdll.dll")]
	private static extern int NtQueryInformationProcess(IntPtr processHandle, int processInformationClass,
			ref ParentProcessUtilities processInformation, int processInformationLength, out int returnLength);

	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>   Gets the parent process of the current process. </summary>
	///
	/// <returns>   An instance of the Process class. </returns>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	public static Process GetParentProcess()
	{
		return GetParentProcess(Process.GetCurrentProcess().Handle);
	}

	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>   Gets the parent process of specified process. </summary>
	///
	/// <param name="id">   The process id. </param>
	///
	/// <returns>   An instance of the Process class. </returns>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	public static Process GetParentProcess(int id)
	{
		Process process = Process.GetProcessById(id);
		return GetParentProcess(process.Handle);
	}

	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>   Gets the parent process of a specified process. </summary>
	///
	/// <exception cref="Win32Exception">   Thrown when a window 32 error condition occurs. </exception>
	///
	/// <param name="handle">   The process handle. </param>
	///
	/// <returns>   An instance of the Process class. </returns>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	public static Process GetParentProcess(IntPtr handle)
	{
		ParentProcessUtilities pbi = new ParentProcessUtilities();
		int returnLength;
		int status = NtQueryInformationProcess(handle, 0, ref pbi, Marshal.SizeOf(pbi), out returnLength);
		if (status != 0)
			throw new Win32Exception(status);

		try
		{
			return Process.GetProcessById(pbi.InheritedFromUniqueProcessId.ToInt32());
		}
		catch (ArgumentException)
		{
			// not found
			return null;
		}
	}
}

}
