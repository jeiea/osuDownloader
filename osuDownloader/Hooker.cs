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

public class InvokeDownload : MarshalByRefObject
{
	static string Cookie;

	public void ReportException(Exception info)
	{
		MessageBox.Show(info.Message);
	}

	public void Ping()
	{
	}
}

public class OsuHooker
{
	static string ChannelName = null;
	public static bool IsHooked;

	public static bool Hook()
	{
		if (IsHooked)
			return true;

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

			RemoteHooking.IpcCreateServer<InvokeDownload>(ref ChannelName, WellKnownObjectMode.SingleCall);

			var osuCandidates = from proc in Process.GetProcesses()
								where proc.ProcessName == "osu!"
								select proc;

			int targetPid;
			if (osuCandidates.Count() == 1)
			{
				targetPid = osuCandidates.First().Id;
			}
			else
			{
				// CreateAndInject doesn't work. I don't know reason.
				targetPid = Process.Start(OsuHelper.GetOsuPath()).Id;
			}

			RemoteHooking.Inject(targetPid, injectee, injectee, ChannelName);
		}
		catch (Exception extInfo)
		{
			LogException(extInfo);
			return false;
		}

		IsHooked = true;
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
