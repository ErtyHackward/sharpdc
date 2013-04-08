// -------------------------------------------------------------
// SharpDc project 
// written by Vladislav Pozdnyakov (hackward@gmail.com) 2012-2013
// licensed under the LGPL
// -------------------------------------------------------------

using System;

namespace SharpDc.Events
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