using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.Remoting;
using EasyHook;
using System.Windows;
using System.Net;
using System.Diagnostics;
using System.IO;

namespace OsuDownloader
{

public class HookSwitch : MarshalByRefObject
{
	public bool IsHooking;
	public bool IsInstalled;
	public DateTime LastPing;

	#region Injector to injectee event

	public event Action EnableHookRequest;

	public event Action DisableHookRequest;

	#endregion

	public void Installed()
	{
		IsInstalled = true;
	}

	public void Ping(bool isHookEnabled)
	{
		IsHooking = isHookEnabled;
		LastPing = DateTime.Now;
	}

	public bool EnableHook()
	{
		try
		{
			EnableHookRequest.Invoke();
		}
		catch
		{
			return false;
		}
		return true;
	}

	public bool DisableHook()
	{
		try
		{
			DisableHookRequest.Invoke();
		}
		catch
		{
			return false;
		}
		return true;
	}
}

public class OsuHooker
{
	static string ChannelName = null;
	static HookSwitch HookChannel;
	static int TargetPid;

	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>   Query whether injectee is ready. </summary>
	///
	/// <value> If last ping had been in 3 seconds, then it true. </value>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	public static bool IsInjected
	{
		get
		{
			try
			{
				return HookChannel.IsInstalled &&
					   (DateTime.Now - HookChannel.LastPing).TotalSeconds < 3;
			}
			catch
			{
				return false;
			}
		}
	}
	public static bool IsHooking
	{
		get
		{
			return IsInjected && HookChannel.IsHooking;
		}
	}

	public static bool ToggleHook()
	{
		if (IsHooking)
		{
			HookChannel.DisableHook();
			return true;
		}
		else if (IsInjected)
		{
			HookChannel.EnableHook();
			return true;
		}

		try
		{
			string thisFile = new Uri(System.Reflection.Assembly.GetExecutingAssembly().Location).LocalPath;
			string injectee = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RemoteDown.dll");
			// On publish it can be merged so it should able to be excluded.
			string jsonLib  = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Newtonsoft.Json.dll");

			try
			{
				if (File.Exists(jsonLib))
					Config.Register("Osu beatmap downloader.", thisFile, injectee, jsonLib);
				else
					Config.Register("Osu beatmap downloader.", thisFile, injectee);
			}
			catch (ApplicationException)
			{
				MessageBox.Show("DLL파일이 있는지, 관리자 권한이 있는지 확인해주세요.",
								"후킹 실패", MessageBoxButton.OK);
				return false;
			}

			var osuCandidates = from proc in Process.GetProcesses()
								where proc.ProcessName == "osu!"
								select proc;

			if (osuCandidates.Count() == 1)
			{
				TargetPid = osuCandidates.First().Id;
			}
			else
			{
				// CreateAndInject doesn't work. I don't know reason.
				TargetPid = Process.Start(OsuHelper.GetOsuPath()).Id;
			}

			RemoteHooking.IpcCreateServer<HookSwitch>(ref ChannelName, WellKnownObjectMode.Singleton);
			HookChannel = (HookSwitch)Activator.GetObject(typeof(HookSwitch), "ipc://" + ChannelName + "/" + ChannelName);

			RemoteHooking.Inject(TargetPid, injectee, injectee, ChannelName);
		}
		catch (Exception extInfo)
		{
			HookChannel = null;
			LogException(extInfo);
			return false;
		}

		return true;
	}

	public static void LogException(Exception extInfo)
	{
		var log = System.IO.File.AppendText("C:\\osuDownloader.log");
		log.WriteLine("------------------------");
		var inner = extInfo;
		while (inner != null)
		{
			log.WriteLine(extInfo.Message);
			inner = inner.InnerException;
		}
		log.WriteLine("------------------------");
		log.Close();
	}
}
}
