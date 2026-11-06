using Godot;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

public partial class Main
{
    readonly record struct AtmospherePalette(Color Sky, Color Horizon, Color Ambient, Color Sun, float SunEnergy, float WindowGlow);

    const string AmbienceSettingsPath = "user://ambient-audio.cfg";
    const int AmbienceSampleRate = 22050;
    static readonly Color LampColor = new("ffd18a");
    static readonly AtmospherePalette NightPalette = new(new("14223c"), new("40516b"), new("8b9abf"), new("9ebaf1"), .19f, 1);
    static readonly AtmospherePalette DayPalette = new(new("72aecf"), new("d6e8d7"), new("e7ead9"), new("fff0d6"), .67f, 0);
    static readonly (float Hour, AtmospherePalette Palette)[] AtmosphereSchedule = {
        (0, NightPalette), (4.75f, NightPalette),
        (6.25f, new(new("829dbe"), new("ffd6a6"), new("e5c2a0"), new("ffca8e"), .47f, .37f)),
        (8, DayPalette), (16.75f, DayPalette),
        (18.4f, new(new("957aab"), new("ffc298"), new("e5b4aa"), new("ffb17a"), .49f, .42f)),
        (20, NightPalette), (24, NightPalette)
    };

    ProceduralSkyMaterial? _townSky;
    Node3D? _townStreetLighting;
    StandardMaterial3D? _streetLampGlass;
    readonly List<Vector3> _streetLampPositions = new();
    readonly List<OmniLight3D> _nearbyStreetLights = new();
    int? _streetLightingRevision;
    int _atmosphereMinute = -1;
    string _atmosphereWeather = "";
    bool _allowTownAmbience;
    float _birdVolume = .35f, _rainVolume = .34f;
    AudioStreamPlayer? _birdAmbience, _rainAmbience;

    internal int StreetLampCount => _streetLampPositions.Count;
    internal float TownWindowGlow { get; private set; }

    void InitializeAtmosphere()
    {
        _townSky = new ProceduralSkyMaterial { SkyCurve = .42f, GroundCurve = .35f, SunAngleMax = 0 };
        var environment = _residentialSky!;
        environment.BackgroundMode = Godot.Environment.BGMode.Sky;
        environment.Sky = new Sky
        {
            SkyMaterial = _townSky,
            RadianceSize = Sky.RadianceSizeEnum.Size32,
            ProcessMode = Sky.ProcessModeEnum.Incremental,
        };
        environment.ReflectedLightSource = Godot.Environment.ReflectionSource.Disabled;
        environment.FogEnabled = true;
        environment.FogSkyAffect = 0;
        _residentialSun!.SkyMode = DirectionalLight3D.SkyModeEnum.LightOnly;

        _allowTownAmbience = DisplayServer.GetName() != "headless" && !OS.GetCmdlineUserArgs().Any(arg =>
            arg.StartsWith("--verify-", StringComparison.Ordinal) || arg.StartsWith("--benchmark", StringComparison.Ordinal)
            || arg.EndsWith("-preview", StringComparison.Ordinal) || arg is "--smoke" or "--ui-smoke");
        if (_allowTownAmbience)
        {
            using var settings = new ConfigFile();
            if (settings.Load(AmbienceSettingsPath) == Error.Ok)
            {
                _birdVolume = Mathf.Clamp(settings.GetValue("ambient", "birds", _birdVolume).AsSingle(), 0, 1);
                _rainVolume = Mathf.Clamp(settings.GetValue("ambient", "rain", _rainVolume).AsSingle(), 0, 1);
            }
        }
        UpdateAtmosphere();
    }

    void UpdateAtmosphere()
    {
        if (_townSky == null) return;
        int minute = (int)((Game.Now % NativeGame.Day + NativeGame.Day) % NativeGame.Day / 60);
        float hour = minute / 60f;
        string weather = Game.Weather;
        bool rain = weather.Contains("雨");
        if (_atmosphereMinute != minute || _atmosphereWeather != weather)
        {
            _atmosphereMinute = minute;
            _atmosphereWeather = weather;
            ApplySkyPalette(hour, weather);
        }
        UpdateStreetLighting();
        if (_allowTownAmbience)
        {
            bool birds = hour is >= 5.5f and < 19.5f && !rain && !weather.Contains("雪");
            SetAmbience(ref _birdAmbience, "TownBirdAmbience", birds ? _birdVolume : 0, MakeBirdLoop);
            SetAmbience(ref _rainAmbience, "TownRainAmbience", rain ? _rainVolume : 0, MakeRainLoop);
        }
    }

