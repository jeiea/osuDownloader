using EasyHook;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace OsuDownloader.Injectee
{

[DataContract]
internal class BloodcatResult
{
	[DataMember]
	public int id;
	[DataMember]
	public string artist;
	[DataMember]
	public string title;
}

[DataContract]
internal class BloodcatContainer
{
	[DataMember]
	public int resultCount;

	[DataMember]
	public BloodcatResult[] results;
}

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

	static D3D9Hooker Overlayer;

	/// <summary>   true if disposed. </summary>
	private bool Disposed;

	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>
	/// The requested beatmap list to determine whether or not clicked before. It's used at force
	/// download.
	/// </summary>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	static Dictionary<int, DateTime> requestedBeatmaps = new Dictionary<int, DateTime>();

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

			Overlayer = new D3D9Hooker();

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
			if (Overlayer != null)
			{
				Overlayer.Dispose();
				Overlayer = null;
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

	// TODO: Consider asynchoronous and case of download file name exists.
	#region Download routine

	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>   Download from bloodcat mirror. </summary>
	///
	/// <param name="request"> Official beatmap thread url or bloodcat.com beatmap url. </param>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	static void BloodcatDownload(string request)
	{
		const string BloodcatDownloadUrl = "http://bloodcat.com/osu/m/";

		Uri uri = new Uri(request);

		WebClient client = new WebClient() { Encoding = Encoding.UTF8 };
		var query = client.QueryString;
		query.Add("mod", "json");

		if (uri.Host == "osu.ppy.sh" && "b/d/s/".Contains(uri.Segments[1]))
		{
			// b = each diffibulty id, s = beatmap id, d = beatmap id as download link.
			query.Add("m", uri.Segments[1][0] == 'b' ? "b" : "s");
			query.Add("q", uri.Segments[2]);
		}
		else if (uri.Host == "bloodcat.com" && uri.AbsolutePath.StartsWith("/osu/m/"))
		{
			// Id extracted from bloodcat.com link
			query.Add("m", "s");
			query.Add("q", uri.Segments[3]);
		}

		// Query to bloodcat.com whether the beatmap exists.

		string json = client.DownloadString("http://bloodcat.com/osu/");
		var jsonStream = new MemoryStream(Encoding.UTF8.GetBytes(json));
		var jsonParser = new DataContractJsonSerializer(typeof(BloodcatContainer));
		var container = (BloodcatContainer)jsonParser.ReadObject(jsonStream);

		// If we use CsQuery, then below is possible.
		//dynamic result = JSON.ParseJSON(json);

		if (container.resultCount != 1)
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

		int beatmapId = container.results[0].id;

		// Copy URL for sharing to clipboard.
		string downloadLink = BloodcatDownloadUrl + beatmapId;
		Clipboard.SetText(downloadLink);

		requestedBeatmaps
		.Where(x => (DateTime.Now - x.Value).TotalSeconds >= 1)
		.Select(x => x.Key).ToList()
		.ForEach(x => requestedBeatmaps.Remove(x));

		// Download if already exists or double clicked.
		if (OsuHelper.IsTakenBeatmap(beatmapId) == false ||
			requestedBeatmaps.ContainsKey(beatmapId))
		{
			DownloadAndExecuteOsz(downloadLink);
			requestedBeatmaps.Remove(beatmapId);
		}
		else
		{
			Overlayer.AddMessage(new object(), new NoticeEntry()
			{
				Begin = DateTime.Now,
				Duration = TimeSpan.FromSeconds(2),
				Message = "이미 있는 비트맵입니다. URL이 클립보드로 복사되었습니다. 받으시려면 더블클릭 해 주세요."
			});
			requestedBeatmaps[beatmapId] = DateTime.Now;
		}
	}

	static void DownloadAndExecuteOsz(string url)
	{
		string tempFile = Path.GetTempFileName();

		string cookie = ApplyBloodcatOption().ToString();

		WebClient client = new WebClient() { Encoding = Encoding.UTF8 };
		client.DownloadProgressChanged += new DownloadProgressChangedEventHandler(client_DownloadProgressChanged);
		client.DownloadFileCompleted += new AsyncCompletedEventHandler(client_DownloadFileCompleted);

		Overlayer.AddMessage(client, new ProgressEntry()
		{
			Begin = DateTime.Now,
			Title = Path.GetFileName(url),
			Total = int.MaxValue,
			Path  = tempFile
		});

		client.DownloadFileAsync(new Uri(url), tempFile, client);
	}

	static void client_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
	{
		ProgressEntry entry = (ProgressEntry)Overlayer.MessageQueue[e.UserState];
		if (entry.Total == int.MaxValue)
		{
			try
			{
				WebClient client = (WebClient)e.UserState;

				string disposition = client.ResponseHeaders["Content-Disposition"];
				entry.Title = Regex.Match(disposition, "filename\\s*=\\s*\"(.*?)\"").Groups[1].ToString();

				if (File.Exists(Path.Combine("C:\\", entry.Title)))
				{
					client.CancelAsync();
					return;
				}

				entry.Total = e.TotalBytesToReceive;
			}
			catch (Exception ex)
			{
				MainWindowViewModel.LogException(ex);
			}
		}
		entry.Downloaded = e.BytesReceived;
	}

	static void client_DownloadFileCompleted(object sender, AsyncCompletedEventArgs e)
	{
		if (e.Cancelled)
		{
			MainWindowViewModel.LogException(e.Error);
			return;
		}

		WebClient client = (WebClient)e.UserState;

		var progressVisual = (ProgressEntry)Overlayer.MessageQueue[client];
		string downloadPath = Path.Combine(DownloadDir, progressVisual.Title);
		try
		{
			File.Move(progressVisual.Path, downloadPath);
		}
		catch (Exception ex)
		{
			MainWindowViewModel.LogException(ex);
			downloadPath = progressVisual.Path;
		}

		string osuExePath = Process.GetCurrentProcess().MainModule.FileName;

		Overlayer.MessageQueue.Remove(client);

		try
		{
			Process.Start(osuExePath, downloadPath);
		}
		// TODO: IDropTarget으로 강제 갱신하는 방법 있음
		catch (Win32Exception)
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
	static Cookie ApplyBloodcatOption()
	{
		var option = Properties.Settings.Default.BloodcatOption;

		StringBuilder cookieJson = new StringBuilder(100);
		cookieJson.Append("{\"direct\":false,\"droid\":false,\"bg\":");
		switch (option.Background)
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
		cookieJson.Append(option.RemoveVideoAndStoryboard ? "true" : "false");
		cookieJson.Append(",\"skin\":");
		cookieJson.Append(option.RemoveSkin ? "true" : "false");
		cookieJson.Append('}');

		return new Cookie("DLOPT", Uri.EscapeDataString(cookieJson.ToString()), "/", "bloodcat.com");
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

				var worker = new Thread(() => InvokeUrlHooker.BloodcatDownload(request));
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
