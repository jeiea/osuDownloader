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
	public void Installed()
	{
		OsuHooker.IsInstalled = true;
		OsuHooker.IsHooking = true;
	}

	public void HookSwitched(bool status)
	{
		OsuHooker.IsHooking = status;
	}
}

/// <summary>   Interface for osu injectee. </summary>
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
	static int TargetPid;
	static IOsuInjectee InjecteeProxy;
	static HookCallback Callback;

	public static bool IsInstalled;

	static bool isHooking;
	public static bool IsHooking
	{
		get
		{
			return isHooking;
		}
		set
		{
			isHooking = value;
			if (IsHookingChanged != null)
			{
				IsHookingChanged.Invoke();
			}
		}
	}

	/// <summary>   Event invoked when IsHooking is changed. </summary>
	public static event Action IsHookingChanged;

	public static bool ToggleHook()
	{
		try
		{
			if (IsHooking)
			{
				InjecteeProxy.DisableHook();
				return true;
			}
			else if (IsInstalled)
			{
				InjecteeProxy.EnableHook();
				return true;
			}
		}
		catch (Exception)
		{
			// Case of host terminated.
			var proxy = InjecteeProxy as ICommunicationObject;
			if (proxy != null)
			{
				proxy.Close();
				InjecteeProxy = null;
			}
		}

		try
		{
			string thisFile = new Uri(System.Reflection.Assembly.GetExecutingAssembly().Location).LocalPath;
			string injectee = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RemoteDown.dll");
			// On publish it can be merged so it should able to be excluded.
			string jsonLib  = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Newtonsoft.Json.dll");

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
			// Unless creating new thread, MessageBox will disappear immediately
			// when invoked from tray icon without window.
			new Thread(new ThreadStart(delegate
			{
				MessageBox.Show("DLL파일이 있는지, 관리자 권한이 있는지 확인해주세요.", "후킹 실패");
			})).Start();

			LogException(extInfo);
			return false;
		}

		try
		{
			Callback = new HookCallback();
			var pipeFactory = new DuplexChannelFactory<IOsuInjectee>(
				Callback, new NetNamedPipeBinding(),
				new EndpointAddress("net.pipe://localhost/osuBeatmapHooker"));

			ThreadPool.QueueUserWorkItem(new WaitCallback(obj =>
			{
				InjecteeProxy = pipeFactory.CreateChannel();

				InjecteeProxy.Subscribe();

				(InjecteeProxy as ICommunicationObject).Faulted += ChannelFaulted;
			}));
		}
		catch (Exception e)
		{
			LogException(e);
		}

		return true;
	}

	static void ChannelFaulted(object sender, EventArgs e)
	{
		IsInstalled = false;
		IsHooking = false;
		InjecteeProxy = null;
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
