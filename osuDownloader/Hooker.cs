using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.Remoting;
using EasyHook;
using System.Windows;
using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Diagnostics;

namespace OsuDownloader
{

public class InvokeDownload : MarshalByRefObject
{
	static string Cookie;
	static string DownloadDir = "C:\\";

	public void IsInstalled(int clientPid)
	{
		Console.WriteLine("FileMon has been installed in target {0}.\r\n", clientPid);
	}

	public void OnBeatmapBrowse(int clientPid, string fileName)
	{
		StringBuilder query = new StringBuilder("http://bloodcat.com/osu/?mod=json");

		char idKind = fileName[18];
		query.Append("&m=");
		query.Append(idKind);

		query.Append("&q=");
		query.Append(fileName.Split('/')[4]);

		WebClient client = new WebClient();
		client.Encoding = Encoding.UTF8;

		byte[] json = client.DownloadData(new Uri(query.ToString()));
		JObject result = JObject.Parse(Encoding.UTF8.GetString(json));

		int count = (int)result["resultCount"];
		if (count != 1)
		{
			MessageBox.Show("해당하는 비트맵 수가 " + count + "개 입니다.");
			return;
		}

		var beatmapJson = result["results"][0];
		int beatmapId = (int)beatmapJson["id"];
		string beatmapTitle = (string)beatmapJson["title"];
		beatmapTitle = string.Concat(beatmapTitle.Except(System.IO.Path.GetInvalidFileNameChars()));
		string downloadPath = DownloadDir + beatmapTitle + ".osz";
		client.DownloadFile("http://bloodcat.com/osu/m/" + beatmapId, downloadPath);

		ProcessStartInfo psi = new ProcessStartInfo();
		psi.Verb = "open";
		psi.FileName = downloadPath;
		psi.UseShellExecute = true;
		Process.Start(psi);
	}

	public void OnTerminate()
	{
		OsuHooker.IsHooked = false;
	}

	public void ReportException(Exception info)
	{
		MessageBox.Show(info.Message);
	}

	public void Ping()
	{
	}
}

class OsuHooker
{
	static string ChannelName = null;
	public static bool IsHooked;

	public static bool Hook()
	{
		if (IsHooked)
			return true;

		var osuCandidates = from proc in Process.GetProcesses()
							where proc.ProcessName == "osu!"
							select proc;

		if (osuCandidates.Count() != 1)
		{
			return false;
		}

		int targetPid = osuCandidates.First().Id;

		try
		{
			string thisFile = new Uri(System.Reflection.Assembly.GetExecutingAssembly().Location).LocalPath;
			string injectee = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "RemoteDown.dll");

			try
			{
				Config.Register("Osu beatmap downloader.", thisFile, injectee);
			}
			catch (ApplicationException)
			{
				MessageBox.Show("DLL파일이 있는지, 관리자 권한이 있는지 확인해주세요.",
								"후킹 실패", MessageBoxButton.OK);
				return false;
			}

			RemoteHooking.IpcCreateServer<InvokeDownload>(ref ChannelName, WellKnownObjectMode.SingleCall);

			RemoteHooking.Inject(targetPid, injectee, injectee, ChannelName);
		}
		catch (Exception extInfo)
		{
			var log = System.IO.File.AppendText("osuDownloader.log");
			log.WriteLine("------------------------");
			var inner = extInfo;
			while (inner != null)
			{
				log.WriteLine(extInfo.Message);
				inner = inner.InnerException;
			}
			log.WriteLine("------------------------");
			log.Close();
			return false;
		}

		IsHooked = true;
		return true;
	}
}
}
