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
using System.ServiceModel;
using System.ComponentModel;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows;

namespace OsuDownloader.Injectee
{
[ServiceBehavior(InstanceContextMode = InstanceContextMode.Single)]
public class HookManager :  IOsuInjectee, EasyHook.IEntryPoint
{
	public static List<int> HookingThreadIds = new List<int>();

	ServiceHost InjecteeHost;
	List<ICallback> Callbacks = new List<ICallback>();

	FileNameHooker Blinder;
	InvokeUrlHooker Downloader;

	/// <summary>   Thread termination event. </summary>
	ManualResetEvent QuitEvent;

	public HookManager(RemoteHooking.IContext context)
	{
		InjecteeHost = new ServiceHost(this, new Uri[] { new Uri("net.pipe://localhost") });
		InjecteeHost.AddServiceEndpoint(typeof(IOsuInjectee), new NetNamedPipeBinding(), "osuBeatmapHooker");
		InjecteeHost.Open();
	}

	[STAThread]
	public void Run(RemoteHooking.IContext context)
	{
		AppDomain currentDomain = AppDomain.CurrentDomain;
		currentDomain.AssemblyResolve += (sender, args) =>
		{
			return this.GetType().Assembly.FullName == args.Name ? this.GetType().Assembly : null;
		};

		HookingThreadIds.Add((int)GetCurrentThreadId());

		try
		{
			Downloader = new InvokeUrlHooker();
			Downloader.SetHookState(true);
		}
		catch (Exception extInfo)
		{
			MainWindowViewModel.LogException(extInfo);
		}

		RemoteHooking.WakeUpProcess();

		QuitEvent = new ManualResetEvent(false);

		// wait for host process termination...
		try
		{
			QuitEvent.WaitOne();
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
			QuitEvent.Set();
		}
	}

	public void Unsubscribe()
	{
		var callback = OperationContext.Current.GetCallbackChannel<ICallback>();
		Callbacks.Remove(callback);

		if (Callbacks.Count <= 0)
		{
			QuitEvent.Set();
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

	public void OptionChanged(BloodcatDownloadOption option)
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

}
}
