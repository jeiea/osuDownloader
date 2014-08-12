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

	FileNameHooker Blinder;
	InvokeUrlHooker Downloader;
	WinEventHooker Foregrounder;
	IOverlayer Overlayer;

	WinFormHotKey BossKey;

	public HookManager(RemoteHooking.IContext context)
	{
		if (Process.GetCurrentProcess().ProcessName != "iexplore")
		{
			InjecteeHost = new ServiceHost(this, new Uri[] { new Uri("net.pipe://localhost") });
			InjecteeHost.AddServiceEndpoint(typeof(IOsuInjectee), new NetNamedPipeBinding(), "osuBeatmapHooker");
			InjecteeHost.Open();
		}
	}

	void BossKey_KeyPressed(object sender, KeyPressedEventArgs e)
	{
		if (Blinder == null)
		{
			Blinder = new FileNameHooker();
		}
		Blinder.SetHookState(!Blinder.IsHooking);
	}

	[DllImport("wininet.dll", CharSet = CharSet.Auto, SetLastError = true)]
	static extern bool InternetGetCookieEx(string pchURL, string pchCookieName, StringBuilder pchCookieData,
										   ref uint pcchCookieData, int dwFlags, IntPtr lpReserved);

	public void Run(RemoteHooking.IContext context)
	{
		AppDomain currentDomain = AppDomain.CurrentDomain;
		currentDomain.AssemblyResolve += (sender, args) =>
		{
			return this.GetType().Assembly.FullName == args.Name ? this.GetType().Assembly : null;
		};

		HookingThreadIds.Add((int)GetCurrentThreadId());

		Config.DependencyPath = Path.GetDirectoryName(GetType().Assembly.Location);

		var s = new WinFormHotKey();
		s.KeyPressed += (sender, e) =>
		{
			var proc = Process.Start("iexplore");
			proc.WaitForInputIdle();

			var ies = Process.GetProcessesByName("iexplore");
			proc = ies.First(x => RemoteHooking.IsX64Process(x.Id) == false);

			var downloader = GetType().Assembly.Location;
			var downloader64 = Path.Combine(Path.GetDirectoryName(downloader), "osuDownloader64.exe");

			try
			{
				RemoteHooking.Inject(proc.Id, InjectionOptions.DoNotRequireStrongName, downloader, downloader64);
			}
			catch (Exception exc)
			{
				bool l = true;
			}
		};
		s.RegisterHotKey(ModifierKeys.Control, System.Windows.Forms.Keys.M);

		string cookie;
		if (Process.GetCurrentProcess().ProcessName == "iexplore")
		{
			const int INTERNET_COOKIE_HTTPONLY = 0x2000;
			int flag = INTERNET_COOKIE_HTTPONLY;

			uint size = 0;
			InternetGetCookieEx("http://osu.ppy.sh/", null, null, ref size, flag, IntPtr.Zero);

			var buffer = new StringBuilder((int)size);
			InternetGetCookieEx("http://osu.ppy.sh/", null, buffer, ref size, flag, IntPtr.Zero);

			cookie = buffer.ToString();
		}

		try
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

			Foregrounder = new WinEventHooker();
			Foregrounder.OnForegroundChanged += Foregrounder_OnForegroundChanged;

		}
		catch (Exception extInfo)
		{
			MainWindowViewModel.LogException(extInfo);
		}

		RemoteHooking.WakeUpProcess();

		// wait for host process termination...
		try
		{
			new Application().Run();
		}
		catch (Exception e)
		{
			MainWindowViewModel.LogException(e);
		}
		finally
		{
			if (Downloader != null)
			{
				Downloader.Dispose();
				Downloader = null;
			}
			if (Blinder != null)
			{
				Blinder.Dispose();
				Blinder = null;
			}
			InjecteeHost.Abort();
			InjecteeHost = null;
		}
	}

	void Foregrounder_OnForegroundChanged(string processName)
	{
		if (processName != "osu!" && BossKey != null)
		{
			BossKey.Dispose();
			BossKey = null;
		}
		else if (processName == "osu!" && BossKey == null)
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
			Application.Current.Shutdown();
		}
	}

	public void Unsubscribe()
	{
		var callback = OperationContext.Current.GetCallbackChannel<ICallback>();
		Callbacks.Remove(callback);

		if (Callbacks.Count <= 0)
		{
			Application.Current.Shutdown();
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
