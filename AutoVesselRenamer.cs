// AutoVesselRenamer.cs
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
public class AutoVesselRenamer : MonoBehaviour
{
    private bool launchHooked = false; // Tracks whether the launch button has been hooked already
    private ApplicationLauncherButton appButton; // Toolbar button for toggling GUI
    private Rect windowRect = new Rect(300, 100, 270, 260); // Main window position and size
    private Rect confirmWindowRect = new Rect(320, 380, 250, 120); // Confirmation dialog window
    private bool showGui = false; // Controls GUI visibility
    private bool showConfirm = false; // Controls confirmation window visibility
    private static Texture2D fallbackIconTexture; // Static fallback icon texture
    private Texture2D iconTexture; // Loaded icon texture
    private AutoRenamerConfig config; // Stores configuration values
    private string namePreview = ""; // Stores the preview of the new vessel name
    
    List<string> existingNames = new List<string>();

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
            Texture2D tex = GameDatabase.Instance.GetTexture("AutoVesselRenamer/Textures/AutoVesselRenamer", false);
            if (tex != null) return tex;
        }
        catch { /* Ignore */ }

        // 2. Fallback to embedded resource
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("AutoVesselRenamer.png"));
            
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
        float widthButton = windowRect.width - 30;
        float heightButton = 30;
        float widthField = windowRect.width - 20;
        float heightField = 20;

        GUI.BeginGroup(new Rect(10, 20, widthField, windowRect.height - 40));

        config.AutoRenameEnabled = GUI.Toggle(new Rect(10, 10, widthField, heightField), config.AutoRenameEnabled, "Enable Auto-Rename");

        GUI.Label(new Rect(10, 40, widthField, heightField), "Suffix Format:");
        config.SuffixFormat = GUI.TextField(new Rect(10, 60, widthField, heightField), config.SuffixFormat);

        if (!config.SuffixFormat.Contains("{n}"))
            GUI.Label(new Rect(10, 90, widthField, heightField), "[!] Format must include {n}");

        if (GUI.Button(new Rect(10, 110, widthButton, heightButton), "Rename Now"))
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

        if (GUI.Button(new Rect(10, 150, widthButton, heightButton), "Reset to Defaults"))
        {
            config.Reset();
        }

        if (GUI.Button(new Rect(10, 190, widthButton, heightButton), "Close"))
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
        int count = 2;

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
        while (existingNames.Any(name => string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            logBuilder.AppendLine($" - {candidate} exists, trying next");
            candidate = $"{cleanBase}{config.SuffixFormat.Replace("{n}", count.ToString())}";
            count++;
        }

        Debug.Log(logBuilder.ToString());
        return candidate;
    }
}

// AutoRenamerConfig.cs
public class AutoRenamerConfig
{
    private const string CONFIG_FILE = "AutoVesselRenamer.cfg";
    private PluginConfiguration config;

    public bool AutoRenameEnabled = true;
    public string SuffixFormat = " #{n}";

    // Load saved config values
    public void Load()
    {
        config = PluginConfiguration.CreateForType<AutoRenamerConfig>();
        config.load();

        AutoRenameEnabled = config.GetValue("autoRenameEnabled", true);
        SuffixFormat = config.GetValue("suffixFormat", " #{n}");
    }

    // Save current config values
    public void Save()
    {
        config.SetValue("autoRenameEnabled", AutoRenameEnabled);
        config.SetValue("suffixFormat", SuffixFormat);
        config.save();
    }

    // Restore default config values
    public void Reset()
    {
        AutoRenameEnabled = true;
        SuffixFormat = " #{n}";
    }
}
