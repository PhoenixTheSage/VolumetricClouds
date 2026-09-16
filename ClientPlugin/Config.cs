using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ClientPlugin.Settings;
using ClientPlugin.Settings.Elements;
using VRageMath;

namespace ClientPlugin;

public enum CloudQuality
{
    Low,
    Medium,
    High,
    Ultra
}

public class Config : INotifyPropertyChanged
{
    #region Options

    private bool enabled = true;
    private bool replaceVanilla = true;
    private CloudQuality quality = CloudQuality.High;
    private float coverage = 1.15f;
    private float density = 0.55f;
    private float thickness = 0.055f;
    private float windSpeed = 1.0f;
    private float cirrusStrength = 0.55f;
    private Color albedoTint = Color.White;
    private float hdrLift = 4f;
    private float fadeStartFactor = 8f;
    private float fadeEndFactor = 14f;

    #endregion

    #region User interface

    public readonly string Title = "Volumetric Clouds";

    [Separator("Volumetric Clouds")]

    [Checkbox(description: "Master switch for raymarched clouds")]
    public bool Enabled
    {
        get => enabled;
        set => SetField(ref enabled, value);
    }

    [Checkbox(label: "Replace vanilla layers", description: "Hide Keen CloudSphere layers while this pack is drawing")]
    public bool ReplaceVanilla
    {
        get => replaceVanilla;
        set => SetField(ref replaceVanilla, value);
    }

    [Dropdown(description: "Raymarching quality (samples per pixel)")]
    public CloudQuality Quality
    {
        get => quality;
        set => SetField(ref quality, value);
    }

    [Slider(0.05f, 2f, 0.01f, SliderAttribute.SliderType.Float, description: "Weather-map coverage scale")]
    public float Coverage
    {
        get => coverage;
        set => SetField(ref coverage, value);
    }

    [Slider(0.05f, 1.2f, 0.01f, SliderAttribute.SliderType.Float, description: "Cloud optical density")]
    public float Density
    {
        get => density;
        set => SetField(ref density, value);
    }

    [Slider(0.01f, 0.12f, 0.005f, SliderAttribute.SliderType.Float, description: "Shell thickness as a fraction of planet radius")]
    public float Thickness
    {
        get => thickness;
        set => SetField(ref thickness, value);
    }

    [Slider(0f, 8f, 0.05f, SliderAttribute.SliderType.Float, label: "Wind speed", description: "Cloud drift. 1 wraps the weather map in about 15 minutes")]
    public float WindSpeed
    {
        get => windSpeed;
        set => SetField(ref windSpeed, value);
    }

    [Slider(0f, 1.5f, 0.01f, SliderAttribute.SliderType.Float, label: "Cirrus", description: "High-altitude ice veil in the upper shell")]
    public float CirrusStrength
    {
        get => cirrusStrength;
        set => SetField(ref cirrusStrength, value);
    }

    [Color(description: "Multiplies the planet CloudLayer albedo")]
    public Color AlbedoTint
    {
        get => albedoTint;
        set => SetField(ref albedoTint, value);
    }

    [Slider(1f, 16f, 0.5f, SliderAttribute.SliderType.Float, label: "HDR lift", description: "Extra brightness when an HDR Display pack is live. 1 = same as SDR.")]
    public float HdrLift
    {
        get => hdrLift;
        set => SetField(ref hdrLift, value);
    }

    [Separator("Distance fade")]

    [Slider(1f, 40f, 0.1f, SliderAttribute.SliderType.Float, label: "Fade start", description: "Distance where clouds start fading (atmosphere radii)")]
    public float FadeStartFactor
    {
        get => fadeStartFactor;
        set => SetField(ref fadeStartFactor, value);
    }

    [Slider(1f, 40f, 0.1f, SliderAttribute.SliderType.Float, label: "Fade end", description: "Distance where clouds vanish (atmosphere radii)")]
    public float FadeEndFactor
    {
        get => fadeEndFactor;
        set => SetField(ref fadeEndFactor, value);
    }

    #endregion

    #region Derived values

    public const int MaxRaymarchSteps = 28;

    public int StepCount
    {
        get
        {
            switch (quality)
            {
                case CloudQuality.Low:
                    return 12;
                case CloudQuality.Medium:
                    return 16;
                case CloudQuality.Ultra:
                    return MaxRaymarchSteps;
                default:
                    return 20;
            }
        }
    }

    public int LightStepCount
    {
        get
        {
            switch (quality)
            {
                case CloudQuality.Low:
                    return 2;
                case CloudQuality.Medium:
                    return 3;
                default:
                    return 4;
            }
        }
    }

    #endregion

    #region Property change notification boilerplate

    public static readonly Config Default = new Config();
    public static readonly Config Current = ConfigStorage.Load();

    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        if (ReferenceEquals(this, Current))
            ConfigStorage.Save(this);
        return true;
    }

    #endregion
}
