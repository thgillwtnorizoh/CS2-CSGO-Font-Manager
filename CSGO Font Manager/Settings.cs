using System.Collections.Generic;

namespace CSGO_Font_Manager
{
    public class Settings
    {
        // Backward-compatibility alias used by the original/CS2 code. 4.0 keeps this synced to Cs2Path.
        public string CsgoPath { get; set; }
        public string Cs2Path { get; set; }
        public string LegacyCsgoPath { get; set; }
        public string ActiveGame { get; set; } = "CS2";

        public bool ProTips { get; set; } = true;
        public bool HideNewUpdates { get; set; }
        public string ActiveFont { get; set; }
        public float FontScale { get; set; } = 1.00f;
        public string SpecificFontViewMode { get; set; } = "Group by UI role";
        public Dictionary<string, string> SpecificFontAssignments { get; set; } = new Dictionary<string, string>();
        public Dictionary<string, string> CsgoSpecificFontAssignments { get; set; } = new Dictionary<string, string>();
    }
}
