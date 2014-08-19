using EasyHook;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Remoting;
using System.ServiceModel;
using System.Text;
using System.Threading;
using System.Windows;
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
	void OptionChanged();

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

	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>   Convert own option to cookie form. </summary>
	///
	/// <returns>   A bloodcat download option cookie. </returns>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	public Cookie ToCookie()
	{
		StringBuilder cookieJson = new StringBuilder(100);
		cookieJson.Append("{\"direct\":false,\"droid\":false,\"bg\":");

		switch (Background)
		{
		case BloodcatWallpaperOption.RemoveBackground:
			cookieJson.Append("\"delete\"");
			break;
		case BloodcatWallpaperOption.SolidColor:
			cookieJson.Append("\"color\"");

			var color = BackgroundColor;
			byte[] rgb = new byte[] { color.R, color.G, color.B };
			cookieJson.Append(",\"color\":\"#");
			cookieJson.Append(BitConverter.ToString(rgb).Replace("-", string.Empty));
			cookieJson.Append('"');
			break;
		default:
			cookieJson.Append("false");
			break;
		}
		cookieJson.Append(",\"video\":");
		cookieJson.Append(RemoveVideoAndStoryboard ? "true" : "false");
		cookieJson.Append(",\"skin\":");
		cookieJson.Append(RemoveSkin ? "true" : "false");
		cookieJson.Append('}');

		return new Cookie("DLOPT", Uri.EscapeDataString(cookieJson.ToString()), "/", "bloodcat.com");
	}
}

public class MainWindowViewModel : ICallback, INotifyPropertyChanged
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
				InjecteeProxy.OptionChanged();
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

	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>
	/// Toggle beatmap download hook state. If not installed yet, it will try injection.
	/// </summary>
	///
	/// <returns>   true if it succeeds, false if it fails. </returns>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	private bool ToggleHook()
	{
		// If connection fails, then try injection.
		try
		{
			ConnectToInjectee();
			InjecteeProxy.SetDownloadHook(!IsHooking);
			return true;
		}
		catch { }

		// injection.
		try
		{
			string thisFile = new Uri(System.Reflection.Assembly.GetExecutingAssembly().Location).LocalPath;

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
				Process osu =  Process.Start(osuPath);
				TargetPid = osu.Id;
				osu.WaitForInputIdle();
			}

			RemoteHooking.Inject(TargetPid, InjectionOptions.DoNotRequireStrongName, thisFile, thisFile);
		}
		catch (Exception extInfo)
		{
			LogException(extInfo);
		}

		try
		{
			ThreadPool.QueueUserWorkItem(new WaitCallback(obj =>
			{
				try
				{
					ConnectToInjectee();
				}
				catch (Exception e)
				{
					MessageBox.Show("DLL파일이 있는지, 관리자 권한이 있는지 확인해주세요.", "후킹 실패");
					LogException(e);
				}
			}));
		}
		catch (Exception e)
		{
			LogException(e);
			return false;
		}

		return true;
	}

	/// <summary>   Connects to injectee. If already connected, it does nothing. </summary>
	private void ConnectToInjectee()
	{
		try
		{
			var proxy = InjecteeProxy as ICommunicationObject;
			if (proxy != null)
			{
				if (proxy.State != CommunicationState.Opened)
				{
					proxy.Abort();
					InjecteeProxy = null;
				}
				else
				{
					return;
				}
			}
		}
		catch { }

		var pipeFactory = new DuplexChannelFactory<IOsuInjectee>(
			this, new NetNamedPipeBinding(),
			new EndpointAddress("net.pipe://localhost/osuBeatmapHooker"));

		InjecteeProxy = pipeFactory.CreateChannel();

		InjecteeProxy.Subscribe();

		(InjecteeProxy as ICommunicationObject).Faulted += ChannelFaulted;
	}

	void ChannelFaulted(object sender, EventArgs e)
	{
		InjecteeProxy = null;

		try
		{
			ConnectToInjectee();
		}

		catch
		{
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
