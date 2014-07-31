using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using EasyHook;
using System.Runtime.InteropServices;
using System.Net;
using System.Diagnostics;
using Newtonsoft.Json.Linq;
using System.IO;
using System.Runtime.Remoting.Channels;
using System.Runtime.Remoting.Channels.Ipc;
using System.Runtime.Serialization.Formatters;
using System.ServiceModel;
using System.ComponentModel;

namespace RemoteDown
{

[ServiceBehavior(InstanceContextMode = InstanceContextMode.Single)]
public class OsuInjectee : EasyHook.IEntryPoint, OsuDownloader.IOsuInjectee
{
	static string DownloadDir = "C:\\";

	/// <summary>   ShellExecuteEx hook. Intercept url opens. </summary>
	static LocalHook ShellExecuteExHook;
	/// <summary>   ShowWindow function hook. This is necessary during fullscreen mode. </summary>
	LocalHook ShowWindowHook;

	Queue<string> Queue = new Queue<string>();
	ManualResetEvent QueueAppended;

	ServiceHost InjecteeHost;
	List<OsuDownloader.ICallback> Callbacks = new List<OsuDownloader.ICallback>();

	/// <summary>   This is used to determine whether received no connection. </summary>
	bool LastConnectionFaulted;

	/// <summary>   Identifier for the hooking thread. </summary>
	static int HookingThreadId;

	public OsuInjectee(RemoteHooking.IContext context)
	{
		InjecteeHost = new ServiceHost(this, new Uri[] { new Uri("net.pipe://localhost") });
		InjecteeHost.AddServiceEndpoint(typeof(OsuDownloader.IOsuInjectee),
										new NetNamedPipeBinding(), "osuBeatmapHooker");
		InjecteeHost.Open();
	}

