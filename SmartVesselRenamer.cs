using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using KSP.UI.Screens;
using UnityEngine.UI;
using System.Reflection;
using System.IO;
using System;
using static AutoRenamerConfig;
using UniLinq;

[KSPAddon(KSPAddon.Startup.EditorAny, false)]
public class SmartVesselRenamer : MonoBehaviour
{
    // Core Fields
    private bool launchHooked = false; // Tracks whether the launch button has been hooked already
    private ApplicationLauncherButton appButton; // Toolbar button for toggling GUI
    private Rect mainWindowRect = new Rect(300, 100, 260, 410); // Main window position and size
    private Rect presetEditormainWindowRect = new Rect(590, 100, 260, 410); // Preset editor window
    private Rect confirmRenameWindowRect = new Rect(600, 100, 260, 120); // Confirmation dialog window
    private Rect confirmDeleteWindowRect = new Rect(600, 590, 260, 120); // Confirmation dialog window
    private bool showMainGui = false; // Controls GUI visibility
    private bool showPresetEditor = false; // Controls preset editor visibility
    private bool showConfirmRename = false; // Controls confirmation window visibility
    private bool showConfirmDelete = false; // Controls confirmation window visibility
    private static Texture2D fallbackIconTexture; // Static fallback icon texture
    private Texture2D iconTexture; // Loaded icon texture
    private AutoRenamerConfig config; // Stores configuration values
    private bool showDropdown = false; // Controls dropdown visibility
    private List<string> existingNames = new List<string>(); // List of existing vessel names
    private GUIStyle dropdownBgStyle; // Dropdown background style
    private GUIStyle dropdownItemStyle; // Dropdown item style
    private bool stylesInitialized = false; // Flag to track style initialization
    float widthButton = 240; // Width of buttons
    float heightField = 20; // Height of input fields
    private string logName = "[SmartVesselRenamer]"; // Log name prefix

    // Performance Optimizations
    private EditorLogic editorLogicCache; // Cached EditorLogic instance
    private string previousShipName = ""; // Previous vessel name

    // Expanded Features
    private string newPresetName = ""; // Stores the name of the new preset
    private Vector2 customPresetScrollPos; // Scroll position for the preset list
    private Vector2 templateScrollPos; // Scroll position for the template list
    private Vector2 numberingScrollPos; // Scroll position for the numbering list
    private Vector2 allPresetsScrollPos; // Scroll position for the template list
    private int partCount; // Stores the number of parts in the vessel
    private bool showPresets = false; // Controls presets visibility
    private List<Preset> allPresets; // Stores all presets
    private List<Preset> templatePresets; // Stores template presets
    private List<Preset> customPresets; // Stores custom presets
    private int removePresetIndex; // Index of the preset to remove

    // Preview System
    private string cachedPreview = ""; // Cached preview of the new vessel name
    private float lastPreviewUpdateTime = 0f; // Last time the preview was updated
    private const float PREVIEW_UPDATE_DELAY = 0.5f; // Delay before updating the preview



    /// Initializes the SmartVesselRenamer component.
    private void Start()
    {
        // Create an instance of AutoRenamerConfig
        config = new AutoRenamerConfig();

        // Check if the configuration is loaded
        if (config == null)
        {
            Debug.LogError($"{logName} Config is null, cannot proceed.");
            return;
        }

        // Load the configuration settings
        config.Load();
        if (config == null)
        {
            Debug.LogError($"{logName} Config is null after loading, cannot proceed.");
            return;
        }

        // Start courutine for RenameVesselViaLaunch
        try
        {
            StartCoroutine(RenameVesselViaLaunch());
        }
        catch (Exception ex)
        {
            Debug.LogError($"{logName} Error starting RenameVesselViaLaunch coroutine: {ex}");   
        }

        // Load name presets from configuration
        try
        {
            LoadPresetsFromConfig();
        }
        catch (Exception ex)
        {
            Debug.LogError($"{logName} Error loading presets from config: {ex}");
        }

        // Load the embedded icon texture
        try
        {
            iconTexture = LoadEmbeddedIcon();
        }
        catch (Exception ex)
        {
            Debug.LogError($"{logName} Error loading icon texture: {ex}");
        }

        // Create the toolbar button for toggling the GUI
        if (ApplicationLauncher.Instance != null)
        {
            try
            {
                appButton = ApplicationLauncher.Instance.AddModApplication(
                    ToggleGui, ToggleGui, null, null, null, null,
                    ApplicationLauncher.AppScenes.VAB | ApplicationLauncher.AppScenes.SPH,
                    iconTexture);
            }
            catch (Exception ex)
            {
                Debug.LogError($"{logName} Error creating application launcher button: {ex}");
            }
        }

        // Initialize the style for the dropdown background
        try
        {
            dropdownBgStyle = new GUIStyle();
            dropdownBgStyle.normal.background = CreateSolidColorTexture(new Color(0.15f, 0.15f, 0.15f)); // Dark grey
        }
        catch (Exception ex)
        {
            Debug.LogError($"{logName} Error initializing dropdown background style: {ex}");
        }
    }

    #region Initialization
    /// Initializes the GUI styles used for dropdown backgrounds and items.
    private void InitializeStyles()
    {
        try
        {
            // Ensure the GUI skin is available
            if (GUI.skin == null)
            {
                Debug.LogError($"{logName} GUI.skin is null!");
                return;
            }

            // Initialize the style for the dropdown background
            dropdownBgStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = CreateSolidColorTexture(new Color(0.2f, 0.2f, 0.2f)) } // Dark grey background
            };

            // Ensure the button skin is available
            if (GUI.skin.button == null)
            {
                Debug.LogError($"{logName} GUI.skin.button is null!");
                return;
            }

