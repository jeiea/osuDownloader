////////////////////////////////////////////////////////////////////////////////////////////////////
// file:    Injectee\WinFormHotKey.cs
//
// summary: Implements the Winform hot key class
////////////////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Input;

namespace OsuDownloader.Injectee
{
// http://www.liensberger.it/web/blog/?p=207
public sealed class WinFormHotKey : IDisposable
{

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	static extern bool UnregisterHotKey(IntPtr hWnd, int id);

	HandleWindow Window = new HandleWindow();

	int CurrentId;

	/// <summary>   Represents the window that is used internally to get the messages. </summary>
	public class HandleWindow : NativeWindow, IDisposable
	{
		const int WM_HOTKEY = 0x0312;

		public HandleWindow()
		{
			// create the handle for the window.
			this.CreateHandle(new CreateParams());
		}

		////////////////////////////////////////////////////////////////////////////////////////////////////
		/// <summary>   Overridden to get the notifications. </summary>
		///
		/// <param name="m">    [in,out]. </param>
		////////////////////////////////////////////////////////////////////////////////////////////////////
		protected override void WndProc(ref Message m)
		{
			base.WndProc(ref m);

			// check if we got a hot key pressed.
			if (m.Msg == WM_HOTKEY)
			{
				// get the keys.
				Keys key = (Keys)(((int)m.LParam >> 16) & 0xFFFF);
				ModifierKeys modifier = (ModifierKeys)((int)m.LParam & 0xFFFF);

				// invoke the event to notify the parent.
				if (KeyPressed != null)
					KeyPressed(this, new KeyPressedEventArgs(modifier, key));
			}
		}

		public event EventHandler<KeyPressedEventArgs> KeyPressed;

		#region IDisposable Members

		public void Dispose()
		{
			this.DestroyHandle();
		}

		#endregion
	}

	public WinFormHotKey()
	{
		// register the event of the inner native window.
		Window.KeyPressed += delegate(object sender, KeyPressedEventArgs args)
		{
			if (KeyPressed != null)
				KeyPressed(this, args);
		};
	}

	////////////////////////////////////////////////////////////////////////////////////////////////////
	/// <summary>   Registers a hot key in the system. </summary>
	///
	/// <exception cref="InvalidOperationException">    Thrown when the requested operation is
	///                                                 invalid. </exception>
	///
	/// <param name="modifier"> The modifiers that are associated with the hot key. </param>
	/// <param name="key">      The key itself that is associated with the hot key. </param>
	////////////////////////////////////////////////////////////////////////////////////////////////////
	public void RegisterHotKey(ModifierKeys modifier, Keys key, bool repeat = false)
	{
		// increment the counter.
		CurrentId = CurrentId + 1;

		// register the hot key.
		const int MOD_REPEAT = 0x4000;
		uint finalMod = (uint)modifier | (uint)(repeat ? MOD_REPEAT : 0);
		if (!RegisterHotKey(Window.Handle, CurrentId, finalMod, (uint)key))
			throw new InvalidOperationException("Couldn't register the hot key.");
	}

	/// <summary>   A hot key has been pressed. </summary>
	public event EventHandler<KeyPressedEventArgs> KeyPressed;

	#region IDisposable Members

	public void Dispose()
	{
		// unregister all the registered hot keys.
		for (int i = CurrentId; i > 0; i--)
		{
			UnregisterHotKey(Window.Handle, i);
		}

		// dispose the inner native window.
		Window.Dispose();
	}

	#endregion
}

/// <summary>   Event Args for the event that is fired after the hot key has been pressed. </summary>
public class KeyPressedEventArgs : EventArgs
{
	internal KeyPressedEventArgs(ModifierKeys modifier, Keys key)
	{
		Modifier = modifier;
		Key = key;
	}

	public ModifierKeys Modifier
	{
		get;
		private set;
	}

	public Keys Key
	{
		get;
		private set;
	}
}
}