	public void Run(RemoteHooking.IContext context)
	{
		HookingThreadId = (int)GetCurrentThreadId();
		try
		{
			EnableHook();
		}
		catch (Exception extInfo)
		{
			OsuDownloader.OsuHooker.LogException(extInfo);
		}

		RemoteHooking.WakeUpProcess();

		QueueAppended = new ManualResetEvent(false);

		// wait for host process termination...
		try
		{
			while (LastConnectionFaulted == false)
			{
				QueueAppended.WaitOne(500);
				QueueAppended.Reset();

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
						try
						{
							BloodcatDownload(fileName);
						}
						catch (Exception e)
						{
							OsuDownloader.OsuHooker.LogException(e);
						}
					}
				}
			}
		}
		catch (Exception e)
		{
			OsuDownloader.OsuHooker.LogException(e);
		}
		finally
		{
			DisableHook();
			InjecteeHost.Close();
			InjecteeHost = null;
		}
	}

	#region WCF Implementation

	public void Subscribe()
	{
		var callback = OperationContext.Current.GetCallbackChannel<OsuDownloader.ICallback>();
		callback.Installed();
		(callback as ICommunicationObject).Faulted += ClientFaulted;

		Callbacks.Add(callback);
	}

	void ClientFaulted(object sender, EventArgs e)
	{
		foreach (var callback in Callbacks.ToArray())
		{
			if ((callback as ICommunicationObject).State == CommunicationState.Faulted)
			{
				Callbacks.Remove(callback);
				if (Callbacks.Count == 0)
				{
					LastConnectionFaulted = true;
				}
				break;
			}
		}
	}

	public void Unsubscribe()
	{
		var callback = OperationContext.Current.GetCallbackChannel<OsuDownloader.ICallback>();
		Callbacks.Remove(callback);

		if (Callbacks.Count <= 0)
		{
			DisableHook();
			// TODO: Additional cleaner code.
		}
	}

	public bool IsHookEnabled()
	{
		return ShellExecuteExHook != null;
	}

	public void DisableHook()
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

		NotifyHookSwitch();
	}

	public void EnableHook()
	{
		ShellExecuteExHook = LocalHook.Create(
								 LocalHook.GetProcAddress("shell32.dll", "ShellExecuteExW"),
								 new DShellExecuteEx(ShellExecuteEx_Hooked), this);

		ShowWindowHook = LocalHook.Create(
							 LocalHook.GetProcAddress("user32.dll", "ShowWindow"),
							 new DShowWindow(ShowWindow_Hooked), this);

		File.AppendAllLines("C:\\ThreadList.txt", new string[]
		{
			"EnableHook() : " + GetCurrentThreadId(),
			" - IsThreadIntercepted : " + ShellExecuteExHook.IsThreadIntercepted((int)GetCurrentThreadId()),
		});
		var threadList = new List<int>();
		foreach (ProcessThread thread in Process.GetCurrentProcess().Threads)
		{
			threadList.Add(thread.Id);
		}
		threadList.Remove(HookingThreadId);

		ShellExecuteExHook.ThreadACL.SetInclusiveACL(threadList.ToArray());
		ShowWindowHook.ThreadACL.SetExclusiveACL(new int[] { HookingThreadId });

		NotifyHookSwitch();
	}

	void NotifyHookSwitch()
	{
		foreach (var callback in Callbacks.ToArray())
		{
			try
			{
				callback.HookSwitched(ShellExecuteExHook != null);
			}
			catch (Exception)
			{
				// Remove callback when it's terminal is terminated.
				Callbacks.Remove(callback);
			}
		}
	}

	#endregion

	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>   Download from bloodcat mirror. </summary>
	///
	/// <param name="request"> Official beatmap thread url or bloodcat.com beatmap url. </param>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	private static void BloodcatDownload(string request)
	{
		const string BloodcatDownloadUrl = "http://bloodcat.com/osu/m/";

		StringBuilder query = null;

		var uri = new Uri(request);
		if (uri.Host == "osu.ppy.sh" && "b/d/s/".Contains(uri.Segments[1]))
		{
			// b = each diffibulty id, s = beatmap id, d = beatmap id as download link.
			query = new StringBuilder("http://bloodcat.com/osu/?mod=json&m=");
			query.Append(uri.Segments[1][0] == 'b' ? 'b' : 's');

			query.Append("&q=");
			query.Append(uri.Segments[2]);
		}
		else if (uri.Host == "bloodcat.com" && uri.AbsolutePath.StartsWith("/osu/m/"))
		{
			// Id extracted from bloodcat.com link
			DownloadAndExecuteOsz(BloodcatDownloadUrl + uri.Segments[3]);
			return;
		}

		// Query to bloodcat.com. Difficulty id requires this process.

		WebClient client = new WebClient() { Encoding = Encoding.UTF8 };
		string json = client.DownloadString(new Uri(query.ToString()));
		JObject result = JObject.Parse(json);

		int count = (int)result["resultCount"];
		if (count != 1)
		{
			File.AppendAllLines("C:\\ThreadList.txt", new string[]
			{
				"BloodcatDownload() : " + GetCurrentThreadId(),
				" - IsThreadIntercepted : " + ShellExecuteExHook.IsThreadIntercepted((int)GetCurrentThreadId()),
			});
			// If not found or undecidable, open official page.
			SHELLEXECUTEINFO exInfo = new SHELLEXECUTEINFO()
			{
				nShow = ShowWindowCommands.ShowDefault,
				lpVerb = "open",
				lpFile = request,
			};
			exInfo.cbSize = Marshal.SizeOf(exInfo);
			ShellExecuteEx(ref exInfo);
			return;
		}

		// Parse and download.

		var beatmapJson = result["results"][0];
		int beatmapId = (int)beatmapJson["id"];
		DownloadAndExecuteOsz(BloodcatDownloadUrl + beatmapId);
	}

	private static void DownloadAndExecuteOsz(string url)
	{
		HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
		HttpWebResponse res = (HttpWebResponse)request.GetResponse();
		string disposition = res.Headers["Content-Disposition"] != null ?
							 res.Headers["Content-Disposition"].Replace("attachment; filename=", "").Replace("\"", "") :
							 res.Headers["Location"] != null ? Path.GetFileName(res.Headers["Location"]) :
							 Path.GetFileName(url).Contains('?') || Path.GetFileName(url).Contains('=') ?
							 Path.GetFileName(res.ResponseUri.ToString()) : url.GetHashCode() + ".osz";

		string downloadPath = DownloadDir + disposition;

		// If user clicks several times, ignore.
		if (File.Exists(downloadPath))
		{
			return;
		}

		using(Stream rstream = res.GetResponseStream())
		{
			using(var destStream = File.Create(downloadPath))
			{
				rstream.CopyTo(destStream);
			}
		}
		res.Close();

		string osuExePath = Process.GetCurrentProcess().MainModule.FileName;
		ProcessStartInfo psi = new ProcessStartInfo()
		{
			Verb = "open",
			FileName = osuExePath,
			Arguments = downloadPath,
		};

		try
		{
			Process.Start(psi);
		}
		// TODO: IDropTarget으로 강제 갱신하는 방법 있음
		catch (Win32Exception e)
		{
			string destPath = Path.Combine(Path.GetDirectoryName(osuExePath),
										   "Songs", Path.GetFileName(downloadPath));
			File.Move(downloadPath, destPath);
		}
	}

	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>   Filter character which cannot be file name. </summary>
	///
	/// <param name="name"> The name. </param>
	///
	/// <returns>   The string removed invalid characters. </returns>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	private static string GetValidFileName(string name)
	{
		return string.Concat(name.Except(System.IO.Path.GetInvalidFileNameChars()));
	}

	[DllImport("kernel32.dll")]
	static extern uint GetCurrentThreadId();

	#region ShowWindow pinvoke

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
		File.AppendAllLines("C:\\ThreadList.txt", new string[]
		{
			"ShellExecuteEx_Hooked() : " + GetCurrentThreadId(),
			" - IsThreadIntercepted : " + ShellExecuteExHook.IsThreadIntercepted((int)GetCurrentThreadId()),
			" - lpExecInfo.lpFile : " + lpExecInfo.lpFile,
			" - lpExecInfo.lpParameters : " + lpExecInfo.lpParameters,
		});
		try
		{
			var uri = new Uri(lpExecInfo.lpFile);

			//"https?://osu\.ppy\.sh/[bsd]/"
			//"https?://bloodcat.com/osu/m/"
			if (uri.Host == "osu.ppy.sh" && "b/d/s/".Contains(uri.Segments[1]) ||
				uri.Host == "bloodcat.com" && uri.AbsolutePath.StartsWith("/osu/m/"))
			{
				OsuInjectee instance = (OsuInjectee)HookRuntimeInfo.Callback;
				lock (instance.Queue)
				{
					instance.Queue.Enqueue(lpExecInfo.lpFile);
				}
				instance.QueueAppended.Set();

				return true;
			}
		}
		catch (Exception e)
		{
			OsuDownloader.OsuHooker.LogException(e);
		}

		//call original API...
		return ShellExecuteEx(ref lpExecInfo);
	}

	#endregion

}

}
