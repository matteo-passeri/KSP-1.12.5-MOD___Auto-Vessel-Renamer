using System.Xml.Serialization;
using System.IO;
using System.Collections.Generic;
using KSP.IO;
using System.Linq;
using System.Diagnostics;
using UnityEngine;


public class AutoRenamerConfig
{
    public enum NumberingStyle
    {
        ArabicNumbers,
        ZeroPaddedNumbers,
        RomanNumerals,
        LatinLetters,
        GreekLetters,
        GreekWords,
        NATOPhonetic,
        RussianLetters,
        Date,
        PartsCount
    }

    // List of template presets
    public class Preset
    {
        public string DisplayName { get; set; }
        public string Name { get; set; }
        public string Suffix { get; set; }
        public NumberingStyle NumberingStyle { get; set; }
    }

    private static string presetsPath = "GameData/SmartVesselRenamer/Plugins/PluginData/SmartVesselRenamer/presets.xml";
    private PluginConfiguration config;
    public bool AutoRenameEnabled = true;
    public bool RenameFirstVessel = false;
    public string SuffixFormat = " {n}";
    public NumberingStyle NumberStyle = NumberingStyle.ArabicNumbers;
    public PresetConfig NamePresets;

    /// Loads the configuration from disk.
    ///
    /// This method loads the configuration from disk and
    /// updates the values of the AutoRenameEnabled, SuffixFormat,
    /// RenameFirstVessel, and NumberStyle properties.
    public void Load()
    {
        config = PluginConfiguration.CreateForType<AutoRenamerConfig>();
        config.load();

        AutoRenameEnabled = config.GetValue(nameof(AutoRenameEnabled), true);
        /// The format of the suffix to use for vessel names.
        /// For example, " {n}" will result in vessel names like "MyVessel 1", "MyVessel 2", etc.
        SuffixFormat = config.GetValue(nameof(SuffixFormat), " {n}");
        /// If true, the first vessel will also be renamed.
        /// Otherwise, the first vessel will keep its original name.
        RenameFirstVessel = config.GetValue(nameof(RenameFirstVessel), false);
        /// The numbering style to use for vessel names.
        /// For example, NumberingStyle.ArabicNumbers will result in vessel names like "MyVessel 1", "MyVessel 2", etc.
        NumberStyle = (NumberingStyle)config.GetValue(nameof(NumberStyle), (int)NumberingStyle.ArabicNumbers);
        /* /// The custom & template presets for vessel names.
        /// These presets are saved to disk and loaded when the game is started.
        NamePresets = PresetConfigSerializer.LoadPresetConfig();
        // Print in log NamePresets
        UnityEngine.Debug.Log($"NamePresets: CustomPresets={NamePresets.CustomPresets.Count}, TemplatePresets={NamePresets.TemplatePresets.Count}");

        // Ensure lists are not null
        if (NamePresets.CustomPresets == null)
            NamePresets.CustomPresets = new List<Preset>();

        if (NamePresets.TemplatePresets == null)
            NamePresets.TemplatePresets = new List<Preset>(); */
    }

    /// Saves the config to disk.
    ///
    /// This method saves the current values of the AutoRenameEnabled,
    /// SuffixFormat, RenameFirstVessel, and NumberStyle properties
    /// to disk.
    public void Save()
    {
        // Save the settings to the config
        config.SetValue(nameof(AutoRenameEnabled), AutoRenameEnabled);
        config.SetValue(nameof(SuffixFormat), SuffixFormat);
        config.SetValue(nameof(RenameFirstVessel), RenameFirstVessel);
        config.SetValue(nameof(NumberStyle), (int)NumberStyle);

        // Save the presets to an XML file
        /// The presets are saved to a file named "presets.xml"
        /// in the same directory as the configuration file.
        PresetConfigSerializer.SavePresetConfig(NamePresets);

        // Save the config to disk
        config.save();
    }

    /// Resets the configuration settings to their default values.
    /// 
    /// This method restores the default values for auto-renaming,
    /// suffix format, and numbering style.
    public void Reset()
    {
        // Enable auto-rename by default
        AutoRenameEnabled = true;    

        // Do not rename the first vessel by default
        RenameFirstVessel = false;    

        // Set the default suffix format
        SuffixFormat = " {n}";    

        // Use Arabic numbers as the default numbering style
        NumberStyle = NumberingStyle.ArabicNumbers;
    }