            // Initialize the style for dropdown items
            dropdownItemStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleLeft, // Align text to the left
                normal = { textColor = Color.white }, // Normal text color
                hover = { textColor = Color.yellow }, // Hover text color
                active = { textColor = Color.yellow } // Active text color
            };
        }
        catch (Exception e)
        {
            Debug.LogError($"{logName} Style init failed: {e}");
        }
    }

    /// Creates a 1x1 texture with a solid color.
    /// 
    /// <param name="color">The color to fill the texture with.</param>
    /// <returns>A Texture2D object of size 1x1 filled with the given color.</returns>
    private Texture2D CreateSolidColorTexture(Color color)
    {
        // Create a new 1x1 texture
        Texture2D tex = new Texture2D(1, 1);

        // Set the pixel color to the specified color
        tex.SetPixel(0, 0, color);

        // Apply the changes to the texture
        tex.Apply();

        // Return the created texture
        return tex;
    }
    #endregion

    #region Core Functionality
    /// Caches references to the editor logic and the ship name field
    /// to avoid repeated lookups.
    private void CacheReferences()
    {
        // Cache the editor logic to avoid repeated lookups
        if (editorLogicCache == null)
            editorLogicCache = EditorLogic.fetch;
    }

    /// Updates the cached part count.
    ///
    /// This method is called whenever the GUI window is drawn.
    /// It updates the cached part count to the current number of parts
    /// in the editor logic's ship.
    private void UpdatePartCount()
    {
        // Get the current part count from the editor logic's ship
        // and store it in the cached part count variable.
        // If the editor logic or its ship are null, the part count
        // will be 0.
        partCount = editorLogicCache?.ship?.Parts?.Count ?? 0;
    }

    /// A coroutine that hooks into the launch button click event.
    /// 
    /// This coroutine is used to hook into the launch button click event
    /// and rename the vessel before launching it if the auto-rename config
    /// option is enabled.
    private IEnumerator RenameVesselViaLaunch()
    {
        // Wait for the launch button to be available
        while (EditorLogic.fetch == null || EditorLogic.fetch.launchBtn == null)
            yield return null;

        if (!launchHooked)
        {
            // Mark the launch button as hooked
            launchHooked = true;

            // Store the original click listener
            var originalListener = EditorLogic.fetch.launchBtn.onClick;

            // Replace the original click listener with a new one
            EditorLogic.fetch.launchBtn.onClick = new Button.ButtonClickedEvent();

            // Add a new listener to the launch button
            EditorLogic.fetch.launchBtn.onClick.AddListener(() =>
            {
                try
                {
                    // Check if the auto-rename config option is enabled
                    if (config.AutoRenameEnabled)
                    {
                        // Rename the vessel before launching it
                        RenameVessel(originalListener);
                    }
                    else
                    {
                        // Call the original click listener
                        originalListener.Invoke();
                    }
                }
                catch (Exception ex)
                {
                    // Log any errors that occur during the renaming process
                    Debug.LogError($"{logName} Launch error: {ex}");
                    // Call the original click listener
                    originalListener.Invoke();
                }
            });
        }
    }

    /// Renames the vessel in the editor to a unique name, based on the current name.
    /// If the current name is already unique, it will not be changed.
    /// 
    /// <param name="originalListener">Optional: the original click listener to invoke after renaming the vessel.</param>
    private void RenameVessel(Button.ButtonClickedEvent originalListener = null)
    {
        // Cache the editor logic and ship name field
        CacheReferences();
        if (editorLogicCache?.ship == null || editorLogicCache.ship.shipName == null)
            return;

        // Get the current name of the vessel
        string currentName = editorLogicCache.ship.shipName;

        // Generate a unique name based on the current name
        string newName = GetUniqueName(currentName);

        // Check if the new name is different from the current name
        if (!string.Equals(currentName, newName, StringComparison.OrdinalIgnoreCase) && editorLogicCache.ship.shipName != null)
        {
            // Rename the vessel
            EditorLogic.fetch.ship.shipName = newName;
            EditorLogic.fetch.shipNameField.text = newName;
            GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);

            // Show a message to the user
            ScreenMessages.PostScreenMessage($"Renamed to \"{newName}\"", 4f, ScreenMessageStyle.UPPER_CENTER);
        }
        else
        {
            // Show a message to the user
            ScreenMessages.PostScreenMessage("Name already unique", 3f, ScreenMessageStyle.UPPER_CENTER);
        }
        
        // If the original listener is provided, invoke it after renaming the vessel
        if (originalListener != null)
            StartCoroutine(DelayedLaunch(originalListener));
    }

    /// Waits for the next frame and then invokes the provided listener.
    /// This is used to delay the launch of the vessel until after the renaming process is complete.
    /// 
    /// <param name="listener">The listener to invoke after the renaming process is complete.</param>
    private IEnumerator DelayedLaunch(Button.ButtonClickedEvent listener)
    {
        // Wait for the next frame to ensure that the renaming process is complete
        yield return null;

        // Invoke the original listener
        listener.Invoke();
    }
    #endregion

    #region Naming Logic
    /// Checks if the ship name has changed and updates the name preview accordingly.
    ///
    /// This method is called whenever the vessel name is changed.
    /// It checks if the ship name has changed by comparing it to the previous ship name.
    /// If it has changed, the name preview is updated.
    private void CheckShipNameChange()
    {
        // Check if the game is not in the editor scene
        if (!HighLogic.LoadedSceneIsEditor)
            return;

        // Check if the editor logic or its ship are null
        if (EditorLogic.fetch == null || EditorLogic.fetch.ship == null || EditorLogic.fetch.ship.shipName == null)
        {
            Debug.LogWarning($"{logName} EditorLogic.fetch or its ship is null");
            return;
        }

        // Get the current name of the vessel
        string currentName = EditorLogic.fetch.ship.shipName;

        // Check if the current name is null or empty
        if (string.IsNullOrEmpty(currentName))
            return;

        // Check if the current name is different from the previous ship name
        if (currentName != previousShipName)
        {
            // Update the previous ship name
            previousShipName = currentName;

            // Update the name preview
            UpdateNamePreview();
        }
    }

    /// Generates a unique vessel name based on the provided base name.
    /// If the base name is null or whitespace, a default name is used.
    /// 
    /// <param name="baseName">The base name of the vessel.</param>
    /// <returns>A unique vessel name.</returns>
    private string GetUniqueName(string baseName)
    {
        // Use a default name if the provided base name is null or whitespace
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "Untitled Craft";

        // Generate a unique name using the base name
        return GenerateUniqueName(baseName);
    }

    /// Generates a unique vessel name based on a given base name.
    /// Replaces template variables within the base name and appends a unique suffix if necessary.
    /// 
    /// <param name="cleanBase">The base name of the vessel without any suffixes.</param>
    /// <returns>A unique vessel name.</returns>
    private string GenerateUniqueName(string cleanBase)
    {
        // Replace template variables in the base name
        cleanBase = cleanBase.Replace("{pc}", partCount.ToString()) // Replace part count
                             .Replace("{date}", DateTime.Now.ToString("yyyyMMdd")); // Replace current date

        // Load the existing vessel names
        LoadExistingNames();
        string candidate = cleanBase;
        int count = config.RenameFirstVessel ? 1 : 2; // Start counting from 1 or 2 based on configuration

        // Attempt to find a unique name within 100 tries
        for (int attempt = 0; attempt < 100; attempt++)
        {
            // Check if the candidate name is unique
            if (!existingNames.Contains(candidate, StringComparer.OrdinalIgnoreCase) &&
                (attempt > 0 || !config.RenameFirstVessel))
                break;

            // Generate the suffix using the configured numbering style
            string numberStr = ConvertNumberToStyle(count, config.NumberStyle);
            candidate = $"{cleanBase}{config.SuffixFormat.Replace("{n}", numberStr)}"; // Append suffix to base
            count++; // Increment counter for the next attempt
        }

        return candidate; // Return the unique vessel name
    }

    /// Loads all existing vessel names from both the active vessels and the proto vessel list.
    /// The names are trimmed and added to the existingNames list.
    private void LoadExistingNames()
    {
        existingNames.Clear();

        // Get the list of active vessels
        var vessels = FlightGlobals.Vessels ?? new List<Vessel>();

        // Get the list of proto vessels
        var protoVessels = HighLogic.CurrentGame?.flightState?.protoVessels ?? new List<ProtoVessel>();

        // Add the vessel names from the active vessels to the existingNames list
        existingNames.AddRange(
            vessels.Where(v => v != null && !IsExcludedVesselType(v.vesselType))
                .Select(v => v.vesselName.Trim())
        );

        // Add the vessel names from the proto vessels to the existingNames list
        existingNames.AddRange(
            protoVessels.Where(p => p != null && !IsExcludedVesselType(p.vesselType))
                .Select(p => p.vesselName.Trim())
        );
    }

    /// Loads a name preset and sets the corresponding numbering style.
    /// Replaces the current suffix format with "{n}" and saves the configuration.
    /// 
    /// <param name="presetName">The name of the preset to load.</param>
    /// <param name="numberingStyle">The numbering style to use for the preset.
    /// Defaults to <see cref="NumberingStyle.ArabicNumbers"/>.</param>
    private void LoadPresets(Preset presetName)
    {
        // Update the suffix format to "{n}" and the numbering style
        config.SuffixFormat = presetName.Suffix;
        config.NumberStyle = presetName.NumberingStyle;

        // Save the updated configuration
        config.Save();

        EditorLogic.fetch.ship.shipName = presetName.Name; // Needed for the editor
        EditorLogic.fetch.shipNameField.text = presetName.Name;

        // Simulate clicking and unfocusing to "refresh" the GUI
        GUI.FocusControl(null);
        GUIUtility.keyboardControl = 0;

        // Update preset editor field
        newPresetName = presetName.Name;        

        // Update Preview
        UpdateNamePreview();
    }

    /// Determines if the given vessel type is excluded from naming considerations.
    /// 
    /// <param name="type">The type of the vessel to check.</param>
    /// <returns>True if the vessel type is excluded; otherwise, false.</returns>
    private bool IsExcludedVesselType(VesselType type)
    {
        // Exclude vessel types that are not considered for naming
        return type == VesselType.Debris ||  // Exclude debris
               type == VesselType.Flag ||    // Exclude flags
               type == VesselType.EVA ||     // Exclude extravehicular activity (EVA)
               type == VesselType.SpaceObject || // Exclude unidentified space objects
               type == VesselType.Unknown;   // Exclude unknown vessel types
    }
    #endregion

    #region UI System
    /// Handles rendering of the GUI window and its contents.
    private void OnGUI()
    {        
        // Skip GUI drawing during scene transitions
        if (HighLogic.LoadedScene != GameScenes.EDITOR)
            return;

        // If the GUI window is visible, draw it
        if (showMainGui)
        {
            // Cache references to the editor logic and ship name field
            CacheReferences();

            // Update the part count
            UpdatePartCount();

            // Initialize styles if necessary
            if (!stylesInitialized)
            {
                InitializeStyles();
                stylesInitialized = true;
            }

            // Handle clicks outside the dropdown menu
            if (Event.current.type == EventType.MouseDown)
            {
                // Get the bounds of the dropdown button and menu
                Rect dropdownButtonRect = new Rect(mainWindowRect.x + 10, mainWindowRect.y + 130, mainWindowRect.width - 20, 20);
                Rect dropdownMenuRect = new Rect(mainWindowRect.x + 10, mainWindowRect.y + 150, mainWindowRect.width - 20, 160);

                // If the click is outside both the button and the menu, hide the menu
                if (!dropdownButtonRect.Contains(Event.current.mousePosition) &&
                    !dropdownMenuRect.Contains(Event.current.mousePosition))
                {
                    showDropdown = false;
                    showPresets = false;
                }
            }

            // Draw the GUI window
            mainWindowRect = GUI.Window(GetInstanceID(), mainWindowRect, DrawGuiWindow, "Smart Vessel Renamer", GUI.skin.window);
        }

        // If the confirm window is visible, draw it
        if (showConfirmRename)
        {
            // Set the position of the confirm window
            confirmRenameWindowRect.x = mainWindowRect.x;
            confirmRenameWindowRect.y = mainWindowRect.y + mainWindowRect.height + 10;

            // Draw the confirm window
            confirmRenameWindowRect = GUI.Window(GetInstanceID() + 1, confirmRenameWindowRect, DrawConfirmRenameWindow, "Confirm Rename");
        }

        // If the preset editor window is visible, draw it
        if (showPresetEditor)
        {
            // Draw the preset editor window
            presetEditormainWindowRect = GUI.Window(GetInstanceID() + 2, presetEditormainWindowRect, DrawPresetEditorWindow, "Preset Manager");
        }

        // If the confirm window for preset is visible, draw it
        if (showConfirmDelete)
        {
            // Set the position of the confirm window
            confirmDeleteWindowRect.x = presetEditormainWindowRect.x;
            confirmDeleteWindowRect.y = presetEditormainWindowRect.y + presetEditormainWindowRect.height + 10;

            // Draw the preset editor window
            confirmDeleteWindowRect = GUI.Window(GetInstanceID() + 3, confirmDeleteWindowRect, DrawConfirmRemovePresetWindow, "Confirm Remove");
        }
        
        // Check if ship name has change
        CheckShipNameChange();
    }

    /// Handles rendering of the GUI window and its contents.
    /// 
    /// <param name="windowID">The ID of the window to render.</param>
    private void DrawGuiWindow(int windowID)
    {
        if (!HighLogic.LoadedSceneIsEditor || EditorLogic.fetch == null || EditorLogic.fetch.ship == null)
            return;

        // Add close button in top-right corner
        const float closeButtonSize = 20f;
        const float closeButtonPadding = 5f;
        
        Rect closeButtonRect = new Rect(
            mainWindowRect.width - closeButtonSize - closeButtonPadding,
            closeButtonPadding,
            closeButtonSize,
            closeButtonSize
        );

        // Close button
        if (GUI.Button(closeButtonRect, "×")) //  (looks like X)
        {
            showMainGui = false;
        }

        GUILayout.BeginVertical();

        // Auto-Rename Toggle
        bool autoRenameToggle = GUILayout.Toggle(config.AutoRenameEnabled, "Enable Auto-Rename");
        if (autoRenameToggle != config.AutoRenameEnabled)
        {
            config.AutoRenameEnabled = autoRenameToggle;
            config.Save();
            UpdateNamePreview();
        }

        // First Vessel Toggle
        bool renameFirst = GUILayout.Toggle(config.RenameFirstVessel, "Rename first vessel (include #1)");
        if (renameFirst != config.RenameFirstVessel)
        {
            config.RenameFirstVessel = renameFirst;
            config.Save();
            UpdateNamePreview();
        }

        // Suffix Format
        GUILayout.Label("Suffix Format:");
        string suffixFormat = GUILayout.TextField(config.SuffixFormat);
        if (suffixFormat != config.SuffixFormat)
        {
            config.SuffixFormat = suffixFormat;
            config.Save();
            UpdateNamePreview();
        }

        // Format Validation
        if (!config.SuffixFormat.Contains("{n}"))
            GUILayout.Label("[!] Format must include {n}");

        // Real-time Preview
        GUILayout.Label($"Preview: {cachedPreview}");

        GUILayout.Space(15);

        // Numbering Style Dropdown
        GUILayout.Label("Numbering Style:");
        if (GUILayout.Button(config.NumberStyle.ToString()))
        {
            showDropdown = !showDropdown;
            showPresets = false;
        }
        
        // Auto-position the dropdown below the last button
        Rect buttonRect = GUILayoutUtility.GetLastRect();

        if (showDropdown)
        {
            float dropdownHeight = heightField * 10;
            float dropdownWidth = widthButton;
            float totalHeight = Enum.GetNames(typeof(NumberingStyle)).Length * heightField;

            // Calculate absolute screen position of dropdown
            Vector2 dropdownPos = new Vector2(buttonRect.x, buttonRect.yMax);
            
            GUI.BeginGroup(new Rect(dropdownPos.x, dropdownPos.y, dropdownWidth, dropdownHeight));
            GUI.Box(new Rect(0, 0, dropdownWidth, dropdownHeight), "", dropdownBgStyle);
            
            numberingScrollPos = GUI.BeginScrollView(
                new Rect(0, 0, dropdownWidth, dropdownHeight),
                numberingScrollPos,
                new Rect(0, 0, dropdownWidth - 20, totalHeight)
            );

            GUI.backgroundColor = Color.grey;
            
            int i = 0;
            foreach (string styleName in Enum.GetNames(typeof(NumberingStyle)))
            {
                Rect itemRect = new Rect(0, i * heightField, dropdownWidth, heightField);        

                if (itemRect.yMax > dropdownHeight) break; // Prevent drawing outside dropdown area

                if (GUI.Button(itemRect, styleName, dropdownItemStyle))
                {
                    config.NumberStyle = (NumberingStyle)Enum.Parse(typeof(NumberingStyle), styleName);
                    showDropdown = false;
                    config.Save();
                    
                    UpdateNamePreview();
                }                
                i++;
            }
            GUILayout.EndScrollView();

            GUI.EndGroup();
            GUI.backgroundColor = Color.white;

            // Skip drawing anything *under* the dropdown area
            GUI.DragWindow();
            return;
        }

        // Add spacing
        GUILayout.Space(25);

        GUILayout.Label("Presets:");
        // Presets Button
        if (GUILayout.Button("Load Preset"))
        {
            showPresets = !showPresets;
            showDropdown = false;
        }

        // Auto-position below the Load Preset button
        Rect presetButtonRect = GUILayoutUtility.GetLastRect();

        // Presets Dropdown
        if (showPresets)
        {
            float dropdownWidth = widthButton;
            float visibleHeight = heightField * 5;
            float totalHeight = allPresets.Count * heightField;

            Vector2 dropdownPos = new Vector2(presetButtonRect.x, presetButtonRect.yMax);
            
            // Outer dropdown container (visual box)
            GUI.BeginGroup(new Rect(dropdownPos.x, dropdownPos.y, dropdownWidth, visibleHeight));
            GUI.Box(new Rect(0, 0, dropdownWidth, visibleHeight), "", dropdownBgStyle);

            // Scrollable inner content
            allPresetsScrollPos = GUI.BeginScrollView(
                new Rect(0, 0, dropdownWidth, visibleHeight),
                allPresetsScrollPos,
                new Rect(0, 0, dropdownWidth - 20, totalHeight)
            );

            GUI.backgroundColor = Color.grey;
            
            for (int i = 0; i < allPresets.Count; i++)
            {
                Preset preset = allPresets[i];
                Rect itemRect = new Rect(0, i * heightField, dropdownWidth - 20, heightField);

                if (GUI.Button(itemRect, preset.DisplayName, dropdownItemStyle))
                {
                    LoadPresets(preset);
                    showPresets = false;
                }
            }

            GUI.backgroundColor = Color.white;

            GUILayout.EndScrollView();
            GUI.EndGroup();

            // Skip drawing anything *under* the dropdown area
            GUI.DragWindow();
            return;
        }

        // Preset Manager Button
        if (GUILayout.Button("Preset Manager"))
        {
            showPresetEditor = !showPresetEditor;
        }
        
        // Add spacing
        GUILayout.Space(40);

        // Rename Now Button
        if (GUILayout.Button("Rename Now"))
        {
            if (showConfirmRename == true) showConfirmRename = false;

            showConfirmRename = true;
        }

        // Reset Button
        if (GUILayout.Button("Reset to Defaults"))
        {
            config.Reset();
            config.Save();

            UpdateNamePreview();
        }

        GUILayout.EndVertical();

        // Allow window dragging
        GUI.DragWindow(new Rect(0, 0, mainWindowRect.width - closeButtonSize - closeButtonPadding, 20));
    }

    /// Draws the preset editor window, allowing the user to add, remove and edit their custom presets.
    /// 
    /// <param name="windowID">The ID of the window to draw.</param>
    private void DrawPresetEditorWindow(int windowID)
    {
        if (!HighLogic.LoadedSceneIsEditor || EditorLogic.fetch == null || EditorLogic.fetch.ship == null)
            return;

        // Add close button in top-right corner
        const float closeButtonSize = 20f;
        const float closeButtonPadding = 5f;
        
        Rect closeButtonRect = new Rect(
            mainWindowRect.width - closeButtonSize - closeButtonPadding,
            closeButtonPadding,
            closeButtonSize,
            closeButtonSize
        );

        if (GUI.Button(closeButtonRect, "×")) //  (looks like X)
        {
            showPresetEditor = false;
        }

        GUILayout.BeginVertical();

        // Current Presets
        GUILayout.Label("Custom Presets:");
        customPresetScrollPos = GUILayout.BeginScrollView(customPresetScrollPos, GUILayout.Height(125));
        for (int i = 0; i < customPresets.Count; i++)
        {
            GUILayout.BeginHorizontal();

            // The name of the preset
            GUILayout.Label(customPresets[i].DisplayName, dropdownItemStyle);

            // Remove button
            if (GUILayout.Button("X", GUILayout.Width(25)))
            {
                // Show a confirm window before removing the preset
                showConfirmDelete = true;
                removePresetIndex = i;
            }
            GUILayout.EndHorizontal();
        }
        GUILayout.EndScrollView();

        // Add New Preset
        GUILayout.Space(10);
        
        // The name of the new preset
        GUILayout.Label("New Preset Name:");
        GUILayout.Space(-5); // Reduce spacing between labels
        GUILayout.Label("(don't include {n} or Numbering Style)");
        if (EditorLogic.fetch.ship.shipName != null) newPresetName = EditorLogic.fetch.ship.shipName;
        newPresetName = GUILayout.TextField(newPresetName);

        // Add button
        if (GUILayout.Button("Add Preset"))
        {
            // If the new preset name is empty, use the ship name
            if (string.IsNullOrEmpty(newPresetName) && EditorLogic.fetch?.ship != null) newPresetName = EditorLogic.fetch.ship.shipName;

            // Create a new AutoRenamerConfig.Preset object
            Preset preset = new Preset();            
            // Build preset name
            string presetDisplayName = newPresetName + " {n} - " + config.NumberStyle.ToDisplayString();
            preset.DisplayName = presetDisplayName;
            preset.Name = newPresetName;
            preset.Suffix = config.SuffixFormat;
            preset.NumberingStyle = config.NumberStyle;

            // Add the preset to the list
            customPresets.Add(preset);

            // Clear the new preset name
            newPresetName = "";
        }

        GUILayout.Space(15);

        // Mission Templates
        GUILayout.Label("Preset Templates:");
        templateScrollPos = GUILayout.BeginScrollView(templateScrollPos, GUILayout.Height(75));
        for (int i = 0; i < templatePresets.Count; i++)
        {
            // The name of the template     
            GUILayout.Label(templatePresets[i].DisplayName, dropdownItemStyle);
        }
        GUILayout.EndScrollView();        
        GUILayout.EndVertical();

        // Allow window dragging
        GUI.DragWindow(new Rect(0, 0, presetEditormainWindowRect.width - closeButtonSize - closeButtonPadding, 20));    
    }

    /// Draws the confirm window for removing a preset.
    private void DrawConfirmRemovePresetWindow(int windowID)
    {
        if (!HighLogic.LoadedSceneIsEditor || EditorLogic.fetch == null || EditorLogic.fetch.ship == null)
            return;

        // Add close button in top-right corner
        const float closeButtonSize = 20f;
        const float closeButtonPadding = 5f;
        
        Rect closeButtonRect = new Rect(
            mainWindowRect.width - closeButtonSize - closeButtonPadding,
            closeButtonPadding,
            closeButtonSize,
            closeButtonSize
        );

        if (GUI.Button(closeButtonRect, "×")) //  (looks like X)
        {
            showConfirmDelete = false;
        }

        // Content
        GUILayout.Space(10); // Padding between close and content 

        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        GUIStyle boldLabel = new GUIStyle(GUI.skin.label);
        boldLabel.fontStyle = FontStyle.Bold;        
        GUILayout.Label("Remove preset:");
        GUILayout.Label(customPresets[removePresetIndex].DisplayName, boldLabel);
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.Space(10);

        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();

        // Buttons to confirm or cancel the delete
        if (GUILayout.Button("Confirm", GUILayout.Width(100), GUILayout.Height(30)))
        {
            // Retrieve DisplayName from Index
            string presetName = customPresets[removePresetIndex].DisplayName;
            Debug.Log($"[SmartVesselRenamer] Removing preset: {presetName}");
            // Remove the preset from the file
            PresetConfigSerializer.RemovePresetConfig(presetName);
            // Remove the preset from the cached list
            customPresets.RemoveAt(removePresetIndex);
            // Reload presets
            LoadPresetsFromConfig();
            showConfirmDelete = false;
        }

        GUILayout.Space(20);

        if (GUILayout.Button("Cancel", GUILayout.Width(100), GUILayout.Height(30)))
        {
            showConfirmDelete = false;
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        // Drag entire window from top region
        GUI.DragWindow(new Rect(0, 0, 10000, 20));
    
    }

    /// Draws a window to confirm the new name of a vessel before renaming it.
    /// 
    /// <param name="windowID">The ID of the window to draw.</param>
    private void DrawConfirmRenameWindow(int windowID)
    {
        if (!HighLogic.LoadedSceneIsEditor || EditorLogic.fetch == null || EditorLogic.fetch.ship == null)
            return;

        // Add close button in top-right corner
        const float closeButtonSize = 20f;
        const float closeButtonPadding = 5f;
        
        Rect closeButtonRect = new Rect(
            mainWindowRect.width - closeButtonSize - closeButtonPadding,
            closeButtonPadding,
            closeButtonSize,
            closeButtonSize
        );

        if (GUI.Button(closeButtonRect, "×")) //  (looks like X)
        {
            showConfirmRename = false;
        }

        // Content
        GUILayout.Space(10); // Padding between close and content

        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        GUIStyle boldLabel = new GUIStyle(GUI.skin.label);
        boldLabel.fontStyle = FontStyle.Bold;

        // Show the new name of the vessel
        GUIStyle boldStyle = new GUIStyle(GUI.skin.label);
        boldStyle.richText = true;
        GUILayout.Label($"New Name: <b>{cachedPreview}</b>", boldStyle);
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        GUILayout.Space(10);

        GUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        // Buttons to confirm or cancel the rename
        if (GUILayout.Button("Confirm", GUILayout.Width(100), GUILayout.Height(30)))
        {
            // Rename the vessel if the confirm button is clicked
            RenameVessel();
            showConfirmRename = false;
        }

        GUILayout.Space(20);

        if (GUILayout.Button("Cancel", GUILayout.Width(100), GUILayout.Height(30)))
        {
            // Cancel the rename if the cancel button is clicked
            showConfirmRename = false;
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal();

        // Drag entire window from top region
        GUI.DragWindow(new Rect(0, 0, 10000, 20));
    }

    private void RepaintGUI()
    {
        // Force GUI redraw
        GUI.changed = true;
        GUI.FocusControl(null);
        GUIUtility.keyboardControl = 0;
    }
    #endregion

    #region Preset System
    /// Loads the name presets from the configuration.
    /// This method retrieves the list of name presets stored in the configuration
    private void LoadPresetsFromConfig()
    {
        var presets = PresetConfigSerializer.LoadPresetConfig();
        if (presets != null) 
        {
            Debug.Log($"[SmartVesselRenamer] Loaded {presets.CustomPresets.Count} custom presets and {presets.TemplatePresets.Count} template presets.");
        }
        // Ensure lists are not null
        if (presets.CustomPresets == null)
            presets.CustomPresets = new List<Preset>();

        if (presets.TemplatePresets == null)
            presets.TemplatePresets = new List<Preset>();

        customPresets = presets.CustomPresets;
        templatePresets = presets.TemplatePresets;

        // Join custom and template presets
        allPresets = customPresets.Concat(templatePresets).ToList();
    }
    #endregion

    #region Preview System
    /// Updates the name preview by generating a unique name based on the current name of the vessel.
    private void UpdateNamePreview()
    {
        if (editorLogicCache?.ship != null)
        {
            // Generate a unique name for the vessel
            cachedPreview = GetUniqueName(editorLogicCache.ship.shipName);

            // Store the current time so we can check if the delay period has passed
            lastPreviewUpdateTime = Time.realtimeSinceStartup;

            CacheReferences();

            RepaintGUI();
        }
    }
    #endregion

    #region Utility Methods
    /// Loads the embedded icon texture for the Smart Vessel Renamer.
    /// 
    /// Tries to load the texture from the GameDatabase first. If that fails, it attempts
    /// to load it from the embedded resources. As a last resort, it returns a fallback icon.
    /// 
    /// <returns>A Texture2D object representing the loaded icon.</returns>
    private Texture2D LoadEmbeddedIcon()
    {
        // 1. First try to load from GameDatabase
        try
        {
            // Attempt to get the texture from the GameDatabase
            Texture2D tex = GameDatabase.Instance.GetTexture("SmartVesselRenamer/Textures/SmartVesselRenamer", false);
            if (tex != null) return tex;
        }
        catch
        {
            // Ignore exceptions, proceed to next method
        }

        // 2. Fallback to loading from embedded resources
        try
        {
            // Get the executing assembly
            var assembly = Assembly.GetExecutingAssembly();
            // Find the resource name ending with "SmartVesselRenamer.png"
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("SmartVesselRenamer.png"));

            if (resourceName != null)
            {
                // Open a stream to the resource
                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                {
                    // Read the image data from the stream
                    byte[] imageData = new byte[stream.Length];
                    stream.Read(imageData, 0, (int)stream.Length);

                    // Create a new Texture2D and load the image data
                    Texture2D tex = new Texture2D(64, 64, TextureFormat.ARGB32, false);
                    tex.LoadRawTextureData(imageData);
                    tex.Apply();
                    return tex;
                }
            }
        }
        catch
        {
            // Ignore exceptions, proceed to ultimate fallback
        }

        // 3. Ultimate fallback to a default icon
        return GetFallbackIcon();
    }

    /// Retrieves the fallback icon texture. Creates a new cyan-colored texture if it doesn't exist.
    /// 
    /// <returns>A Texture2D object for the fallback icon.</returns>
    private Texture2D GetFallbackIcon()
    {
        // Check if the fallback icon texture is uninitialized
        if (fallbackIconTexture == null)
        {
            // Create a new texture of size 38x38
            fallbackIconTexture = new Texture2D(38, 38);
            
            // Fill the texture with cyan color
            for (int y = 0; y < fallbackIconTexture.height; y++)
                for (int x = 0; x < fallbackIconTexture.width; x++)
                    fallbackIconTexture.SetPixel(x, y, Color.cyan);
            
            // Apply the changes to the texture
            fallbackIconTexture.Apply();
        }

        // Return the fallback icon texture
        return fallbackIconTexture;
    }

    /// Called when the object is destroyed. Handles cleanup operations.
    private void OnDestroy()
    {
        // Save the current configuration settings
        config.Save();

        // If the application button is not null, remove it from the application launcher
        if (appButton != null)
        {
            ApplicationLauncher.Instance.RemoveModApplication(appButton);
        }
    }

    /// Toggles the visibility of the Smart Vessel Renamer's GUI window.
    private void ToggleGui()
    {
        // Close all windows if opened
        if (showMainGui)
        {
            CloseAllWindows();
        } else {
            // Open the main GUI window
            showMainGui = true;
        }

        // If the GUI is now visible and the editor logic is available,
        // generate a unique name for the vessel and store it in the namePreview field
        if (showMainGui && EditorLogic.fetch?.ship != null)
        {
            cachedPreview = GetUniqueName(EditorLogic.fetch.ship.shipName);
        }
    }

    /// Closes all windows of the Smart Vessel Renamer.
    private void CloseAllWindows()
    {
        // Close the main GUI window
        showMainGui = false;

        // Close the preset editor window
        showPresetEditor = false;

        // Close the "Confirm Rename" window
        showConfirmRename = false;

        // Close the "Confirm Delete Preset" window
        showConfirmDelete = false;
    }
    #endregion

    #region Numbering Styles
    /// Converts a number to a string representation of the specified numbering style.
    /// 
    /// <param name="number">The number to convert to a string.</param>
    /// <param name="style">The numbering style to use for the conversion.</param>
    /// <returns>A string representation of the number in the specified numbering style.</returns>
    private string ConvertNumberToStyle(int number, NumberingStyle style)
    {
        switch (style)
        {
            // Arabic numbers, e.g. 1, 2, 3, ...
            case NumberingStyle.ArabicNumbers:
                return number.ToString();
                
            // Zero-padded numbers, e.g. 01, 02, 03, ...
            case NumberingStyle.ZeroPaddedNumbers:
                return number.ToString("00");
                
            // Roman numerals, e.g. I, II, III, ...
            case NumberingStyle.RomanNumerals:
                return ToRoman(number);
                
            // Latin letters, e.g. a, b, c, ...
            case NumberingStyle.LatinLetters:
                return ToLatinLetters(number);
                
            // Greek letters, e.g. , , ..., ...
            case NumberingStyle.GreekLetters:
                return ToGreekLetters(number);
                
            // Greek words, e.g. , , ..., ...
            case NumberingStyle.GreekWords:
                return ToGreekWords(number);
                
            // NATO phonetic alphabet, e.g. Alpha, Bravo, Charlie, ...
            case NumberingStyle.NATOPhonetic:
                return ToNATOPhonetic(number);
                
            // Russian letters, e.g. , , ..., ...
            case NumberingStyle.RussianLetters:
                return ToRussianLetters(number);
                
            
            case NumberingStyle.PartsCount:
                return ToPartsCount();

            
            case NumberingStyle.Date:
                return ToDate();

            // Default to Arabic numbers
            default:
                return number.ToString();
        }
    }

    /// Converts a number to a string representation of the number in Roman numerals.
    /// 
    /// <param name="number">The number to convert to a string.</param>
    /// <returns>A string representation of the number in Roman numerals.</returns>
    private string ToRoman(int number)
    {
        // Check if the number is outside the range of valid Roman numerals
        if (number < 1 || number > 3999) return number.ToString();
        
        // The list of Roman numerals and their corresponding values
        string[] roman = {"M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I"};
        int[] values = {1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1};
        
        // The result string
        string result = "";
        
        // Iterate over the list of Roman numerals and their values
        for (int i = 0; i < 13; i++)
        {
            // While the number is greater than or equal to the current Roman numeral value,
            // subtract the value from the number and add the Roman numeral to the result string
            while (number >= values[i])
            {
                number -= values[i];
                result += roman[i];
            }
        }
        
        // Return the result string
        return result;
    }

    /// Converts a number to a string representation of the number in Latin letters (a, b, c, ...).
    /// 
    /// <param name="number">The number to convert to a string.</param>
    /// <returns>A string representation of the number in Latin letters.</returns>
    private string ToLatinLetters(int number)
    {
        // Return "A" for numbers less than 1
        if (number < 1) return "A";
        
        // The result string
        string result = "";
        
        // Convert the number to Latin letters
        while (number > 0)
        {
            // Decrement the number
            number--;
            
            // Add the current letter to the start of the result string
            result = (char)('A' + (number % 26)) + result;
            
            // Divide the number by 26
            number /= 26;
        }
        
        // Return the result string
        return result;
    }

    /// Converts a number to a string representation of the number in Greek letters (, , ..., ).
    /// 
    /// <param name="number">The number to convert to a string.</param>
    /// <returns>A string representation of the number in Greek letters.</returns>
    private string ToGreekLetters(int number)
    {
        // The Greek letters in order (from 1 to 24)
        string[] greek = {
            "Α", "Β", "Γ", "Δ", "Ε", "Ζ", "Η", "Θ", "Ι", "Κ", "Λ", "Μ", 
            "Ν", "Ξ", "Ο", "Π", "Ρ", "Σ", "Τ", "Υ", "Φ", "Χ", "Ψ", "Ω"
        };
        // Return the Greek letter at the given index, or the number as a string if it's out of range
        return number <= greek.Length ? greek[number-1] : number.ToString();
    }

    /// Converts a number to a string representation of the number in Greek words (Alpha, Beta, ..., Omega).
    /// 
    /// <param name="number">The number to convert to a string.</param>
    /// <returns>A string representation of the number in Greek words.</returns>
    private string ToGreekWords(int number)
    {
        // The list of Greek words in order (from 1 to 24)
        string[] greekWords = {
            "Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta", "Eta", "Theta",
            "Iota", "Kappa", "Lambda", "Mu", "Nu", "Xi", "Omicron", "Pi", "Rho",
            "Sigma", "Tau", "Upsilon", "Phi", "Chi", "Psi", "Omega"
        };
        // Return the Greek word at the given index, or the number as a string if it's out of range
        return number <= greekWords.Length ? greekWords[number-1] : number.ToString();
    }

    /// Converts a number to its corresponding NATO phonetic alphabet representation.
    /// 
    /// <param name="number">The number to convert to a NATO phonetic alphabet string.</param>
    /// <returns>A string representation of the number in the NATO phonetic alphabet, or the number as a string if out of range.</returns>
    private string ToNATOPhonetic(int number)
    {
        // The NATO phonetic alphabet in order
        string[] nato = {
            "Alpha", "Bravo", "Charlie", "Delta", "Echo", "Foxtrot", "Golf", "Hotel",
            "India", "Juliett", "Kilo", "Lima", "Mike", "November", "Oscar", "Papa",
            "Quebec", "Romeo", "Sierra", "Tango", "Uniform", "Victor", "Whiskey", "X-ray",
            "Yankee", "Zulu"
        };

        // Return the NATO phonetic alphabet name at the given index, or the number as a string if it's out of range
        return number <= nato.Length ? nato[number-1] : number.ToString();
    }

    /// Converts a number to a string representation of the number in Russian letters (, , ..., , etc.).
    /// 
    /// <param name="number">The number to convert to a Russian letter string.</param>
    /// <returns>A string representation of the number in Russian letters.</returns>
    private string ToRussianLetters(int number)
    {
        // The list of Russian letters in order (from 1 to 33)
        string[] russian = {
            "А", "Б", "В", "Г", "Д", "Е", "Ж", "З", "И", "Й", "К", "Л", "М",
            "Н", "О", "П", "Р", "С", "Т", "У", "Ф", "Х", "Ц", "Ч", "Ш", "Щ",
            "Ъ", "Ы", "Ь", "Э", "Ю", "Я"
        };
        // Return the Russian letter at the given index, or the number as a string if it's out of range
        return number <= russian.Length ? russian[number-1] : number.ToString();
    }

    /// Converts a number to a string representation of the current part count of the vessel.
    /// This method is used as a special case for the "Parts Count" numbering style.
    /// 
    /// <param name="number">The number to convert to a part count string.</param>
    /// <returns>A string representation of the current part count of the vessel.</returns>
    private string ToPartsCount()
    {
        // Return the current part count as a string if the editor logic and the ship are not null
        // Otherwise, return 0
        var partsNumber = editorLogicCache?.ship?.Parts?.Count.ToString() ?? 0.ToString();
        return partsNumber + " Parts";
    }

    /// Converts the current date to a string representation in the format "yyyy-MM-dd".
    /// 
    /// <returns>A string representation of the current date in the format "yyyy-MM-dd".</returns>
    private string ToDate()
    {        
        // Get the current date and time and convert it to a string in the specified format
        return DateTime.Now.ToString("yyyy-MM-dd");
    }
    #endregion
}
