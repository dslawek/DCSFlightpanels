﻿namespace SamplePanelEventPlugin
{
    using System.Collections.Generic;
    using System.ComponentModel.Composition;

    using MEF;

    /*
     * Use this class as a template for your plugin.
     * Reference the MEF project where the interface and necessary files are located.
     */


    [Export(typeof(IPanelEventHandler))]
    public class PanelEventHandler : IPanelEventHandler
    {
        public string PluginGuid =>
            /*
             * Generate a new Guid for YOUR plugin
             */
            "b2d3e289-6388-4d42-9256-861acf0990e7";

        public string PluginName => "Sample Plugin";

        public void PanelEvent(string profile, string panelHidId, int panelId, int switchId, bool pressed, SortedList<int, IKeyPressInfo> keySequence)
        {
            /*
             * Your code here
             */
            PanelEventFileWriter.WriteInfo(profile, panelHidId, panelId, switchId, pressed, keySequence);
        }
    }
}
