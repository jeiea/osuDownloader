using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading;
using System.Windows;

using EasyHook;

namespace OsuDownloader
{
/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
	private Mutex InstanceMutex;

	protected override void OnStartup(StartupEventArgs e)
	{
		bool isSoleInstance;

		InstanceMutex = new Mutex(true, "osu! Beatmap Downloader by jeiea", out isSoleInstance);
		if (isSoleInstance == false)
		{
			InstanceMutex = null;
			Application.Current.Shutdown();
			return;
		}

		base.OnStartup(e);
	}

	protected override void OnExit(ExitEventArgs e)
	{
		if (InstanceMutex != null)
		{
			InstanceMutex.ReleaseMutex();
		}
		base.OnExit(e);
	}
}
}
