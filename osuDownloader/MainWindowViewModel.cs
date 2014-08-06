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
using System.Xml;

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
[ServiceContract(SessionMode = SessionMode.Required, CallbackContract = typeof(ICallback))]
public interface IOsuInjectee
{
	[OperationContract(IsOneWay = true)]
	void Subscribe();

	[OperationContract(IsOneWay = true)]
	void Unsubscribe();

	[OperationContract(IsOneWay = true)]
	void SetDownloadHook(bool request);

	[OperationContract(IsOneWay = true)]
	void ToggleHook(bool request);

	[OperationContract(IsOneWay = true)]
	void OptionChanged(BloodcatDownloadOption option);

	[OperationContract]
	bool IsHookEnabled();
}

/// <summary>   Wallpaper download option at bloodcat.com. </summary>
public enum BloodcatWallpaperOption { NoTouch, SolidColor, ReplaceWithPicture, RemoveBackground }

public class BloodcatDownloadOption
{
	public BloodcatWallpaperOption Background = BloodcatWallpaperOption.NoTouch;
	public System.Windows.Media.Color BackgroundColor = System.Windows.Media.Colors.Black;
	public bool RemoveVideoAndStoryboard;
	public bool RemoveSkin;
}

public class MainWindowViewModel : ICallback, INotifyPropertyChanged
{
	static int TargetPid;
	static IOsuInjectee InjecteeProxy;

	bool IsBossed;

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
			if (_IsHooking != value)
			{
				ToggleHook();
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
				OnPropertyChanged("StartAsTray");
			}
		}
	}

	public bool AutoTerminate
	{
		get { return Properties.Settings.Default.AutoTerminate; }
		set
		{
			if (AutoTerminate != value)
			{
				Properties.Settings.Default.AutoTerminate = value;
				OnPropertyChanged("AutoTerminate");
			}
		}
	}

	public BloodcatDownloadOption BloodcatOption
	{
		get { return Properties.Settings.Default.BloodcatOption; }
		set
		{
			Properties.Settings.Default.BloodcatOption = value;
			OnPropertyChanged("BloodcatOption");
			if (InjecteeProxy != null)
			{
				InjecteeProxy.OptionChanged(Properties.Settings.Default.BloodcatOption);
			}
		}
	}

	#endregion

	#region ICallback Implementation

	public void Installed()
	{
		_IsInstalled = true;
		_IsHooking = true;
		OnPropertyChanged("IsInstalled");
		OnPropertyChanged("IsHooking");
	}

	public void HookSwitched(bool status)
	{
		if (_IsHooking != status)
		{
			_IsHooking = status;
			OnPropertyChanged("IsHooking");
		}
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

	public MainWindowViewModel()
	{
		if (AutoStart)
		{
			ToggleHook();
		}
	}

	public void ToggleBoss()
	{
		IsBossed = !IsBossed;

		if (InjecteeProxy != null)
		{
			InjecteeProxy.ToggleHook(IsBossed);
		}
	}

	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>
	/// Toggle beatmap download hook state. If not installed yet, it will try injection.
	/// </summary>
	///
	/// <returns>   true if it succeeds, false if it fails. </returns>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	private bool ToggleHook()
	{
		try
		{
			if (IsHooking)
			{
				InjecteeProxy.SetDownloadHook(false);
				return true;
			}
			else if (IsInstalled)
			{
				InjecteeProxy.SetDownloadHook(true);
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

			RemoteHooking.Inject(TargetPid, InjectionOptions.DoNotRequireStrongName, "osuDownloader.exe", injectee);
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
		InjecteeProxy = null;

		if (AutoTerminate)
		{
			// BeginInvokeShutdown doesn't care OnExit() and the others.
			Application.Current.Dispatcher.BeginInvoke(new Action(() =>
			{
				Application.Current.Shutdown();
			}));
		}

		_IsHooking = false;
		_IsInstalled = false;
		OnPropertyChanged("IsInstalled");
		OnPropertyChanged("IsHooking");
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
