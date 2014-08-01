using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.Remoting;
using EasyHook;
using System.Windows;
using System.Net;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.ServiceModel;
using System.ComponentModel;

namespace OsuDownloader
{

public interface ICallback
{
	[OperationContract(IsOneWay = true)]
	void Installed();

	[OperationContract(IsOneWay = true)]
	void HookSwitched(bool status);
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

public class MainViewModel : ICallback, INotifyPropertyChanged
{
	static int TargetPid;
	static IOsuInjectee InjecteeProxy;

	#region Property declaration

	bool _IsInstalled;
	public bool IsInstalled
	{
		get { return _IsInstalled; }
		set
		{
			_IsInstalled = value;
			OnPropertyChanged("IsInstalled");
		}
	}

	bool _IsHooking;
	public bool IsHooking
	{
		get { return _IsHooking; }
		set
		{
			bool isHooked = _IsHooking;
			if (isHooked != _IsHooking)
			{
				OnPropertyChanged("IsHooking");
			}
		}
	}

	public bool AutoStart
	{
		get { return Properties.Settings.Default.AutoStart; }
		set
		{
			if (AutoStart != value)
			{
				Properties.Settings.Default.AutoStart = value;
				Properties.Settings.Default.Save();
				OnPropertyChanged("AutoStart");
			}
		}
	}

	public bool StartAsTray
	{
		get { return Properties.Settings.Default.StartAsTray; }
		set
		{
			if (StartAsTray != value)
			{
				Properties.Settings.Default.StartAsTray = value;
				Properties.Settings.Default.Save();
				OnPropertyChanged("StartAsTray");
			}
		}
	}

	bool _AutoTerminate;
	public bool AutoTerminate
	{
		get { return Properties.Settings.Default.AutoTerminate; }
		set
		{
			if (AutoTerminate != value)
			{
				Properties.Settings.Default.AutoTerminate = value;
				Properties.Settings.Default.Save();
				OnPropertyChanged("AutoTerminate");
			}
		}
	}

	#endregion

	#region ICallback Implementation

	public void Installed()
	{
		IsInstalled = true;
		IsHooking = true;
	}

	public void HookSwitched(bool status)
	{
		IsHooking = status;
	}

	#endregion

	#region INotifyPropertyChanged implementation

	public event PropertyChangedEventHandler PropertyChanged;

	public void OnPropertyChanged(string property)
	{
		if (PropertyChanged != null)
		{
			PropertyChanged(this, new PropertyChangedEventArgs(property));
		}
	}

	#endregion

	public MainViewModel()
	{
		if (AutoStart)
		{
			ToggleHook();
		}
	}

	public bool ToggleHook()
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

			var osuCandidates = from proc in Process.GetProcesses()
								where proc.ProcessName == "osu!"
								select proc;

			if (osuCandidates.Count() == 1)
			{
				TargetPid = osuCandidates.First().Id;
			}
			else
			{
				string osuPath = OsuHelper.GetOsuPath();
				if (osuPath == null)
				{
					new Thread(new ThreadStart(() =>
					{
						MessageBox.Show("osu!를 찾지 못했습니다. osu!를 한 번 실행해주세요.");
					})).Start();
					return false;
				}

				// CreateAndInject doesn't work. I don't know reason.
				TargetPid = Process.Start(osuPath).Id;
			}

			RemoteHooking.Inject(TargetPid, InjectionOptions.DoNotRequireStrongName, injectee, injectee);
		}
		catch (Exception extInfo)
		{
			// Unless creating new thread, MessageBox will disappear immediately
			// when invoked from tray icon without window.
			new Thread(new ThreadStart(() =>
			{
				MessageBox.Show("DLL파일이 있는지, 관리자 권한이 있는지 확인해주세요.", "후킹 실패");
			})).Start();

			LogException(extInfo);
			return false;
		}

		try
		{
			var pipeFactory = new DuplexChannelFactory<IOsuInjectee>(
				this, new NetNamedPipeBinding(),
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

	void ChannelFaulted(object sender, EventArgs e)
	{
		IsInstalled = false;
		IsHooking = false;
		InjecteeProxy = null;

		if (AutoTerminate)
		{
			// BeginInvokeShutdown doesn't care OnExit() and the others.
			Application.Current.Dispatcher.BeginInvoke(new Action(() =>
			{
				Application.Current.Shutdown();
			}));
		}
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