    [XmlRoot("presetsList")]
    // Represents a collection of presets
    public class PresetConfig
    {
        [XmlArray("CustomPresets")]
        [XmlArrayItem("Preset")]
        public List<Preset> CustomPresets { get; set; } = new List<AutoRenamerConfig.Preset>();

        [XmlArray("TemplatePresets")]
        [XmlArrayItem("Preset")]
        public List<Preset> TemplatePresets { get; set; } = new List<AutoRenamerConfig.Preset>();
    }

    public class PresetConfigSerializer
    {
        /// Saves a PresetConfig to an XML file.
        /// 
        /// This method takes a PresetConfig object and saves it to an XML file
        /// at the provided file path.
        /// 
        /// <param name="config">The PresetConfig to save.</param>
        /// <param name="filePath">The path to the XML file where the PresetConfig should be saved.</param>
        public static void SavePresetConfig(PresetConfig config)
        {
            XmlSerializer serializer = new XmlSerializer(typeof(PresetConfig));
            using (StreamWriter writer = new StreamWriter(presetsPath))
            {
                serializer.Serialize(writer, config);
            }
        }

        /// Loads a PresetConfig from an XML file.
        /// 
        /// This method takes the path to an XML file and loads the PresetConfig
        /// from it. If the file does not exist or the XML is invalid, an
        /// exception is thrown.
        /// 
        /// <param name="filePath">The path to the XML file to load.</param>
        /// <returns>The loaded PresetConfig.</returns>
        public static PresetConfig LoadPresetConfig()
        {
            XmlSerializer serializer = new XmlSerializer(typeof(PresetConfig));
            using (StreamReader reader = new StreamReader(presetsPath))
            {
                return (PresetConfig)serializer.Deserialize(reader);
            }
        }

        /// Removes a custom preset from the preset list.
        ///
        /// This method takes a preset name and file path as arguments.
        /// It loads the preset config from the file path, removes the
        /// preset with the given name from the list of custom presets,
        /// and saves the updated preset config back to the file path.
        ///
        /// <param name="presetName">The name of the preset to remove.</param>
        /// <param name="filePath">The path to the XML file containing the preset config.</param>
        public static void RemovePresetConfig(string presetName)
        {
            // Load the preset config from the file path
            var presets = LoadPresetConfig();

            // Find the preset to remove
            var presetToRemove = presets.CustomPresets.FirstOrDefault(p => p.DisplayName == presetName);
            UnityEngine.Debug.Log($"[AutoRenamer] Removing preset: {presetName}");

            // If the preset exists, remove it from the list of custom presets
            if (presetToRemove != null)
            {
                presets.CustomPresets.Remove(presetToRemove);
                // Save the updated preset config back to the file path
                SavePresetConfig(presets); // Save the updated presets();
            }
        }

    }
}

public static class NumberingStyleExtensions
{
    public static string ToDisplayString(this AutoRenamerConfig.NumberingStyle style)
    {
        if (style == AutoRenamerConfig.NumberingStyle.ArabicNumbers)
            return "AN";
        if (style == AutoRenamerConfig.NumberingStyle.ZeroPaddedNumbers)
            return "ZPN";
        if (style == AutoRenamerConfig.NumberingStyle.RomanNumerals)
            return "RN";
        if (style == AutoRenamerConfig.NumberingStyle.LatinLetters)
            return "LL";
        if (style == AutoRenamerConfig.NumberingStyle.GreekLetters)
            return "GL";
        if (style == AutoRenamerConfig.NumberingStyle.GreekWords)
            return "GW";
        if (style == AutoRenamerConfig.NumberingStyle.NATOPhonetic)
            return "NP";
        if (style == AutoRenamerConfig.NumberingStyle.RussianLetters)
            return "RL";
        if (style == AutoRenamerConfig.NumberingStyle.Date)
            return "Date";
        if (style == AutoRenamerConfig.NumberingStyle.PartsCount)
            return "PC";

        return style.ToString(); // Fallback
    }
}
