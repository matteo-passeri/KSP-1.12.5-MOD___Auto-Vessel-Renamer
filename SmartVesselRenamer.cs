// SmartVesselRenamer.cs
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using KSP.IO;
using KSP.UI.Screens;
using UnityEngine.UI;
using System.Reflection;
using System.IO;
using System;
using System.Text;

[KSPAddon(KSPAddon.Startup.EditorAny, false)]
public class SmartVesselRenamer : MonoBehaviour
{
    private bool launchHooked = false; // Tracks whether the launch button has been hooked already
    private ApplicationLauncherButton appButton; // Toolbar button for toggling GUI
    private Rect windowRect = new Rect(300, 100, 260, 370); // Main window position and size
    private Rect confirmWindowRect = new Rect(300, 480, 250, 120); // Confirmation dialog window
    private bool showGui = false; // Controls GUI visibility
    private bool showConfirm = false; // Controls confirmation window visibility
    private static Texture2D fallbackIconTexture; // Static fallback icon texture
    private Texture2D iconTexture; // Loaded icon texture
    private AutoRenamerConfig config; // Stores configuration values
    private string namePreview = ""; // Stores the preview of the new vessel name
    private bool showDropdown = false;     
    List<string> existingNames = new List<string>();
    private GUIStyle dropdownBgStyle;

    private void Start()
    {
        config = new AutoRenamerConfig();
        config.Load();
        StartCoroutine(RenameVesselViaLaunch()); // Hook launch button coroutine

        iconTexture = LoadEmbeddedIcon();

        // Create toolbar button
        if (ApplicationLauncher.Instance != null)
        {
            appButton = ApplicationLauncher.Instance.AddModApplication(
                ToggleGui, ToggleGui, null, null, null, null,
                ApplicationLauncher.AppScenes.VAB | ApplicationLauncher.AppScenes.SPH,
                iconTexture);
        }

        // Initialize the dropdown background style
        dropdownBgStyle = new GUIStyle();
        dropdownBgStyle.normal.background = CreateSolidColorTexture(new Color(0.15f, 0.15f, 0.15f)); // Dark grey
    }

