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

namespace RemoteDown
{

public class OsuInjectee : EasyHook.IEntryPoint
{
	static string DownloadDir = "C:\\";

	OsuDownloader.HookSwitch Interface;
	LocalHook ShellExecuteExHook;
	/// <summary>   ShowWindow function hook. This is necessary during fullscreen mode. </summary>
	LocalHook ShowWindowHook;
	bool IsHooked;
	Queue<string> Queue = new Queue<string>();
	ManualResetEvent QueueAppended;
	IpcServerChannel ClientServerChannel;

	public OsuInjectee(RemoteHooking.IContext context, string channelName)
	{
		// connect to host...
		Interface = RemoteHooking.IpcConnectClient<OsuDownloader.HookSwitch>(channelName);

		Interface.Ping(false);

		#region Allow client event handlers (bi-directional IPC)

		// Attempt to create a IpcServerChannel so that any event handlers on the client will function correctly
		System.Collections.IDictionary properties = new System.Collections.Hashtable();
		properties["name"] = channelName;
		// random portName so no conflict with existing channels of channelName
		properties["portName"] = channelName + Guid.NewGuid().ToString("N");

		var binaryProv = new BinaryServerFormatterSinkProvider();
		binaryProv.TypeFilterLevel = TypeFilterLevel.Full;

		ClientServerChannel = new IpcServerChannel(properties, binaryProv);
		System.Runtime.Remoting.Channels.ChannelServices.RegisterChannel(ClientServerChannel, false);

		#endregion
	}

	public void Run(RemoteHooking.IContext context, string channelName)
	{
		// install hook...
		try
		{
			EnableHook();

			Interface.EnableHookRequest += EnableHook;
			Interface.DisableHookRequest += DisableHook;
		}
		catch (Exception extInfo)
		{
			OsuDownloader.OsuHooker.LogException(extInfo);
		}

		Interface.Installed();

		RemoteHooking.WakeUpProcess();

		QueueAppended = new ManualResetEvent(false);
		// wait for host process termination...
		try
		{
			while (true)
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
				else
					Interface.Ping(ShellExecuteExHook != null);
			}
		}
		catch
		{
			// Ping() will raise an exception if host is unreachable
		}
		finally
		{
			DisableHook();
			ChannelServices.UnregisterChannel(ClientServerChannel);
		}
	}

	private void DisableHook()
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

	private void EnableHook()
	{
		ShellExecuteExHook = LocalHook.Create(
								 LocalHook.GetProcAddress("shell32.dll", "ShellExecuteExW"),
								 new DShellExecuteEx(ShellExecuteEx_Hooked), this);

		ShowWindowHook = LocalHook.Create(
							 LocalHook.GetProcAddress("user32.dll", "ShowWindow"),
							 new DShowWindow(ShowWindow_Hooked), this);

		ShellExecuteExHook.ThreadACL.SetExclusiveACL(new int[] { 0 });
		ShowWindowHook.ThreadACL.SetExclusiveACL(new int[] { 0 });
	}

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

		WebClient client = new WebClient();
		client.Encoding = Encoding.UTF8;

		byte[] json = client.DownloadData(new Uri(query.ToString()));
		JObject result = JObject.Parse(Encoding.UTF8.GetString(json));

		int count = (int)result["resultCount"];
		if (count != 1)
		{
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

		ProcessStartInfo psi = new ProcessStartInfo();
		psi.Verb = "open";
		psi.FileName = downloadPath;
		psi.UseShellExecute = true;
		Process.Start(psi);
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
	/// ShellExecute during fullscreen mode. So it's necessary.
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

	[DllImport("shell32.dll", CharSet = CharSet.Auto)]
	static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);

	[UnmanagedFunctionPointer(CallingConvention.StdCall,
							  CharSet = CharSet.Auto,
							  SetLastError = true)]
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
				OsuInjectee instance = (OsuInjectee)HookRuntimeInfo.Callback;
				lock (instance.Queue)
				{
					instance.Queue.Enqueue(lpExecInfo.lpFile);
				}
				instance.QueueAppended.Set();

				// For prevention of window minimization in fullscreen mode.
				instance.IsHooked = true;

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
