using Godot;
using System;
using System.Text.Json.Nodes;

public partial class Main
{
    Action<JsonNode>? AddOrderPublisherControls(VBoxContainer parent, JsonNode order)
    {
        string id = NativeGame.S(order, "publisher_id");
        if (id.Length == 0) return null;
        var identity = MarketLabel(parent, "", color: TownTheme.Muted, wrap: true);
        identity.Name = "OrderPublisherDetails";
        var story = MarketLabel(parent, "", color: TownTheme.Leaf, wrap: true);
        story.Name = "OrderPublisherStory";
        var actions = new HFlowContainer();
        actions.AddThemeConstantOverride("h_separation", 8);
        parent.AddChild(actions);
        var locate = MarketButton(actions, "定位居民", () => LocateResident(id), expand: false, accent: "家庭");
        locate.Name = "OrderPublisherLocate";
        var profile = MarketButton(actions, "查看居民", () => OpenResidentDirectoryEntry(id), expand: false, accent: "家庭");
        var residence = MarketButton(actions, "查看住所", () =>
        {
            if (Resident(id) is JsonNode person && Game.ResidentHome(person) is JsonNode home)
                SelectResidence(NativeGame.S(home, "building_id"));
        }, expand: false, accent: "家庭");
        residence.Name = "OrderPublisherResidence";
        var work = MarketButton(actions, "查看工作区", () =>
        {
            string zone = Resident(id) is JsonNode person ? Game.ResidentWorkZone(person) : NativeGame.S(order, "publisher_work");
            if (Data.Portals.ContainsKey(zone)) ShowManagement("地图", "district:" + zone);
        }, expand: false, accent: "地图");
        work.Name = "OrderPublisherWork";
        var project=MarketButton(actions,"定位邻里成果",()=>LocateNeighborhoodResult(id),expand:false,accent:"地图");
        project.Visible=false;

        return current =>
        {
            var person = Resident(id);
            var progress = Game.NeighborhoodProgress(id);
            string zone = person != null ? Game.ResidentWorkZone(person) : NativeGame.S(current, "publisher_work");
            var home = person != null ? Game.ResidentHome(person) : null;
            string place = home != null ? $" · 住所 {ResidenceName(home)}" : "";
            string job = person != null ? NativeGame.S(person, "profession") + " · " : "";
            string workplace = zone.Length > 0 ? AutonomyZoneName(zone) + " · " : "";
            identity.Text = $"{job}{workplace}已交付 {progress.Deliveries} 单{place}";
            string description = NativeGame.S(current, "story");
            int step = NativeGame.I(current, "story_step");
            int remaining = Math.Max(0, 2 * (progress.StoryStep + 1) - progress.Deliveries);
            story.Text = step > 0 ? $"邻里委托 · 第 {step} 章 · {description}"
                : $"邻里故事 · 已完成 {progress.StoryStep} / 3 章" + (progress.StoryStep >= 3 ? ""
                    : remaining > 0 ? $" · 再交付 {remaining} 单解锁后续" : " · 已解锁后续委托");
            project.Visible=Game.HasNeighborhoodResult(id);
            locate.Disabled = person == null || NativeGame.S(person, "status") == "离镇";
            profile.Disabled = person == null;
            residence.Visible = home != null;
            work.Disabled = !Data.Portals.ContainsKey(zone);
        };
    }
}
