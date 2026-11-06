internal static class PetAmbientMotion
{
    internal readonly record struct Profile(string Pose, string Activity);

    const int FrameDurationMs = 170;
    static readonly int[] Frames = { 0, 1, 2, 1, 0, 3, 4, 3, 0, 1, 2, 1, 0 };
    internal static int ActiveMs => Frames.Length * FrameDurationMs;
    internal const int CycleMs = 7200;

    internal static Profile? ForSpecies(string species) => species switch
    {
        "兔子" => new("look", "竖耳张望"),
        "绵羊" => new("graze", "低头啃草"),
        "鹦鹉" => new("preen", "歪头梳羽"),
        "狐狸" or "小狗" => new("wag", "摇尾巴"),
        "猫咪" => new("groom", "抬爪梳洗"),
        "仓鼠" or "刺猬" => new("sniff", "探头嗅闻"),
        "羊驼" or "梅花鹿" => new("look", "抬头张望"),
        "小鸭" => new("dabble", "低头探啄"),
        "孔雀" => new("display", "舒展尾羽"),
        _ => null,
    };

    internal static int FrameAt(int elapsedMs) => Frames[elapsedMs / FrameDurationMs];

    internal static float Sweep(int frame) => frame switch
    {
        1 => -.52f,
        2 => -1,
        3 => .52f,
        4 => 1,
        _ => 0,
    };
}
