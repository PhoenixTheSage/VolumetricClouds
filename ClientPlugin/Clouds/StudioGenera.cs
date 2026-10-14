namespace ClientPlugin.Clouds;

/// <summary>
/// Ten genera as points in the studio profile. Blend lerps the vector.
/// The noise tile does not change.
/// </summary>
static class StudioGenera
{
    struct Preset
    {
        public float Width, Height, Depth;
        public float Coverage, Base, Crown, Envelope, Density, DensityPower, DetailType, Erosion;
        public float Anvil, Shear, Lid, Breakup, Cells, Aspect, Heading, Fill;
    }

    // Order matches StudioGenus / planetary type ids.
    // Sheets are wide and filled. Cumulus is one puff. Cb shelves.
    // High cloud is thin; cirrus is a streak; Ac and Cc are cells.
    static readonly Preset[] Table =
    {
        P(1800, 180, 1800, 1.30f, 0.08f, 0.92f, 1.15f, 0.70f, 1.00f, 0.35f, 0.40f, 0.00f, 0.00f, 0.90f, 0.15f, 2.0f, 1.40f, 0.40f, 0.85f),
        P(1600, 280, 1600, 1.10f, 0.12f, 0.78f, 1.00f, 0.90f, 1.00f, 0.70f, 1.20f, 0.05f, 0.10f, 0.55f, 0.65f, 3.5f, 1.20f, 0.20f, 0.45f),
        P(1000, 520, 1000, 1.00f, 0.16f, 0.58f, 0.78f, 1.00f, 1.00f, 0.65f, 1.00f, 0.00f, 0.00f, 0.10f, 0.20f, 2.0f, 1.00f, 0.00f, 0.15f),
        P(2000, 700, 2000, 1.40f, 0.06f, 0.88f, 1.20f, 1.60f, 0.70f, 0.40f, 0.50f, 0.00f, 0.15f, 0.75f, 0.10f, 2.0f, 1.30f, 0.60f, 0.80f),
        P(1400, 1600, 1200, 1.15f, 0.10f, 0.72f, 0.70f, 1.40f, 1.00f, 0.45f, 1.30f, 0.85f, 0.70f, 0.35f, 0.25f, 2.0f, 1.10f, 0.80f, 0.35f),
        P(1700, 220, 1700, 0.85f, 0.10f, 0.90f, 1.15f, 0.45f, 1.00f, 0.20f, 0.60f, 0.00f, 0.25f, 0.70f, 0.05f, 1.5f, 1.80f, 0.30f, 0.70f),
        P(1200, 260, 1200, 0.90f, 0.18f, 0.62f, 0.55f, 0.75f, 1.00f, 0.75f, 1.40f, 0.00f, 0.05f, 0.20f, 0.80f, 5.0f, 1.00f, 0.10f, 0.10f),
        P(2200, 120, 800, 0.70f, 0.20f, 0.70f, 0.45f, 0.35f, 0.80f, 0.05f, 1.80f, 0.10f, 0.90f, 0.15f, 0.35f, 2.0f, 3.20f, 0.50f, 0.05f),
        P(2400, 100, 2400, 0.60f, 0.12f, 0.92f, 1.30f, 0.25f, 1.10f, 0.08f, 0.80f, 0.00f, 0.40f, 0.85f, 0.05f, 1.5f, 1.60f, 0.20f, 0.75f),
        P(1400, 140, 1400, 0.75f, 0.22f, 0.55f, 0.40f, 0.50f, 1.00f, 0.55f, 1.60f, 0.00f, 0.10f, 0.15f, 0.90f, 6.5f, 1.10f, 0.15f, 0.00f),
    };

    public static void Apply(Config config)
    {
        int from = Clamp((int)config.StudioGenusFrom);
        int to = Clamp((int)config.StudioMorphTarget);
        float t = config.StudioMorph;
        if (t < 0f) t = 0f;
        if (t > 1f) t = 1f;
        Preset a = Table[from];
        Preset b = Table[to];
        config.StudioWidth = Lerp(a.Width, b.Width, t);
        config.StudioHeight = Lerp(a.Height, b.Height, t);
        config.StudioDepth = Lerp(a.Depth, b.Depth, t);
        config.StudioCoverage = Lerp(a.Coverage, b.Coverage, t);
        config.StudioBaseSoft = Lerp(a.Base, b.Base, t);
        config.StudioCrown = Lerp(a.Crown, b.Crown, t);
        config.StudioEnvelope = Lerp(a.Envelope, b.Envelope, t);
        config.StudioDensity = Lerp(a.Density, b.Density, t);
        config.StudioDensityPower = Lerp(a.DensityPower, b.DensityPower, t);
        config.StudioDetailType = Lerp(a.DetailType, b.DetailType, t);
        config.StudioErosion = Lerp(a.Erosion, b.Erosion, t);
        config.StudioAnvil = Lerp(a.Anvil, b.Anvil, t);
        config.StudioShear = Lerp(a.Shear, b.Shear, t);
        config.StudioLid = Lerp(a.Lid, b.Lid, t);
        config.StudioBreakup = Lerp(a.Breakup, b.Breakup, t);
        config.StudioCells = Lerp(a.Cells, b.Cells, t);
        config.StudioAspect = Lerp(a.Aspect, b.Aspect, t);
        config.StudioHeading = Lerp(a.Heading, b.Heading, t);
        config.StudioFill = Lerp(a.Fill, b.Fill, t);
    }

    static int Clamp(int id)
    {
        if (id < 0) return 0;
        if (id > 9) return 9;
        return id;
    }

    static float Lerp(float a, float b, float t) => a + (b - a) * t;

    static Preset P(
        float width, float height, float depth,
        float coverage, float baseSoft, float crown, float envelope,
        float density, float densityPower, float detailType, float erosion,
        float anvil, float shear, float lid, float breakup, float cells,
        float aspect, float heading, float fill)
    {
        return new Preset
        {
            Width = width, Height = height, Depth = depth,
            Coverage = coverage, Base = baseSoft, Crown = crown, Envelope = envelope,
            Density = density, DensityPower = densityPower, DetailType = detailType, Erosion = erosion,
            Anvil = anvil, Shear = shear, Lid = lid, Breakup = breakup, Cells = cells,
            Aspect = aspect, Heading = heading, Fill = fill,
        };
    }
}
