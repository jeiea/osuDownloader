////////////////////////////////////////////////////////////////////////////////////////////////////
// file:    WpfHotkey.cs
//
// summary: Implements the WPF hotkey class
////////////////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;

namespace OsuDownloader
{
public sealed class WpfHotkey : IDisposable
{
	public event Action<WpfHotkey> HotKeyPressed;

	public Keys Key { get; private set; }

	public ModifierKeys KeyModifier { get; private set; }

	private readonly int Id;
	private bool IsKeyRegistered;
	readonly IntPtr Handle;

	public WpfHotkey(ModifierKeys modifierKeys, Keys key, Window window)
	: this(modifierKeys, key, new WindowInteropHelper(window))
	{
	}

	public WpfHotkey(ModifierKeys modifierKeys, Keys key, WindowInteropHelper window)
	: this(modifierKeys, key, window.Handle)
	{
	}

	public WpfHotkey(ModifierKeys modifierKeys, Keys key, IntPtr windowHandle)
	{
		Contract.Requires(modifierKeys != ModifierKeys.None || key != Keys.None);
		Contract.Requires(windowHandle != IntPtr.Zero);

		Key = key;
		KeyModifier = modifierKeys;
		Id = GetHashCode();
		Handle = windowHandle;
		RegisterHotKey();
		ComponentDispatcher.ThreadPreprocessMessage += ThreadPreprocessMessageMethod;
	}

	~WpfHotkey()
	{
		Dispose();
	}

	public void RegisterHotKey()
	{
		if (Key == Keys.None)
			return;
		if (IsKeyRegistered)
			UnregisterHotKey();
		IsKeyRegistered = RegisterHotKey(Handle, Id, (uint)KeyModifier, (uint)Key);
		if (!IsKeyRegistered)
			throw new ApplicationException("Hotkey already in use");
	}

	public void UnregisterHotKey()
	{
		IsKeyRegistered = !UnregisterHotKey(Handle, Id);
	}

	public void Dispose()
	{
		ComponentDispatcher.ThreadPreprocessMessage -= ThreadPreprocessMessageMethod;
		UnregisterHotKey();
	}

	private void ThreadPreprocessMessageMethod(ref MSG msg, ref bool handled)
	{
		if (!handled)
		{
			if (msg.message == WmHotKey && (int)(msg.wParam) == Id)
			{
				OnHotKeyPressed();
				handled = true;
			}
		}
	}

	private void OnHotKeyPressed()
	{
		if (HotKeyPressed != null)
			HotKeyPressed(this);
	}

	const int WmHotKey = 0x0312;

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
}
