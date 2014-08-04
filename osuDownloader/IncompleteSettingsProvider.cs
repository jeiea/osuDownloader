using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Xml.Linq;
using System.Xml.XPath;

namespace OsuDownloader
{
public static class XExtensions
{
	public static XElement GetOrAddElement(this XContainer parent, XName name)
	{
		Contract.Requires<ArgumentNullException>(parent != null);
		Contract.Requires<ArgumentNullException>(name != null);

		var element = parent.Element(name);
		if (element == null)
		{
			element = new XElement(name);
			parent.Add(element);
		}
		return element;
	}
}

// Derived from http://www.codeproject.com/Articles/20917/Creating-a-Custom-Settings-Provider?msg=2934144#xx2934144xx
public class IncompleteSettingsProvider : SettingsProvider
{
	const string SettingsRootName = "Settings";
	const string RoamingSettingsRootName = "Roaming";
	const string LocalSettingsRootName = "Local";

	readonly string FileName;
	readonly Lazy<XDocument> SettingsXml;

	public static readonly string DefaultDirectory = Path.GetDirectoryName(Application.ExecutablePath);
	public static readonly string DefaultFileName = Path.GetFileNameWithoutExtension(Application.ExecutablePath) + ".settings";

	public IncompleteSettingsProvider() : this(DefaultDirectory, DefaultFileName)
	{
	}

	public IncompleteSettingsProvider(string settingsDirectory, string settingsFileName)
	{
		Contract.Requires<ArgumentException>(!string.IsNullOrWhiteSpace(settingsFileName));
		Contract.Requires<ArgumentException>(!string.IsNullOrWhiteSpace(settingsDirectory));

		Directory.CreateDirectory(settingsDirectory);

		FileName = Path.Combine(settingsDirectory, settingsFileName);
		SettingsXml = new Lazy<XDocument>(() => LoadOrCreateSettings(FileName), LazyThreadSafetyMode.PublicationOnly);
	}

	public override void Initialize(string name, NameValueCollection collection)
	{
		base.Initialize(this.ApplicationName, collection);
	}

	public override string ApplicationName
	{
		get { return Path.GetFileNameWithoutExtension(Application.ExecutablePath); }
		set { }
	}

	public override string Name
	{
		get { return GetType().Name; }
	}

	public override void SetPropertyValues(SettingsContext context, SettingsPropertyValueCollection properties)
	{
		// Only dirty settings are included in properties, and only ones relevant to this provider
		foreach (SettingsPropertyValue propertyValue in properties)
		{
			SetValue(propertyValue);
		}

		try
		{
			SettingsXml.Value.Save(FileName);
		}
		catch (Exception ex)
		{
			//Log.WriteError(string.Concat(Errors.Save_Settings_Error, ":", Environment.NewLine), ex);
		}
	}

	public override SettingsPropertyValueCollection GetPropertyValues(
		SettingsContext context, SettingsPropertyCollection props)
	{
		// Create new collection of values
		var values = new SettingsPropertyValueCollection();

		// Iterate through the settings to be retrieved
		foreach (SettingsProperty setting in props)
		{
			values.Add(new SettingsPropertyValue(setting)
			{
				IsDirty = false,
				SerializedValue = GetValue(setting),
			});
		}
		return values;
	}

	XElement SettingsRoot
	{
		get { return SettingsXml.Value.Root; }
	}

	object GetValue(SettingsProperty setting)
	{
		var propertyPath = IsRoaming(setting)
						   ? string.Concat("./", RoamingSettingsRootName, "/", setting.Name)
						   : string.Concat("./", LocalSettingsRootName, "/", Environment.MachineName, "/", setting.Name);

		var propertyElement = SettingsRoot.XPathSelectElement(propertyPath);
		return propertyElement == null ? setting.DefaultValue : propertyElement.Value;
	}

	void SetValue(SettingsPropertyValue setting)
	{
		var parentElement = IsRoaming(setting.Property)
							? SettingsRoot.GetOrAddElement(RoamingSettingsRootName)
							: SettingsRoot.GetOrAddElement(LocalSettingsRootName)
							.GetOrAddElement(Environment.MachineName);

		parentElement.GetOrAddElement(setting.Name).Value = setting.SerializedValue.ToString();
	}

	static XDocument LoadOrCreateSettings(string filePath)
	{
		XDocument settingsXml = null;
		try
		{
			settingsXml = XDocument.Load(filePath);
			Contract.Assert(settingsXml.Root != null);

			if (settingsXml.Root.Name.LocalName != SettingsRootName)
			{
				//Log.WriteError(string.Format(Errors.Invalid_Settings_Format_0, filePath));
				settingsXml = null;
			}
		}
		catch (Exception ex)
		{
			//Log.WriteError(string.Format(Errors.Invalid_Settings_File_0, filePath), ex);
		}

		return settingsXml ??
			   new XDocument(
				   new XDeclaration("1.0", "utf-8", "yes"),
				   new XElement(SettingsRootName, string.Empty)
			   );
	}

	static bool IsRoaming(SettingsProperty property)
	{
		return property.Attributes
			   .Cast<DictionaryEntry>()
			   .Any(a => a.Value is SettingsManageabilityAttribute);
	}
}
}
