﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Configuration;
using System.Configuration.Provider;
using System.Windows.Forms;
using System.Collections.Specialized;
using Microsoft.Win32;
using System.Xml;
using System.IO;
using IniParser;
using IniParser.Model;
using System.Linq;

namespace OsuDownloader
{
// http://www.codeproject.com/Articles/20917/Creating-a-Custom-Settings-Provider?msg=2934144#xx2934144xx
public class IniSettingsProvider : SettingsProvider
{
	const string SettingsRoot = "Settings";

	/// <summary>   If true, store ini only to this class assembly directory. </summary>
	bool IsDistributed = false;

	public override void Initialize(string name, NameValueCollection col)
	{
		base.Initialize(this.ApplicationName, col);
	}

	public override string ApplicationName
	{
		get
		{
			if (IsDistributed)
			{
				if (Application.ProductName.Trim().Length > 0)
				{
					return Application.ProductName;
				}
				else
				{
					FileInfo fi = new FileInfo(Application.ExecutablePath);
					return fi.Name.Substring(0, fi.Name.Length - fi.Extension.Length);
				}
			}
			else
			{
				// Converted only dll path used.
				return Path.GetFileNameWithoutExtension(GetType().Assembly.Location);
			}
		}
		//Do nothing
		set { }
	}

	public override string Name
	{
		get { return "IniSettingsProvider"; }
	}

	public virtual string GetAppSettingsPath()
	{
		// Used to determine where to store the settings
		//System.IO.FileInfo fi = new System.IO.FileInfo(Application.ExecutablePath);
		//return fi.DirectoryName;
		return Path.GetDirectoryName(GetType().Assembly.Location);
	}

	public virtual string GetAppSettingsFileName()
	{
		// Used to determine the filename to store the settings
		return ApplicationName + ".ini";
	}


	public override void SetPropertyValues(SettingsContext context, SettingsPropertyValueCollection propvals)
	{
		// Iterate through the settings to be stored
		// Only dirty settings are included in propvals, and only ones relevant to this provider
		foreach (SettingsPropertyValue propval in propvals)
		{
			SettingsIni.Global[propval.Name] = propval.SerializedValue.ToString();
			//SetValue(propval);
		}

		try
		{
			//SettingsXML.Save(Path.Combine(GetAppSettingsPath(), GetAppSettingsFileName()));
			var parser = new FileIniDataParser();
			parser.WriteFile(Path.Combine(GetAppSettingsPath(), GetAppSettingsFileName()), SettingsIni);
		}
		catch (Exception ex)
		{
		}
		// Ignore if cant save, device been ejected
	}

	/// <summary>   The setting data held. </summary>
	IniData SettingsIni = new IniData();

	public override SettingsPropertyValueCollection GetPropertyValues(SettingsContext context, SettingsPropertyCollection props)
	{
		// Create new collection of values
		SettingsPropertyValueCollection values = new SettingsPropertyValueCollection();

		try
		{
			var parser = new FileIniDataParser();
			SettingsIni = parser.ReadFile(Path.Combine(GetAppSettingsPath(), GetAppSettingsFileName()));
		}
		catch { }

		// Iterate through the settings to be retrieved
		foreach (SettingsProperty setting in props)
		{
			SettingsPropertyValue value = new SettingsPropertyValue(setting);
			value.IsDirty = false;
			value.SerializedValue = SettingsIni.Global[setting.Name] ?? string.Empty;
			//value.SerializedValue = GetValue(setting);
			values.Add(value);
		}

		return values;
	}

	private XmlDocument _settingsXML = null;

	private XmlDocument SettingsXML
	{
		get
		{
			//If we dont hold an xml document, try opening one.
			//If it doesnt exist then create a new one ready.
			if (_settingsXML == null)
			{
				_settingsXML = new XmlDocument();

				try
				{
					_settingsXML.Load(Path.Combine(GetAppSettingsPath(), GetAppSettingsFileName()));
				}
				catch (Exception ex)
				{
					//Create new document
					XmlDeclaration dec = _settingsXML.CreateXmlDeclaration("1.0", "utf-8", string.Empty);
					_settingsXML.AppendChild(dec);

					XmlNode nodeRoot = default(XmlNode);

					nodeRoot = _settingsXML.CreateNode(XmlNodeType.Element, SettingsRoot, "");
					_settingsXML.AppendChild(nodeRoot);
				}
			}

			return _settingsXML;
		}
	}

	private string GetValue(SettingsProperty setting)
	{
		string ret = "";

		try
		{
			if (IsRoaming(setting))
			{
				ret = SettingsXML.SelectSingleNode(SettingsRoot + "/" + setting.Name).InnerText;
			}
			else
			{
				ret = SettingsXML.SelectSingleNode(SettingsRoot + "/" + Environment.MachineName + "/" + setting.Name).InnerText;
			}
		}

		catch (Exception ex)
		{
			if (setting.DefaultValue != null)
			{
				ret = setting.DefaultValue.ToString();
			}
			else
			{
				ret = "";
			}
		}

		return ret;
	}

	private void SetValue(SettingsPropertyValue propVal)
	{
		XmlElement MachineNode = default(XmlElement);
		XmlElement SettingNode = default(XmlElement);

		//Determine if the setting is roaming.
		//If roaming then the value is stored as an element under the root
		//Otherwise it is stored under a machine name node
		try
		{
			if (IsRoaming(propVal.Property))
			{
				SettingNode = (XmlElement)SettingsXML.SelectSingleNode(SettingsRoot + "/" + propVal.Name);
			}
			else
			{
				SettingNode = (XmlElement)SettingsXML.SelectSingleNode(SettingsRoot + "/" + Environment.MachineName + "/" + propVal.Name);
			}
		}
		catch (Exception ex)
		{
			SettingNode = null;
		}

		//Check to see if the node exists, if so then set its new value
		if (SettingNode != null)
		{
			SettingNode.InnerText = propVal.SerializedValue.ToString();
		}
		else
		{
			if (IsRoaming(propVal.Property))
			{
				// Store the value as an element of the Settings Root Node
				SettingNode = SettingsXML.CreateElement(propVal.Name);
				SettingNode.InnerText = propVal.SerializedValue.ToString();
				SettingsXML.SelectSingleNode(SettingsRoot).AppendChild(SettingNode);
			}
			else
			{
				// Its machine specific, store as an element of the machine name node,
				// creating a new machine name node if one doesn't exist.
				try
				{
					MachineNode = (XmlElement)SettingsXML.SelectSingleNode(SettingsRoot + "/" + Environment.MachineName);
				}
				catch (Exception ex)
				{
					MachineNode = SettingsXML.CreateElement(Environment.MachineName);
					SettingsXML.SelectSingleNode(SettingsRoot).AppendChild(MachineNode);
				}

				if (MachineNode == null)
				{
					MachineNode = SettingsXML.CreateElement(Environment.MachineName);
					SettingsXML.SelectSingleNode(SettingsRoot).AppendChild(MachineNode);
				}

				SettingNode = SettingsXML.CreateElement(propVal.Name);
				SettingNode.InnerText = propVal.SerializedValue.ToString();
				MachineNode.AppendChild(SettingNode);
			}
		}
	}

	private bool IsRoaming(SettingsProperty prop)
	{
		//Determine if the setting is marked as Roaming
		foreach (DictionaryEntry d in prop.Attributes)
		{
			Attribute a = (Attribute)d.Value;
			if (a is System.Configuration.SettingsManageabilityAttribute)
			{
				return true;
			}
		}
		return false;
	}
}
}