    private Texture2D CreateSolidColorTexture(Color color)
    {
        Texture2D tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, color);
        tex.Apply();
        return tex;
    }

    // Fallback icon creation if texture is missing
    private Texture2D GetFallbackIcon()
    {
        if (fallbackIconTexture == null)
        {
            fallbackIconTexture = new Texture2D(38, 38);
            for (int y = 0; y < fallbackIconTexture.height; y++)
                for (int x = 0; x < fallbackIconTexture.width; x++)
                    fallbackIconTexture.SetPixel(x, y, Color.cyan);
            fallbackIconTexture.Apply();
        }
        return fallbackIconTexture;
    }

    // Load embedded icon from the mod's resources or GameDatabase
    private Texture2D LoadEmbeddedIcon()
    {
        // 1. First try GameDatabase
        try
        {
            Texture2D tex = GameDatabase.Instance.GetTexture("SmartVesselRenamer/Textures/SmartVesselRenamer", false);
            if (tex != null) return tex;
        }
        catch { /* Ignore */ }

        // 2. Fallback to embedded resource
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("SmartVesselRenamer.png"));
            
            if (resourceName != null)
            {
                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                {
                    byte[] imageData = new byte[stream.Length];
                    stream.Read(imageData, 0, (int)stream.Length);
                    
                    Texture2D tex = new Texture2D(64, 64, TextureFormat.ARGB32, false);
                    tex.LoadRawTextureData(imageData);
                    tex.Apply();
                    return tex;
                }
            }
        }
        catch { /* Ignore */ }

        // 3. Ultimate fallback
        return GetFallbackIcon();
    }

    private void OnDestroy()
    {
        config.Save();
        if (appButton != null)
        {
            ApplicationLauncher.Instance.RemoveModApplication(appButton);
        }
    }

    // Toggle GUI display state
    private void ToggleGui()
    {
        showGui = !showGui;
        if (showGui && EditorLogic.fetch?.ship != null)
        {
            namePreview = GetUniqueName(EditorLogic.fetch.ship.shipName);
        }
    }

    private void OnGUI()
    {
        if (showGui)
        {
            // Define dropdown area (adjust coordinates to match your layout)
            Rect dropdownButtonRect = new Rect(windowRect.x + 10, windowRect.y + 110, windowRect.width - 20, 20);
            Rect dropdownMenuRect = new Rect(windowRect.x + 10, windowRect.y + 130, windowRect.width - 20, 160);

            // Close dropdown if clicking outside both the button and menu
            if (Event.current.type == EventType.MouseDown && 
                !dropdownButtonRect.Contains(Event.current.mousePosition) && 
                !dropdownMenuRect.Contains(Event.current.mousePosition))
            {
                showDropdown = false;
            }

            windowRect = GUI.Window(GetInstanceID(), windowRect, DrawGuiWindow, "Auto-Rename Settings");
        }

        if (showConfirm)
        {
            confirmWindowRect = GUI.Window(GetInstanceID() + 1, confirmWindowRect, DrawConfirmWindow, "Confirm Rename");
        }
    }

    // Main GUI window content
    private void DrawGuiWindow(int windowID)
    {
        float widthButton = 220;
        float heightButton = 30;
        float heightField = 20;
        float groupPadding = 10;

        GUI.BeginGroup(new Rect(10, 20, widthButton + (groupPadding * 2), windowRect.height - 40));

        config.AutoRenameEnabled = GUI.Toggle(new Rect(10, 10, widthButton, heightButton), config.AutoRenameEnabled, "Enable Auto-Rename");

        GUI.Label(new Rect(10, 40, widthButton, heightButton), "Suffix Format:");
        config.SuffixFormat = GUI.TextField(new Rect(10, 60, widthButton, heightField), config.SuffixFormat);

        if (!config.SuffixFormat.Contains("{n}"))
            GUI.Label(new Rect(10, 90, widthButton, heightField), "[!] Format must include {n}");

        GUI.Label(new Rect(10, 110, widthButton, heightButton), "Numbering Style:");

        if (GUI.Button(new Rect(10, 130, widthButton, heightButton), config.NumberStyle.ToString()))
        {
            showDropdown = !showDropdown;
        }

        float dropdownHeight = heightField * 8;
        float dropdownY = 140 + heightField;

        // Draw dropdown if enabled
        if (showDropdown)
        {
            // Draw dropdown background and options
            GUI.depth = 0; // Bring dropdown to front

            GUI.Box(new Rect(10, dropdownY, widthButton, dropdownHeight), ""); // Base background box
            GUI.backgroundColor = Color.grey;
            string[] styleNames = Enum.GetNames(typeof(AutoRenamerConfig.NumberingStyle));

            for (int i = 0; i < styleNames.Length; i++)
            {
                if (GUI.Button(new Rect(10, dropdownY + (i * heightField), widthButton, heightField), styleNames[i]))
                {
                    config.NumberStyle = (AutoRenamerConfig.NumberingStyle)i;
                    showDropdown = false;
                }
            }

            GUI.backgroundColor = Color.white;

            // Skip drawing anything *under* the dropdown area
            GUI.EndGroup();
            GUI.DragWindow();
            return;
        }

        config.RenameFirstVessel = GUI.Toggle(new Rect(10, 160, widthButton, heightButton),
                                        config.RenameFirstVessel,
                                        "Rename first vessel (include #1)");

        if (GUI.Button(new Rect(10, 210, widthButton, heightButton), "Rename Now"))
        {
            string oldName = EditorLogic.fetch.ship.shipName;
            namePreview = GetUniqueName(oldName);
            if (oldName != namePreview)
            {
                showConfirm = true;
            }
            else
            {
                ScreenMessages.PostScreenMessage("Name already unique", 3f, ScreenMessageStyle.UPPER_CENTER);
            }
        }

        if (GUI.Button(new Rect(10, 250, widthButton, heightButton), "Reset to Defaults"))
        {
            config.Reset();
        }

        if (GUI.Button(new Rect(10, 290, widthButton, heightButton), "Close"))
        {
            showGui = false;
        }

        GUI.EndGroup();
        GUI.DragWindow();
    }


    // Confirmation popup window
    private void DrawConfirmWindow(int windowID)
    {
        GUI.BeginGroup(new Rect(10, 20, confirmWindowRect.width - 20, confirmWindowRect.height - 40));

        GUI.Label(new Rect(10, 10, confirmWindowRect.width - 20, 20), $"New Name: {namePreview}");

        if (GUI.Button(new Rect(10, 40, 100, 30), "Confirm"))
        {
            RenameVessel();
            showConfirm = false;
        }

        if (GUI.Button(new Rect(130, 40, 100, 30), "Cancel"))
        {
            showConfirm = false;
        }

        GUI.EndGroup();
        GUI.DragWindow();
    }

    // Coroutine to hook into the launch button click
    private IEnumerator RenameVesselViaLaunch()
    {
        while (EditorLogic.fetch == null || EditorLogic.fetch.launchBtn == null)
            yield return null;

        if (!launchHooked)
        {
            launchHooked = true;
            var originalListener = EditorLogic.fetch.launchBtn.onClick;
            EditorLogic.fetch.launchBtn.onClick = new Button.ButtonClickedEvent();

            EditorLogic.fetch.launchBtn.onClick.AddListener(() =>
            {
                try
                {
                    if (config.AutoRenameEnabled)
                        RenameVessel(originalListener);
                    else
                        originalListener.Invoke();
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[AutoRenamer] Exception during rename/launch: {ex}");
                    originalListener.Invoke();
                }
            });
        }
    }

    // Handles vessel renaming logic
    private void RenameVessel(Button.ButtonClickedEvent originalListener = null)
    {
        if (EditorLogic.fetch?.ship == null || EditorLogic.fetch.shipNameField == null || string.IsNullOrEmpty(EditorLogic.fetch.shipNameField.text))
            return;

        string currentName = EditorLogic.fetch.ship.shipName;
        string newName = GetUniqueName(currentName);

        if (!string.Equals(currentName, newName, StringComparison.OrdinalIgnoreCase))
        {
            EditorLogic.fetch.shipNameField.text = newName;
            EditorLogic.fetch.ship.shipName = newName;
            GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
            ScreenMessages.PostScreenMessage($"Renamed to \"{newName}\"", 4f, ScreenMessageStyle.UPPER_CENTER);
        }
        else
        {
            ScreenMessages.PostScreenMessage("Name already unique", 3f, ScreenMessageStyle.UPPER_CENTER);
        }
        
        if (originalListener != null)
            StartCoroutine(DelayedLaunch(originalListener));
    }

    // Delays actual launch to allow rename first
    private IEnumerator DelayedLaunch(Button.ButtonClickedEvent listener)
    {
        yield return null;
        listener.Invoke();
    }

    // Generates a unique vessel name
    private string GetUniqueName(string baseName)
    {
        if (string.IsNullOrWhiteSpace(baseName)) baseName = "Untitled Craft";

        string cleanBase = baseName;
        int suffixStart = baseName.LastIndexOf(" #");
        if (suffixStart > 0 && int.TryParse(baseName.Substring(suffixStart + 2), out _))
            cleanBase = baseName.Substring(0, suffixStart);

        string candidate = cleanBase;
        int count = config.RenameFirstVessel ? 1 : 2;  // Start from 1 if enabled

        HashSet<VesselType> excludedTypes = new HashSet<VesselType>
        {
            VesselType.Debris,
            VesselType.Flag,
            VesselType.EVA,
            VesselType.SpaceObject,
            VesselType.Unknown
        };

        if (!existingNames.Any()) {
            // Collect current vessel names to compare
            if (FlightGlobals.Vessels != null)
                existingNames.AddRange(
                    FlightGlobals.Vessels.Where(v => v != null && !excludedTypes.Contains(v.vesselType) && !string.IsNullOrEmpty(v.vesselName))
                    .Select(v => v.vesselName.Trim()));

            if (HighLogic.CurrentGame?.flightState != null)
                existingNames.AddRange(
                    HighLogic.CurrentGame.flightState.protoVessels
                    .Where(p => p != null && !excludedTypes.Contains(p.vesselType) && !string.IsNullOrEmpty(p.vesselName))
                    .Select(p => p.vesselName.Trim()));
        }        

        // Build log for debug output
        StringBuilder logBuilder = new StringBuilder();
        logBuilder.AppendLine($"[AutoRenamer] Checking against {existingNames.Count} existing vessels:");

        // Loop until a unique name is found
        int attempt = 0;
        while (true)
        {
            bool nameExists = existingNames.Any(name => string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase));
            
            if (!nameExists && (attempt > 0 || !config.RenameFirstVessel))
                break;

            logBuilder.AppendLine($" - {candidate} exists or RenameFirstVessel forced, trying next");

            string numberStr = ConvertNumberToStyle(count, config.NumberStyle);
            candidate = $"{cleanBase}{config.SuffixFormat.Replace("{n}", numberStr)}";
            count++;
            attempt++;
        }


        Debug.Log(logBuilder.ToString());
        return candidate;
    }

    private string ConvertNumberToStyle(int number, AutoRenamerConfig.NumberingStyle style)
    {
        switch(style)
        {
            case AutoRenamerConfig.NumberingStyle.ArabicNumbers:
                return number.ToString();
                
            case AutoRenamerConfig.NumberingStyle.ZeroPaddedNumbers:
                return number.ToString("00");
                
            case AutoRenamerConfig.NumberingStyle.RomanNumerals:
                return ToRoman(number);
                
            case AutoRenamerConfig.NumberingStyle.LatinLetters:
                return ToLatinLetters(number);
                
            case AutoRenamerConfig.NumberingStyle.GreekLetters:
                return ToGreekLetters(number);
                
            case AutoRenamerConfig.NumberingStyle.GreekWords:
                return ToGreekWords(number);
                
            case AutoRenamerConfig.NumberingStyle.NATOPhonetic:
                return ToNATOPhonetic(number);
                
            case AutoRenamerConfig.NumberingStyle.RussianLetters:
                return ToRussianLetters(number);
                
            default:
                return number.ToString();
        }
    }

    // Helper methods for each numbering style
    private string ToRoman(int number)
    {
        if (number < 1 || number > 3999) return number.ToString();
        string[] roman = {"M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I"};
        int[] values = {1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1};
        string result = "";
        for (int i = 0; i < 13; i++)
        {
            while (number >= values[i])
            {
                number -= values[i];
                result += roman[i];
            }
        }
        return result;
    }

    private string ToLatinLetters(int number)
    {
        if (number < 1) return "A";
        string result = "";
        while (number > 0)
        {
            number--;
            result = (char)('A' + (number % 26)) + result;
            number /= 26;
        }
        return result;
    }

    private string ToGreekLetters(int number)
    {
        string[] greek = {
            "Α", "Β", "Γ", "Δ", "Ε", "Ζ", "Η", "Θ", "Ι", "Κ", "Λ", "Μ", 
            "Ν", "Ξ", "Ο", "Π", "Ρ", "Σ", "Τ", "Υ", "Φ", "Χ", "Ψ", "Ω"
        };
        return number <= greek.Length ? greek[number-1] : number.ToString();
    }

    private string ToGreekWords(int number)
    {
        string[] greekWords = {
            "Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta", "Eta", "Theta",
            "Iota", "Kappa", "Lambda", "Mu", "Nu", "Xi", "Omicron", "Pi", "Rho",
            "Sigma", "Tau", "Upsilon", "Phi", "Chi", "Psi", "Omega"
        };
        return number <= greekWords.Length ? greekWords[number-1] : number.ToString();
    }

    private string ToNATOPhonetic(int number)
    {
        string[] nato = {
            "Alpha", "Bravo", "Charlie", "Delta", "Echo", "Foxtrot", "Golf", "Hotel",
            "India", "Juliett", "Kilo", "Lima", "Mike", "November", "Oscar", "Papa",
            "Quebec", "Romeo", "Sierra", "Tango", "Uniform", "Victor", "Whiskey", "X-ray",
            "Yankee", "Zulu"
        };
        return number <= nato.Length ? nato[number-1] : number.ToString();
    }

    private string ToRussianLetters(int number)
    {
        string[] russian = {
            "А", "Б", "В", "Г", "Д", "Е", "Ж", "З", "И", "Й", "К", "Л", "М",
            "Н", "О", "П", "Р", "С", "Т", "У", "Ф", "Х", "Ц", "Ч", "Ш", "Щ",
            "Ъ", "Ы", "Ь", "Э", "Ю", "Я"
        };
        return number <= russian.Length ? russian[number-1] : number.ToString();
    }

}

