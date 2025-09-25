using TMPro;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Settingspanel : MonoBehaviour
{
    private ShopManager shopManager;
    // Call this from a UI Button to save all settings
    public void SaveSettings()
    {
        // Save resolution
        PlayerPrefs.SetInt("ResolutionIndex", currentResolutionIndex);
        // Save graphics quality
        PlayerPrefs.SetInt("GraphicsQualityIndex", currentQualityIndex);
        // Save volume (already saved in SetMasterVolume, but for completeness)
        PlayerPrefs.SetFloat("MasterVolume", masterVolume);
        PlayerPrefs.Save();
    }
    [Header("UI Elements")]
    public TMP_Dropdown resolutionDropdown;
    public TMP_Dropdown graphicsDropdown;
    public Slider volumeSlider;
    public Button saveSettingsButton; // Assign in Inspector, set TMP_Text as child for label
    public Button resetSettingsButton; // Assign in Inspector for Reset
    [Header("Assign the CanvasGroup of the settings panel")]
    public CanvasGroup panelCanvasGroup;

    // Optional: Key to toggle panel for demo/testing
    public KeyCode toggleKey = KeyCode.Escape;

    // Resolution settings
    private Resolution[] resolutions;
    public int currentResolutionIndex = 0;

    // Graphics quality settings
    public int currentQualityIndex = 0;

    // Volume settings
    [Range(0f, 1f)]
    public float masterVolume = 1f;

    [Header("Sliding Panel Animation")]
    public RectTransform panelRectTransform;
    public float slideDuration = 0.5f;
    public Vector2 hiddenPosition = new Vector2(-600, 0); // Off-screen left
    public Vector2 shownPosition = new Vector2(0, 0); // On-screen
    private bool isPanelVisible = false;
    private Coroutine slideCoroutine;

    void Start()
    {
        if (panelCanvasGroup != null)
        {
            HidePanelImmediate();
        }
        if (panelRectTransform != null)
        {
            panelRectTransform.anchoredPosition = hiddenPosition;
        }
        // Initialize resolutions
        resolutions = Screen.resolutions;
        currentResolutionIndex = GetCurrentResolutionIndex();
        // Initialize graphics quality
        currentQualityIndex = QualitySettings.GetQualityLevel();
        // Load saved volume or default to 1.0
        masterVolume = PlayerPrefs.GetFloat("MasterVolume", 1.0f);
        AudioListener.volume = masterVolume;
        // Setup UI
        SetupResolutionDropdown();
        SetupGraphicsDropdown();
        SetupVolumeSlider();
        if (saveSettingsButton != null)
        {
            saveSettingsButton.onClick.AddListener(SaveSettings);
        }
        if (resetSettingsButton != null)
        {
            resetSettingsButton.onClick.AddListener(ResetSettings);
        }
        // Find ShopManager in scene (or assign via Inspector for best practice)
    shopManager = FindFirstObjectByType<ShopManager>();
    }

    // --- UI Setup ---
    void SetupResolutionDropdown()
    {
        if (resolutionDropdown == null) return;
        resolutionDropdown.ClearOptions();
        List<TMP_Dropdown.OptionData> options = new List<TMP_Dropdown.OptionData>();
        for (int i = 0; i < resolutions.Length; i++)
        {
            string refreshRate = resolutions[i].refreshRateRatio.denominator == 1 ?
                resolutions[i].refreshRateRatio.numerator.ToString() :
                (resolutions[i].refreshRateRatio.numerator / (float)resolutions[i].refreshRateRatio.denominator).ToString("F2");
            string option = resolutions[i].width + " x " + resolutions[i].height + " @" + refreshRate + "Hz";
            options.Add(new TMP_Dropdown.OptionData(option));
        }
        resolutionDropdown.AddOptions(options);
        resolutionDropdown.value = currentResolutionIndex;
        resolutionDropdown.RefreshShownValue();
        resolutionDropdown.onValueChanged.AddListener(SetResolution);
    }

    void SetupGraphicsDropdown()
    {
        if (graphicsDropdown == null) return;
        graphicsDropdown.ClearOptions();
        List<TMP_Dropdown.OptionData> options = new List<TMP_Dropdown.OptionData>();
        foreach (var name in QualitySettings.names)
        {
            options.Add(new TMP_Dropdown.OptionData(name));
        }
        graphicsDropdown.AddOptions(options);
        graphicsDropdown.value = currentQualityIndex;
        graphicsDropdown.RefreshShownValue();
        graphicsDropdown.onValueChanged.AddListener(SetGraphicsQuality);
    }

    void SetupVolumeSlider()
    {
        if (volumeSlider == null) return;
        volumeSlider.minValue = 0f;
        volumeSlider.maxValue = 1f;
        volumeSlider.value = masterVolume;
        volumeSlider.onValueChanged.AddListener(SetMasterVolume);
    }

    void Update()
    {
        // Optional: Toggle panel with a key
        if (panelCanvasGroup != null && toggleKey != KeyCode.None && Input.GetKeyDown(toggleKey))
        {
            TogglePanelSlide();
        }
    }

    public void TogglePanelSlide()
    {
        if (slideCoroutine != null)
            StopCoroutine(slideCoroutine);
        if (isPanelVisible)
            slideCoroutine = StartCoroutine(SlidePanel(hiddenPosition, false));
        else
        {
            // Close shop if open
            if (shopManager != null)
                shopManager.CloseShopIfOpen();
            slideCoroutine = StartCoroutine(SlidePanel(shownPosition, true));
        }
    }

    private IEnumerator SlidePanel(Vector2 targetPosition, bool show)
    {
        if (panelRectTransform == null || panelCanvasGroup == null)
            yield break;
        float elapsed = 0f;
        Vector2 startPos = panelRectTransform.anchoredPosition;
        if (show)
        {
            panelCanvasGroup.alpha = 1f;
            panelCanvasGroup.interactable = true;
            panelCanvasGroup.blocksRaycasts = true;
        }
        while (elapsed < slideDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            panelRectTransform.anchoredPosition = Vector2.Lerp(startPos, targetPosition, elapsed / slideDuration);
            yield return null;
        }
        panelRectTransform.anchoredPosition = targetPosition;
        isPanelVisible = show;
        if (!show)
        {
            panelCanvasGroup.alpha = 0f;
            panelCanvasGroup.interactable = false;
            panelCanvasGroup.blocksRaycasts = false;
        }
    }

    // Instantly hide panel (no animation)
    public void HidePanelImmediate()
    {
        if (panelCanvasGroup == null || panelRectTransform == null) return;
        panelCanvasGroup.alpha = 0f;
        panelCanvasGroup.interactable = false;
        panelCanvasGroup.blocksRaycasts = false;
        panelRectTransform.anchoredPosition = hiddenPosition;
        isPanelVisible = false;
    }

    // --- Resolution Settings ---
    public void SetResolution(int resolutionIndex)
    {
        if (resolutions == null || resolutionIndex < 0 || resolutionIndex >= resolutions.Length)
            return;
        Resolution res = resolutions[resolutionIndex];
        Screen.SetResolution(res.width, res.height, Screen.fullScreen);
        currentResolutionIndex = resolutionIndex;
        if (resolutionDropdown != null)
        {
            resolutionDropdown.value = resolutionIndex;
            resolutionDropdown.RefreshShownValue();
        }
    }

    public Resolution[] GetAvailableResolutions()
    {
        return resolutions;
    }

    private int GetCurrentResolutionIndex()
    {
        for (int i = 0; i < resolutions.Length; i++)
        {
            if (Screen.currentResolution.width == resolutions[i].width &&
                Screen.currentResolution.height == resolutions[i].height)
            {
                return i;
            }
        }
        return 0;
    }

    // --- Graphics Quality Settings ---
    public void SetGraphicsQuality(int qualityIndex)
    {
        QualitySettings.SetQualityLevel(qualityIndex);
        currentQualityIndex = qualityIndex;
        if (graphicsDropdown != null)
        {
            graphicsDropdown.value = qualityIndex;
            graphicsDropdown.RefreshShownValue();
        }
    }

    public int GetGraphicsQuality()
    {
        return QualitySettings.GetQualityLevel();
    }

    public string[] GetQualityNames()
    {
        return QualitySettings.names;
    }

    // --- Volume Settings ---
    public void SetMasterVolume(float volume)
    {
        masterVolume = Mathf.Clamp01(volume);
        AudioListener.volume = masterVolume;
        PlayerPrefs.SetFloat("MasterVolume", masterVolume);
        PlayerPrefs.Save();
        if (volumeSlider != null && volumeSlider.value != masterVolume)
        {
            volumeSlider.value = masterVolume;
        }
    }

    public float GetMasterVolume()
    {
        return AudioListener.volume;
    }

    // --- Reset Settings ---
    public void ResetSettings()
    {
        // Set to 1920x1080 if available, else first available
        int resIndex = 0;
        if (resolutions != null && resolutions.Length > 0)
        {
            for (int i = 0; i < resolutions.Length; i++)
            {
                if (resolutions[i].width == 1920 && resolutions[i].height == 1080)
                {
                    resIndex = i;
                    break;
                }
            }
            SetResolution(resIndex);
        }
        // Set to first quality level
        SetGraphicsQuality(0);
        // Set volume to max
        SetMasterVolume(1.0f);
        // Update UI
        if (resolutionDropdown != null)
            resolutionDropdown.value = resIndex;
        if (graphicsDropdown != null)
            graphicsDropdown.value = 0;
        if (volumeSlider != null)
            volumeSlider.value = 1.0f;
    }
}
