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
using System.Threading;
using System.ServiceModel;

namespace OsuDownloader
{

public interface ICallback
{
	[OperationContract(IsOneWay = true)]
	void Installed();

	[OperationContract(IsOneWay = true)]
	void HookSwitched(bool status);
}

public class HookCallback : ICallback
{
	#region Callback implementation

	public void Installed()
	{
		OsuHooker.IsInstalled = true;
		OsuHooker.NewIsHooking = true;
	}

	public void HookSwitched(bool status)
	{
		OsuHooker.NewIsHooking = status;
	}

	#endregion
}

[ServiceContract(SessionMode = SessionMode.Required,
				 CallbackContract = typeof(ICallback))]
public interface IOsuInjectee
{
	[OperationContract(IsOneWay = true)]
	void Subscribe();

	[OperationContract(IsOneWay = true)]
	void Unsubscribe();

	[OperationContract(IsOneWay = true)]
	void EnableHook();

	[OperationContract(IsOneWay = true)]
	void DisableHook();

	[OperationContract]
	bool IsHookEnabled();
}

public class OsuHooker
{
	static string ChannelName = null;
	static int TargetPid;
	static IOsuInjectee InjecteeHost;
	static HookCallback Callback;

	public static bool IsInstalled;
	public static bool NewIsHooking;

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
				return IsInstalled;
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
			return IsInjected && NewIsHooking;
		}
	}

	public static bool ToggleHook()
	{
		if (IsHooking)
		{
			InjecteeHost.DisableHook();
			return true;
		}
		else if (IsInjected)
		{
			InjecteeHost.EnableHook();
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
				// Why need?
				if (File.Exists(jsonLib))
					Config.Register("Osu beatmap downloader.", thisFile, injectee, jsonLib);
				else
					Config.Register("Osu beatmap downloader.", thisFile, injectee);
			}
			catch (ApplicationException e)
			{
				// Unless creating new thread, MessageBox will disappear immediately
				// when invoked from tray icon without window.
				new Thread(new ThreadStart(delegate
				{
					MessageBox.Show("DLL파일이 있는지, 관리자 권한이 있는지 확인해주세요.", "후킹 실패");
				})).Start();

				LogException(e);
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

			RemoteHooking.Inject(TargetPid, InjectionOptions.DoNotRequireStrongName,
								 injectee, injectee, "");
		}
		catch (Exception extInfo)
		{
			LogException(extInfo);
			return false;
		}

		try
		{
			Callback = new HookCallback();
			var pipeFactory = new DuplexChannelFactory<IOsuInjectee>(
				Callback, new NetNamedPipeBinding(),
				new EndpointAddress("net.pipe://localhost/PipeReverse"));

			ThreadPool.QueueUserWorkItem(new WaitCallback(obj =>
			{
				InjecteeHost = pipeFactory.CreateChannel();

				InjecteeHost.Subscribe();
			}));
		}
		catch (Exception e)
		{
			LogException(e);
		}

		return true;
	}

	public static void LogException(Exception extInfo)
	{
		var log = System.IO.File.AppendText("C:\\osuDownloader.log");
		log.WriteLine("----------------------------------------");
		log.WriteLine(extInfo.ToString());
		log.WriteLine("-----------------------------------------");
		log.Close();
	}
}
}
