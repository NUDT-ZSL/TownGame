using Godot;
using System;
using System.Linq;

public partial class Main
{
    void AddPetRelationships(VBoxContainer parent, string id, bool archive)
    {
        var card = MarketCard(parent, color: "edf0e4");
        MarketLabel(card, archive ? "伙伴关系档案" : "伙伴关系", 17, TownTheme.Leaf);
        var list = new VBoxContainer { Name = archive ? "PetRelationshipArchive" : "PetRelationships" };
        card.AddChild(list);
        NativeGame.PetRelationship[]? displayed = null;

        void Refresh()
        {
            var relationships = Game.PetRelationships(id);
            if (!archive) relationships = relationships.Take(4).ToArray();
            if (displayed != null && displayed.SequenceEqual(relationships)) return;
            displayed = relationships;
            foreach (Node child in list.GetChildren())
            {
                list.RemoveChild(child);
                child.QueueFree();
            }
            if (relationships.Length == 0)
                MarketLabel(list, "尚无共同活动记录。伙伴在场景中结伴后，这里会留下足迹。", color: TownTheme.Muted, wrap: true);
            foreach (var relation in relationships)
            {
                string friendId = relation.OtherId;
                var friend = Data.FindPet(friendId);
                string name = friend == null ? "伙伴" : NativeGame.S(friend, "name");
                string totals = $"玩耍 {relation.Play} · 散步 {relation.Walk} · 休息 {relation.Rest}";
                string last = $"最近 · {NativeGame.CompanionActivityName(relation.LastActivity)} · {Stamp(relation.LastAt)}";
                MarketLabel(list, $"{name} · {relation.Familiarity} · {totals}\n{last}", color: TownTheme.Ink, wrap: true);
                if (friend != null)
                    MarketButton(list, "查看 " + name, () =>
                    {
                        if (!archive && Game.Active(friend)) OpenPetManagement(friendId);
                        else OpenPetArchive(friendId);
                    }, expand: false, accent: "宠物");
            }
            if (!archive)
                MarketButton(list, "查看完整关系档案", () => OpenPetArchive(id), expand: false, accent: "宠物");
        }
        Refresh();
        _refreshManagementPage += Refresh;
    }
}
