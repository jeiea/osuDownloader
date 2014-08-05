using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
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
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using System.Windows.Media;

namespace OsuDownloader
{

[ServiceBehavior(InstanceContextMode = InstanceContextMode.Single)]
public class OsuInjectee : IOsuInjectee, EasyHook.IEntryPoint
{
	static string DownloadDir = "C:\\";

	/// <summary>   ShellExecuteEx hook. Intercept url opens. </summary>
	LocalHook ShellExecuteExHook;
	/// <summary>   ShowWindow function hook. This is necessary during fullscreen mode. </summary>
	LocalHook ShowWindowHook;
	/// <summary>   CreateFile function hook for boss mode. </summary>
	LocalHook CreateFileHook;

	Queue<string> Queue = new Queue<string>();
	ManualResetEvent QueueAppended;

	ServiceHost InjecteeHost;
	List<ICallback> Callbacks = new List<ICallback>();

	BloodcatDownloadOption BloodcatOption = new BloodcatDownloadOption();

	/// <summary>   This is used to determine whether received no connection. </summary>
	bool LastConnectionFaulted;

	/// <summary>   Identifier for the hooking thread. </summary>
	int HookingThreadId;

	/// <summary>   The alternative background image path for boss mode. </summary>
	static string AlternativeImage;
	/// <summary>   The alternative storyboard file path for boss mode. </summary>
	static string AlternativeStoryboard;
	static string AlternativeVideo;

	static Regex SkinNames;

	static OsuInjectee()
	{
		var sb = new StringBuilder();
		sb.Append(@"^(");
		sb.Append(@"approachcircle|button-|comboburst|count\d|cursor|default-\d|followpoint|");
		sb.Append(@"fruit-|go|hit|inputoverlay|mania-|particle|pause-|pippidon|ranking-|");
		sb.Append(@"ready|reversearrow|score-|scorebar-|section-|slider|spinner-|star|taiko-|");
		sb.Append(@"taikobigcircle|taikohitciecle");
		sb.Append(@").*$");

		SkinNames = new Regex(sb.ToString(), RegexOptions.IgnoreCase);
	}

