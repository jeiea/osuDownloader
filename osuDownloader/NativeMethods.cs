using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace OsuDownloader
{
static class NativeMethods
{
	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	public static extern bool PeekMessage(out NativeMessage lpMsg, HandleRef hWnd, uint wMsgFilterMin,
										  uint wMsgFilterMax, uint wRemoveMsg);

	[DllImport("user32.dll")]
	public static extern IntPtr DispatchMessage([In] ref Message lpmsg);
}

[StructLayout(LayoutKind.Sequential)]
public struct NativeMessage
{
	public IntPtr handle;
	public uint msg;
	public IntPtr wParam;
	public IntPtr lParam;
	public uint time;
	public System.Drawing.Point p;
}
}
