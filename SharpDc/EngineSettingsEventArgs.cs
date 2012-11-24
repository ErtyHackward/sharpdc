using System;

namespace QuickDc
{
    public class EngineSettingsEventArgs : EventArgs
    {
        public EngineSettingType SettingType { get; set; }

        public EngineSettingsEventArgs(EngineSettingType st)
        {
            SettingType = st;
        }
    }
}