    void ApplySkyPalette(float hour, string weather)
    {
        int next = Array.FindIndex(AtmosphereSchedule, point => point.Hour > hour);
        if (next <= 0) next = AtmosphereSchedule.Length - 1;
        var (startHour, start) = AtmosphereSchedule[next - 1];
        var (endHour, end) = AtmosphereSchedule[next];
        float blend = Mathf.SmoothStep(startHour, endHour, hour);
        float cloud = weather switch { "暴雨" => .74f, "小雨" or "雨夹雪" or "降雪" => .58f, "阴天" => .42f, _ => 0 };
        Color horizon = start.Horizon.Lerp(end.Horizon, blend);
        Color skyTop = start.Sky.Lerp(end.Sky, blend).Lerp(horizon, cloud * .72f);
        _townSky!.SkyTopColor = skyTop;
        _townSky.SkyHorizonColor = horizon.Lerp(skyTop, cloud * .2f);
        _townSky.GroundBottomColor = horizon.Darkened(.24f);
        _townSky.GroundHorizonColor = _townSky.SkyHorizonColor;
        var environment = _residentialSky!;
        environment.AmbientLightColor = start.Ambient.Lerp(end.Ambient, blend).Lerp(new Color("b5c2d1"), cloud * .5f);
        environment.AmbientLightEnergy = .30f + cloud * .08f;
        environment.FogLightColor = _townSky.SkyHorizonColor;
        environment.FogDensity = .00065f + cloud * .00225f;
        var sun = _residentialSun!;
        sun.LightColor = start.Sun.Lerp(end.Sun, blend).Lerp(new Color("c8d4e0"), cloud * .75f);
        sun.LightEnergy = Mathf.Lerp(start.SunEnergy, end.SunEnergy, blend) * (1 - cloud * .54f);
        float daylight = Mathf.Clamp((hour - 5.25f) / 14.5f, 0, 1);
        sun.RotationDegrees = new(-Mathf.Lerp(17, 58, Mathf.Sin(daylight * Mathf.Pi)), Mathf.Lerp(62, -122, daylight), 0);

        TownWindowGlow = Mathf.Lerp(start.WindowGlow, end.WindowGlow, blend);
        _material.SetShaderParameter("window_glow", TownWindowGlow);
    }

    void UpdateStreetLighting()
    {
        int revision = _roadTopologyVersion;
        if (_streetLightingRevision != revision)
        {
            _streetLightingRevision = revision;
            BuildStreetLighting();
        }
        if (_streetLampGlass != null) _streetLampGlass.EmissionEnergyMultiplier = TownWindowGlow * 1.3f;

        // All lanterns glow, while only the closest ones cast light on nearby geometry.
        var nearest = TownWindowGlow > .05f
            ? _streetLampPositions.Where(point => point.DistanceSquaredTo(_center) < 80 * 80)
                .OrderBy(point => point.DistanceSquaredTo(_center)).Take(ActiveGraphics.Lights).ToArray()
            : Array.Empty<Vector3>();
        for (int i = 0; i < _nearbyStreetLights.Count; i++)
        {
            var lamp = _nearbyStreetLights[i];
            lamp.Visible = i < nearest.Length;
            if (!lamp.Visible) continue;
            lamp.Position = nearest[i] + Vector3.Down * .2f;
            lamp.LightEnergy = TownWindowGlow * 1.55f;
        }
    }

