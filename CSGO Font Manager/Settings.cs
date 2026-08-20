using System.Collections.Generic;

namespace CSGO_Font_Manager
{
    public class Settings
    {
        public string CsgoPath { get; set; }
        public bool ProTips { get; set; } = true;
        public bool HideNewUpdates { get; set; }
        public string ActiveFont { get; set; }
        public float FontScale { get; set; } = 1.00f;
        public string SpecificFontViewMode { get; set; } = "Group by UI role";
        public Dictionary<string, string> SpecificFontAssignments { get; set; } = new Dictionary<string, string>();
    }
}
