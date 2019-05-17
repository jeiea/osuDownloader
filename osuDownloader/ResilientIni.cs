using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ResilientIni
{

class Sample
{
	static void Main()
	{
		ResilientIni ini;
		//ini.Global["asdf"] = "fdsa";
	}
}

static class IniUtil
{
	static string EscapeChar(char c)
	{
		return string.Format((c > 0xFF) ? "\\x{0:X2}" : "\\x{0:X4}", c);
	}

	static string EscapeString(string s)
	{
		StringBuilder builder = new StringBuilder(s.Length * 4 + 10);

		foreach (char c in s)
		{
			builder.Append(EscapeChar(c));
		}

		return builder.ToString();
	}

	public static string Escape(string s, string[] additionals = null)
	{
		var builder = new StringBuilder(s.Length + 30);

		foreach (char c in s)
		{
			if (char.IsControl(c))
			{
				builder.Append(EscapeChar(c));
			}
			else if (c == '\\')
			{
				builder.Append("\\\\");
			}
			else
			{
				builder.Append(c);
			}
		}

		// Additional replacement
		if (additionals != null)
		{
			foreach (var additional in additionals)
			{
				if (s.IndexOf(additional) != -1)
				{
					s.Replace(additional, EscapeString(additional));
				}
			}
		}

		return builder.ToString();
	}

	public static string Unescape(string str)
	{
		var escapement = @"(?<![\])\\x([0-9a-f]{1,4})";
		var evaluator = new MatchEvaluator(m =>
		{
			char c = (char)Convert.ToUInt16(m.Groups[1].ToString(), 16);
			return c.ToString();
		});
		// Replace only \xX ~ \xXXXX format.
		return Regex.Replace(str, escapement, evaluator, RegexOptions.IgnoreCase);
	}
}

abstract class IniField
{
	// Raw text for direct modification.
	protected string _Text;
	public virtual string Text
	{
		get
		{
			return _Text;
		}
		set
		{
			_Text = value;
		}
	}
}

class IniComment : IniField
{
	public IniComment()
	{
		Text = ";";
	}

	public IniComment(string content)
	{
		Text = ";" + content;
	}

	// 실제 foreach로 돌다간 이걸로 할당을 못 하지만...
	public static implicit operator IniComment(string s)
	{
		return new IniComment(s);
	}

	public static implicit operator string(IniComment comment)
	{
		return comment.ToString();
	}

	public override string ToString()
	{
		int idx = Text.IndexOf(';');
		return (idx == -1) ? Text : Text.Substring(idx + 1);
	}
}

// TODO: Strict 모드 구현

class IniKeyValue : IniField
{
	public override string Text
	{
		get
		{
			// TODO: 대용량 텍스트를 읽는 경우의 불상사 대책
			return base.Text ?? IniUtil.Escape(Key) + '=' + IniUtil;

			// 나중에 추가적인 주석이라든가 추가 가능
			var otherDelims = new string[] { "=", ";" };
			string escaped = IniUtil.Escape(value, otherDelims);
			var otherDelims = new string[] { ";" };
			string escaped = IniUtil.Escape(value, otherDelims);
		}
		set
		{
			base.Text = value;
		}
	}

	string _Key;
	public string Key
	{
		get
		{
			return _Key;
		}
		set
		{
			base.Text = null;
			_Key = value;
		}
	}

	string _Value;
	public string Value
	{
		get
		{
			return _Value;
		}
		set
		{
			base.Text = null;
			_Value = value;
		}
	}

	public IniKeyValue()
	{
		_Key = string.Empty;
		_Value = string.Empty;
	}

	public IniKeyValue(string key, string value)
	{
		Key = key;
		Value = value;
	}


}

class IniSection : IniField
{
	public override string Text
	{
		set
		{
			base.Text = value;

			int begIdx = value.IndexOf('[');
			int endIdx = value.IndexOf(']');

			if (begIdx == -1 || endIdx == -1 || begIdx >= endIdx)
			{
				throw new Exception();
			}

			string innerText = value.Substring(begIdx, endIdx - begIdx).Trim();
			_Name = IniUtil.Unescape(innerText);
		}
	}

	protected string _Name;
	public string Name
	{
		get
		{
			return _Name;
		}
		set
		{
			var escaped = IniUtil.Escape(value, new string[] { "]" });
			base.Text = '[' + escaped + ']';
			_Name = value;
		}
	}

	public IniSection()
	{
		Text = string.Empty;
	}

	public IniSection(string name)
	{
	}
}

class ResilientIni
{
}

}