// AutoRenamerConfig.cs
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
        RussianLetters
    }

    private const string CONFIG_FILE = "SmartVesselRenamer.cfg";
    private PluginConfiguration config;

    public bool AutoRenameEnabled = true;
    public bool RenameFirstVessel = false;
    public string SuffixFormat = " #{n}";
    public NumberingStyle NumberStyle = NumberingStyle.ArabicNumbers; // New field

    public void Load()
    {
        config = PluginConfiguration.CreateForType<AutoRenamerConfig>();
        config.load();

        AutoRenameEnabled = config.GetValue("autoRenameEnabled", true);
        SuffixFormat = config.GetValue("suffixFormat", " #{n}");
        RenameFirstVessel = config.GetValue("renameFirstVessel", false);
        NumberStyle = (NumberingStyle)config.GetValue("numberStyle", (int)NumberingStyle.ArabicNumbers);
    }

    public void Save()
    {
        config.SetValue("autoRenameEnabled", AutoRenameEnabled);
        config.SetValue("suffixFormat", SuffixFormat);
        config.SetValue("renameFirstVessel", RenameFirstVessel);
        config.SetValue("numberStyle", (int)NumberStyle);
        config.save();
    }

    public void Reset()
    {
        AutoRenameEnabled = true;
        SuffixFormat = " #{n}";
        RenameFirstVessel = false;
        NumberStyle = NumberingStyle.ArabicNumbers;
    }
}
