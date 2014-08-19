using EasyHook;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.ServiceModel;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace OsuDownloader.Injectee
{

internal class EntryBase
{
	/// <summary>   The time when message appears. This is also used to ordering message. </summary>
	public DateTime Begin;

	public virtual string Message { get; set; }
}

internal class NoticeEntry : EntryBase
{
	/// <summary>   The duration message shown. </summary>
	public TimeSpan Duration;
}

internal class ProgressEntry : EntryBase
{
	/// <summary>   Downloaded bytes. </summary>
	public long Downloaded;
	/// <summary>   Total bytes. </summary>
	public long Total;
	/// <summary>   The title. </summary>
	public string Title;

	public override string Message
	{
		get
		{
			string format = (Total == int.MaxValue)
							? "[     요청 중    ] {2}"
							: "[{0,5:F1}MB /{1,5:F1}MB] {2}";
			return string.Format(format, Downloaded / 1000000F, Total / 1000000F, Title);
		}
		set { }
	}
}

internal interface IOverlayer
{
	Dictionary<object, EntryBase> GetMessageQueue();

	void AddMessage(object key, EntryBase entry);
}

[ServiceBehavior(InstanceContextMode = InstanceContextMode.Single)]
public class HookManager :  IOsuInjectee, EasyHook.IEntryPoint
{
	public static List<int> HookingThreadIds = new List<int>();

	ServiceHost InjecteeHost;
	List<ICallback> Callbacks = new List<ICallback>();

	System.Windows.Threading.Dispatcher MainDispatcher;

	// Hookers. Blinder remove background image, Downloader downloads osz, Detector detects
	// foreground window change and IE navigation, Overlayer overlays display.

	FileNameHooker Blinder;
	InvokeUrlHooker Downloader;
	ForegroundHooker Detector;
	IOverlayer Overlayer;

	WinFormHotKey BossKey;

	Timer CookieTimer;

	public HookManager(RemoteHooking.IContext context)
	{
		if (Process.GetCurrentProcess().ProcessName != "iexplore")
		{
			InjecteeHost = new ServiceHost(this, new Uri[] { new Uri("net.pipe://localhost") });
			InjecteeHost.AddServiceEndpoint(typeof(IOsuInjectee), new NetNamedPipeBinding(), "osuBeatmapHooker");
			InjecteeHost.Open();
		}
	}

	[DllImport("wininet.dll", CharSet = CharSet.Auto, SetLastError = true)]
	static extern bool InternetGetCookieEx(string pchURL, string pchCookieName, StringBuilder pchCookieData,
										   ref uint pcchCookieData, int dwFlags, IntPtr lpReserved);

	public void Run(RemoteHooking.IContext context)
	{

		RegisterAssemblyToDomain();

		Config.DependencyPath = Path.GetDirectoryName(GetType().Assembly.Location);

		HookingThreadIds.Add((int)GetCurrentThreadId());

		try
		{
			// Hooker initialization. IE or osu.
			var currentProc = Process.GetCurrentProcess();
			if (currentProc.ProcessName == "iexplore")
			{
				// In IE, event not firing well. So use one time stretegy.
				var newCookie = GetCookie();
				if (newCookie != Properties.Settings.Default.OfficialSession)
				{
					Properties.Settings.Default.OfficialSession = newCookie;
				}
				return;
			}
			else
			{
				InitializeOsuHookers();
			}
		}
		catch (Exception extInfo)
		{
			MainWindowViewModel.LogException(extInfo);
		}

		RemoteHooking.WakeUpProcess();

		// wait for host process termination...
		try
		{
			MainDispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
			System.Windows.Threading.Dispatcher.Run();
		}
		catch (Exception e)
		{
			MainWindowViewModel.LogException(e);
		}
		finally
		{
			if (Blinder != null)
			{
				Blinder.Dispose();
				Blinder = null;
			}
			if (Downloader != null)
			{
				Downloader.Dispose();
				Downloader = null;
			}
			if (Detector != null)
			{
				Detector.Dispose();
				Detector = null;
			}
			if (InjecteeHost != null)
			{
				InjecteeHost.Abort();
				InjecteeHost = null;
			}
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

	/// <summary>   Registers the assembly to app domain. </summary>
	static void RegisterAssemblyToDomain()
	{
		AppDomain currentDomain = AppDomain.CurrentDomain;
		currentDomain.AssemblyResolve += (sender, args) =>
		{
			return typeof(HookManager).Assembly.FullName == args.Name ? typeof(HookManager).Assembly : null;
		};
		currentDomain.ReflectionOnlyAssemblyResolve += (sender, args) =>
		{
			return typeof(HookManager).Assembly.FullName == args.Name ? typeof(HookManager).Assembly : null;
		};
	}

	private void InitializeOsuHookers()
	{
		try
		{
			// opengl32.dll loaded lazily. wait it.
			while (Process.GetCurrentProcess().MainWindowHandle == IntPtr.Zero)
			{
				Thread.Sleep(100);
			}

			// d3d9.dll load always. so tests with opengl32.dll.
			if (GetModuleHandle("opengl32.dll") == IntPtr.Zero)
			{
				var hooker = new D3D9Hooker();

				// If hook failed, pass to window notifier by exception.
				hooker.SetHookState(true);

				// Accepts D3 hook if test passes.
				Overlayer = hooker;
			}
			else
			{
				throw new ApplicationException("DirectX9 hook seems unavailable.");
			}
		}
		catch
		{
			Overlayer = new WPFOverlayer();
		}

		if (Overlayer != null)
		{
			InvokeUrlHooker.Overlayer = Overlayer;
			Overlayer.AddMessage(new object(), new NoticeEntry()
			{
				Begin = DateTime.Now,
				Duration = TimeSpan.FromSeconds(5),
				Message = "비트맵 다운로더가 동작합니다."
			});
		}

		Downloader = new InvokeUrlHooker();
		Downloader.SetHookState(true);

		RegisterBossKey();

		Detector = new ForegroundHooker();
		Detector.OnForegroundChanged += Foregrounder_OnForegroundChanged;
		Detector.SetHookState(true);
	}

	void Foregrounder_OnForegroundChanged(Process proc)
	{
		if (proc.ProcessName != "osu!" && BossKey != null)
		{
			BossKey.Dispose();
			BossKey = null;
		}
		else if (proc.ProcessName == "osu!" && BossKey == null)
		{
			RegisterBossKey();
		}
	}

	private void RegisterBossKey()
	{
		BossKey = new WinFormHotKey();
		BossKey.RegisterHotKey(ModifierKeys.Control, System.Windows.Forms.Keys.L);
		BossKey.KeyPressed += BossKey_KeyPressed;
	}

	void BossKey_KeyPressed(object sender, KeyPressedEventArgs e)
	{
		if (Blinder == null)
		{
			Blinder = new FileNameHooker();
		}
		Blinder.SetHookState(!Blinder.IsHooking);
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
			}
		}
		if (Callbacks.Count == 0)
		{
			//MainDispatcher.InvokeShutdown();
			System.Windows.Forms.Application.Exit();
		}
	}

	public void Unsubscribe()
	{
		var callback = OperationContext.Current.GetCallbackChannel<ICallback>();
		Callbacks.Remove(callback);

		if (Callbacks.Count <= 0)
		{
			//MainDispatcher.InvokeShutdown();
			System.Windows.Forms.Application.Exit();
		}
	}

	public void SetDownloadHook(bool request)
	{
		Downloader.SetHookState(request);
		NotifyHookSwitch();
	}

	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>   Fake name. It cannot be obfuscated due to WCF protocol. </summary>
	///
	/// <param name="request">  true if enable request. </param>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	public void ToggleHook(bool request)
	{
		if (Blinder == null)
		{
			Blinder = new FileNameHooker();
		}
		Blinder.SetHookState(request);
	}

	public bool IsHookEnabled()
	{
		return Downloader != null;
	}

	public void OptionChanged()
	{
		Properties.Settings.Default.Reload();
	}

	void NotifyHookSwitch()
	{
		foreach (var callback in Callbacks.ToArray())
		{
			try
			{
				callback.HookSwitched(Downloader.IsHooking);
			}
			catch (Exception)
			{
				// Remove callback when it's terminal is terminated.
				Callbacks.Remove(callback);
			}
		}
	}

	#endregion

	[DllImport("kernel32.dll")]
	static extern uint GetCurrentThreadId();

	[DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	public static extern IntPtr GetModuleHandle(string lpModuleName);

}
}