	public OsuInjectee(RemoteHooking.IContext context)
	{
		InjecteeHost = new ServiceHost(this, new Uri[] { new Uri("net.pipe://localhost") });
		InjecteeHost.AddServiceEndpoint(typeof(IOsuInjectee), new NetNamedPipeBinding(), "osuBeatmapHooker");
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
			MainViewModel.LogException(extInfo);
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
							MainViewModel.LogException(e);
						}
					}
				}
			}
		}
		catch (Exception e)
		{
			MainViewModel.LogException(e);
		}
		finally
		{
			DisableHook();
			InjecteeHost.Abort();
			InjecteeHost = null;
		}
	}

	#region WCF Implementation

	public void Subscribe()
	{
		var callback = OperationContext.Current.GetCallbackChannel<ICallback>();
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
		var callback = OperationContext.Current.GetCallbackChannel<ICallback>();
		Callbacks.Remove(callback);

		if (Callbacks.Count <= 0)
		{
			DisableHook();
			LastConnectionFaulted = true;
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

		var threadList = GetOsuThreads();

		ShellExecuteExHook.ThreadACL.SetInclusiveACL(threadList.ToArray());
		ShowWindowHook.ThreadACL.SetExclusiveACL(new int[] { HookingThreadId });

		NotifyHookSwitch();
	}

	private List<int> GetOsuThreads()
	{
		var threadList = new List<int>();
		foreach (ProcessThread thread in Process.GetCurrentProcess().Threads)
		{
			threadList.Add(thread.Id);
		}
		threadList.Remove(HookingThreadId);
		return threadList;
	}

	// 이름으로 기능 추측 방지
	public void ToggleHook(bool request)
	{
		if (request)
		{
			#region Bitmap creation

			DrawingVisual drawingvisual = new DrawingVisual();
			using(DrawingContext context = drawingvisual.RenderOpen())
			{
				context.DrawRectangle(new SolidColorBrush(BloodcatOption.BackgroundColor),
									  null, new System.Windows.Rect(0, 0, 2, 2));
				context.Close();
			}

			RenderTargetBitmap result = new RenderTargetBitmap(2, 2, 96, 96, PixelFormats.Pbgra32);
			result.Render(drawingvisual);

			var encoder = new PngBitmapEncoder();
			encoder.Frames.Add(BitmapFrame.Create(result));

			AlternativeImage = Path.GetTempFileName();
			using(var tempImage = File.OpenWrite(AlternativeImage))
			{
				encoder.Save(tempImage);
			}

			#endregion

			AlternativeStoryboard = Path.GetTempFileName();
			File.AppendAllText(AlternativeStoryboard, "[Events]");

			AlternativeVideo = Path.GetTempFileName();
			File.WriteAllBytes(AlternativeVideo, Properties.Resources.Black2x2);

			CreateFileHook = LocalHook.Create(
								 LocalHook.GetProcAddress("kernel32.dll", "CreateFileW"),
								 new DCreateFile(CreateFile_Hooked),
								 this);

			// BG reading is not thread specific. Newly created thread also included.
			CreateFileHook.ThreadACL.SetExclusiveACL(new int[] { HookingThreadId });
		}
		else if (CreateFileHook != null)
		{
			CreateFileHook.Dispose();
			CreateFileHook = null;

			File.Delete(AlternativeImage);
			AlternativeImage = null;

			File.Delete(AlternativeStoryboard);
			AlternativeStoryboard = null;
		}
	}

	public void OptionChanged(BloodcatDownloadOption option)
	{
		BloodcatOption = option;
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

	#region Download routine

	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>   Download from bloodcat mirror. </summary>
	///
	/// <param name="request"> Official beatmap thread url or bloodcat.com beatmap url. </param>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	private void BloodcatDownload(string request)
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

	private void DownloadAndExecuteOsz(string url)
	{
		HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);

		ApplyBloodcatOption(request);

		HttpWebResponse res = (HttpWebResponse)request.GetResponse();

		// TODO: It must be SID Artist - Title format for avoiding duplication.
		string disposition = res.Headers["Content-Disposition"] != null ?
							 Regex.Match(res.Headers["Content-Disposition"], "filename\\s*=\\s*\"(.*?)\"").Groups[1].ToString() :
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
			string[] pathElements = new string[]
			{
				Path.GetDirectoryName(osuExePath),
				"Songs",
				Path.GetFileName(downloadPath),
			};
			// For .NET 3.5
			string destPath = pathElements.Aggregate(Path.Combine);
			File.Move(downloadPath, destPath);
		}
	}

	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>   Add cookie which describe download manipulation option. </summary>
	///
	/// <param name="request">  WebRequest to use. </param>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	private void ApplyBloodcatOption(HttpWebRequest request)
	{
		StringBuilder cookieJson = new StringBuilder(100);
		cookieJson.Append("{\"direct\":false,\"droid\":false,\"bg\":");
		switch (BloodcatOption.Background)
		{
		case BloodcatWallpaperOption.RemoveBackground:
			cookieJson.Append("\"delete\"");
			break;
		case BloodcatWallpaperOption.SolidColor:
			cookieJson.Append("\"color\"");
			break;
		default:
			cookieJson.Append("false");
			break;
		}
		cookieJson.Append(",\"video\":");
		cookieJson.Append(BloodcatOption.RemoveVideoAndStoryboard ? "true" : "false");
		cookieJson.Append(",\"skin\":");
		cookieJson.Append(BloodcatOption.RemoveSkin ? "true" : "false");
		cookieJson.Append('}');

		var downloadOption = new Cookie("DLOPT", Uri.EscapeDataString(cookieJson.ToString()), "/", "bloodcat.com");
		request.CookieContainer = new CookieContainer();
		request.CookieContainer.Add(downloadOption);
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

	#endregion

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
			MainViewModel.LogException(e);
		}

		//call original API...
		return ShellExecuteEx(ref lpExecInfo);
	}

	#endregion

	#region CreateFile pinvoke

	[UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode, SetLastError = true)]
	delegate IntPtr DCreateFile(
		[MarshalAs(UnmanagedType.LPWStr)] string filename,
		[MarshalAs(UnmanagedType.U4)] FileAccess access,
		[MarshalAs(UnmanagedType.U4)] FileShare share,
		IntPtr securityAttributes, // optional SECURITY_ATTRIBUTES struct or IntPtr.Zero
		[MarshalAs(UnmanagedType.U4)] FileMode creationDisposition,
		[MarshalAs(UnmanagedType.U4)] FileAttributes flagsAndAttributes,
		IntPtr templateFile);

	// just use a P-Invoke implementation to get native API access from C# (this step is not necessary for C++.NET)
	[DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, CallingConvention = CallingConvention.StdCall)]
	public static extern IntPtr CreateFile(
		[MarshalAs(UnmanagedType.LPWStr)] string filename,
		[MarshalAs(UnmanagedType.U4)] FileAccess access,
		[MarshalAs(UnmanagedType.U4)] FileShare share,
		IntPtr securityAttributes, // optional SECURITY_ATTRIBUTES struct or IntPtr.Zero
		[MarshalAs(UnmanagedType.U4)] FileMode creationDisposition,
		[MarshalAs(UnmanagedType.U4)] FileAttributes flagsAndAttributes,
		IntPtr templateFile);

	// this is where we are intercepting all file accesses!
	static IntPtr CreateFile_Hooked(
		[MarshalAs(UnmanagedType.LPWStr)] string filename,
		[MarshalAs(UnmanagedType.U4)] FileAccess access,
		[MarshalAs(UnmanagedType.U4)] FileShare share,
		IntPtr securityAttributes, // optional SECURITY_ATTRIBUTES struct or IntPtr.Zero
		[MarshalAs(UnmanagedType.U4)] FileMode creationDisposition,
		[MarshalAs(UnmanagedType.U4)] FileAttributes flagsAndAttributes,
		IntPtr templateFile)
	{
		try
		{
			filename = filename.ToLower();
			// Frequency order.
			if (!filename.EndsWith(".exe") &&
				filename.IndexOf("\\osu!\\data\\") == -1 &&
				!filename.EndsWith(".osu") &&
				!filename.EndsWith(".mp3") &&
				!filename.EndsWith(".wav"))
			{
				if (filename.EndsWith(".osb"))
				{
					filename = AlternativeStoryboard;
				}
				else if (filename.EndsWith(".avi") ||
						 filename.EndsWith(".mkv") ||
						 filename.EndsWith(".mp4") ||
						 filename.EndsWith(".flv"))
				{
					filename = AlternativeVideo;
				}
				else if (SkinNames.IsMatch(Path.GetFileName(filename)) == false)
				{
					filename = AlternativeImage;
				}
			}
		}
		catch (Exception e)
		{
			MainViewModel.LogException(e);
		}

		// call original API...
		return CreateFile(filename, access, share, securityAttributes,
						  creationDisposition, flagsAndAttributes, templateFile);
	}

	#endregion

}

#if NET35
static class CompatibilityHelper
{

	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>   Equal to above .NET 4.0 CopyTo method. </summary>
	///
	/// <param name="input">    The input to act on. </param>
	/// <param name="output">   The output. </param>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	public static void CopyTo(this Stream input, Stream output)
	{
		byte[] buffer = new byte[1024 * 1024]; // Fairly arbitrary size
		int bytesRead;

		while ((bytesRead = input.Read(buffer, 0, buffer.Length)) > 0)
		{
			output.Write(buffer, 0, bytesRead);
		}
	}
}
#endif
}