    void BuildStreetLighting()
    {
        if (_townStreetLighting != null)
        {
            RemoveChild(_townStreetLighting);
            _townStreetLighting.QueueFree();
        }
        _townStreetLighting = new Node3D { Name = "TownStreetLighting" };
        AddChild(_townStreetLighting);
        _streetLampPositions.Clear();
        _nearbyStreetLights.Clear();
        var frames = new StreetMeshBuilder();
        var lenses = new StreetMeshBuilder();
        var frameColor = new Color("485956");
        foreach (var cell in Data.Roads.Keys)
        {
            int mask = StreetMask(cell);
            bool northSouth = mask == 5;
            if ((!northSouth && mask != 10) || (northSouth ? cell.Y : cell.X) % 4 != 0) continue;
            float side = ((cell.X + cell.Y / 4 + cell.X / 4) & 1) == 0 ? 1 : -1;
            var lateral = (northSouth ? Vector3.Right : Vector3.Back) * side;
            var pole = WorldData.Cell(cell) + lateral * 3.84f;
            var glass = pole - lateral * .32f + Vector3.Up * 3.37f;
            _streetLampPositions.Add(glass);
            frames.Box(pole + Vector3.Up * .15f, new(.29f, .30f, .29f), frameColor);
            frames.Box(pole + Vector3.Up * 1.69f, new(.14f, 3.38f, .14f), frameColor);
            frames.Box(glass + Vector3.Up * .20f, new(.65f, .15f, .65f), frameColor);
            lenses.Box(glass, new(.43f, .27f, .43f), LampColor);
        }
        if (_streetLampPositions.Count == 0) return;
        _streetLampGlass ??= new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true, EmissionEnabled = true, Emission = LampColor, Roughness = .65f,
        };
        _townStreetLighting.AddChild(new MeshInstance3D { Name = "StreetLampFrames", Mesh = frames.Commit(_material) });
        _townStreetLighting.AddChild(new MeshInstance3D
        {
            Name = "StreetLampGlass", Mesh = lenses.Commit(_streetLampGlass), CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        for (int i = 0; i < 4; i++)
        {
            var light = new OmniLight3D { LightColor = LampColor, OmniRange = 10, OmniAttenuation = 1.3f, Visible = false };
            _townStreetLighting.AddChild(light);
            _nearbyStreetLights.Add(light);
        }
    }

    void AtmosphereSoundControls()
    {
        Section("小镇环境音", "根据游戏时段和天气播放");
        var panel = MarketCard(_managementBody!, color: "e8efe5");
        AddVolume("日间鸟鸣", "BirdAmbienceVolume", _birdVolume, value => _birdVolume = value);
        AddVolume("雨声和屋檐滴水", "RainAmbienceVolume", _rainVolume, value => _rainVolume = value);
        MarketLabel(panel, "音量调到 0 即可关闭对应声音。", color: TownTheme.Muted);

        void AddVolume(string label, string name, float initial, Action<float> set)
        {
            var row = new HBoxContainer();
            panel.AddChild(row);
            MarketLabel(row, label).CustomMinimumSize = new(128, 0);
            var slider = new HSlider
            {
                Name = name, MinValue = 0, MaxValue = 100, Step = 1, Value = Math.Round(initial * 100),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                TooltipText = label,
            };
            row.AddChild(slider);
            var number = MarketLabel(row, $"{slider.Value:0}%", color: TownTheme.Muted);
            number.CustomMinimumSize = new(46, 0);
            bool dragging = false;
            slider.DragStarted += () => dragging = true;
            slider.DragEnded += changed =>
            {
                dragging = false;
                if (changed) SaveAmbiencePreferences();
            };
            slider.ValueChanged += value =>
            {
                set((float)value / 100);
                number.Text = $"{value:0}%";
                UpdateAtmosphere();
                if (!dragging) SaveAmbiencePreferences();
            };
        }
    }

    void SaveAmbiencePreferences()
    {
        if (!_allowTownAmbience) return;
        using var settings = new ConfigFile();
        settings.SetValue("ambient", "birds", _birdVolume);
        settings.SetValue("ambient", "rain", _rainVolume);
        if (settings.Save(AmbienceSettingsPath) != Error.Ok)
            SetManagementFeedback("环境音量设置保存失败，当前调整仍然生效。", true);
    }

    void SetAmbience(ref AudioStreamPlayer? player, string name, float volume, Func<AudioStreamWav> stream)
    {
        if (volume <= 0)
        {
            player?.Stop();
            return;
        }
        if (player == null)
        {
            player = new AudioStreamPlayer { Name = name, Stream = stream(), Bus = TownAudioBus };
            AddChild(player);
        }
        player.VolumeLinear = volume;
        if (!player.Playing) player.Play();
    }

    static AudioStreamWav MakeBirdLoop()
    {
        var chirps = new (double Start, double Duration, double Pitch, double Sweep)[]
        {
            (1.0, .16, 1650, 760), (1.21, .23, 2040, -360),
            (3.9, .21, 1530, 530), (4.18, .12, 2220, 270),
            (7.3, .14, 2530, -550), (7.51, .17, 2260, -380), (7.78, .12, 2380, 460),
            (10.6, .22, 1670, 780), (10.94, .19, 2060, -510),
        };
        return AmbienceLoop(13, time =>
        {
            double sample = 0;
            foreach (var (start, length, pitch, sweep) in chirps)
            {
                double elapsed = time - start;
                if (elapsed <= 0 || elapsed >= length) continue;
                double phase = Math.Tau * (pitch * elapsed + sweep * elapsed * elapsed / (2 * length));
                double envelope = Math.Pow(Math.Sin(Math.PI * elapsed / length), 1.6);
                sample += envelope * (.075 * Math.Sin(phase) + .012 * Math.Sin(phase * 2));
            }
            return sample;
        });
    }

    static AudioStreamWav MakeRainLoop()
    {
        var random = new Random(731);
        double lowNoise = 0, nextDrop = .1, dropStart = -.5, pitch = 950;
        return AmbienceLoop(11, time =>
        {
            if (time >= nextDrop)
            {
                dropStart = time;
                nextDrop = time + .06 + random.NextDouble() * .22;
                pitch = 570 + random.NextDouble() * 1250;
            }
            double white = random.NextDouble() - .5;
            lowNoise = lowNoise * .91 + white * .09;
            double elapsed = time - dropStart;
            double drop = .10 * (1 - Math.Exp(-elapsed * 650)) * Math.Exp(-elapsed * 105)
                * Math.Sin(Math.Tau * pitch * elapsed * (1 + elapsed * 4));
            double fade = Math.Min(1, Math.Min(time, 11 - time) * 26);
            return (.024 * white + .16 * lowNoise + drop) * fade;
        });
    }

    static AudioStreamWav AmbienceLoop(int seconds, Func<double, double> wave)
    {
        int frames = seconds * AmbienceSampleRate;
        var data = new byte[frames * sizeof(short)];
        for (int frame = 0; frame < frames; frame++)
        {
            short sample = (short)Math.Clamp(wave((double)frame / AmbienceSampleRate) * short.MaxValue, short.MinValue, short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(data.AsSpan(frame * sizeof(short)), sample);
        }
        return new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits, Data = data, MixRate = AmbienceSampleRate,
            LoopMode = AudioStreamWav.LoopModeEnum.Forward, LoopEnd = frames,
        };
    }
}
