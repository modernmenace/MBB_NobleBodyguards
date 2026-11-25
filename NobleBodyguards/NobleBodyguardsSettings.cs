using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;

namespace NobleBodyguards
{
    /// <summary>Provides the MCM options for Noble Bodyguards.</summary>
    public class NobleBodyguardsSettings : AttributeGlobalSettings<NobleBodyguardsSettings>
    {
        public override string Id => "NobleBodyguards";

        public override string DisplayName => "Noble Bodyguards";

        public override string FolderName => "NobleBodyguards";

        public override string FormatType => "json2";

        /// <summary>Gets or sets whether bodyguards are added to eligible settlement missions.</summary>
        [SettingPropertyGroup("General", GroupOrder = 0)]
        [SettingPropertyBool(
            "Enable bodyguards",
            Order = 0,
            RequireRestart = false,
            HintText = "Spawn bodyguards in eligible settlement missions.")]
        public bool Enabled { get; set; } = true;

        /// <summary>Gets or sets whether only the player receives bodyguards, disabling companion and lord escorts.</summary>
        [SettingPropertyGroup("General")]
        [SettingPropertyBool(
            "Player bodyguards only",
            Order = 1,
            RequireRestart = false,
            HintText = "When enabled, only the player receives bodyguards. Companions and lords get none.")]
        public bool PlayerOnly { get; set; } = false;

        /// <summary>Gets or sets whether lord army troops walk around villages alongside the villagers.</summary>
        [SettingPropertyGroup("General")]
        [SettingPropertyBool(
            "Army troops in villages",
            Order = 2,
            RequireRestart = false,
            HintText = "Spawn non-hero troops from lord armies that are present in a village, walking alongside the villagers.")]
        public bool ArmyTroopsInVillages { get; set; } = true;

        /// <summary>Gets or sets whether diagnostic messages are shown in the game UI.</summary>
        [SettingPropertyGroup("Advanced", GroupOrder = 1)]
        [SettingPropertyBool(
            "Debug messages",
            Order = 0,
            RequireRestart = false,
            HintText = "Show diagnostic messages from army troop walkers in village centers.")]
        public bool DebugMessages { get; set; } = false;
    }
}