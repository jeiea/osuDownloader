using System.Configuration;

namespace OsuDownloader.Properties
{

// This class allows you to handle specific events on the settings class:
//  The SettingChanging event is raised before a setting's value is changed.
//  The PropertyChanged event is raised after a setting's value is changed.
//  The SettingsLoaded event is raised after the setting values are loaded.
//  The SettingsSaving event is raised before the setting values are saved.
[SettingsProvider(typeof(OsuDownloader.PortableSettingsProvider))]
internal sealed partial class Settings
{

	public Settings()
	{
		// // To add event handlers for saving and changing settings, uncomment the lines below:
		//
		// this.SettingChanging += this.SettingChangingEventHandler;
		//
		// this.SettingsSaving += this.SettingsSavingEventHandler;
		//
		PropertyChanged += Settings_PropertyChanged;
	}

	void Settings_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
	{
		Save();
	}
}
}
