using Godot;
using System.Linq;
using System.Text.Json.Nodes;

public partial class PetAgent
{
    string _approachingCompanion = "";
    string _activeCompanion = "";
    NativeGame.CompanionActivity _companionActivity;
    double _companionTime;
    Vector3 _companionStart;
    bool _companionWalkRecorded;

    bool AvailableForCompanionship => _reaction <= 0 && !_atHospital && !_returningHome && !_movingHome
        && _companionTime <= 0 && Town.Data.FindPet(Ident) is JsonNode pet && Town.Game.Active(pet)
        && !NativeGame.B(pet, "fostered") && NativeGame.D(pet, "health") >= 45 && NativeGame.D(pet, "hunger") >= 45;

    bool ChooseCompanion()
    {
        var friend = Town.FarmPets.Where(p => p != this && !p.IsQueuedForDeletion() && p.AvailableForCompanionship
                && Position.DistanceSquaredTo(p.Position) <= 18 * 18)
            .OrderByDescending(p => Town.Game.Familiarity(Ident, p.Ident))
            .ThenBy(p => Position.DistanceSquaredTo(p.Position)).FirstOrDefault();
        if (friend == null) return false;
        _approachingCompanion = friend.Ident;
        _target = friend.Position;
        _intent = "寻找伙伴";
        return true;
    }

    void BeginCompanionActivity(PetAgent friend, NativeGame.CompanionActivity activity)
    {
        _facilityIntent=null;
        _approachingCompanion = "";
        _activeCompanion = friend.Ident;
        _companionActivity = activity;
        _companionStart = Position;
        _companionTime = activity == NativeGame.CompanionActivity.Walk ? 7 : 4;
        _companionWalkRecorded = false;
        _wait = 2;
        _intent = "探索";
        _index = 0;
        _path.Clear();
    }

    void EndCompanionship()
    {
        _approachingCompanion = "";
        if (_companionTime <= 0) return;
        _companionTime = 0;
        _path.Clear();
        _index = 0;
    }

    bool UpdateCompanionActivity(JsonNode state, double delta)
    {
        if (_companionTime <= 0 && _approachingCompanion.Length > 0
            && Town.FarmPet(_approachingCompanion) is PetAgent friend && friend.AvailableForCompanionship
            && AvailableForCompanionship && Position.DistanceSquaredTo(friend.Position) <= 3.2f * 3.2f)
        {
            int familiarity = Town.Game.Familiarity(Ident, friend.Ident);
            var activity = familiarity >= 3 && familiarity % 3 == 0 ? NativeGame.CompanionActivity.Rest
                : familiarity >= 2 && familiarity % 3 == 2 ? NativeGame.CompanionActivity.Walk : NativeGame.CompanionActivity.Play;
            if(activity==NativeGame.CompanionActivity.Walk&&!Town.TryScheduleFarmRoutes(2))return false;
            BeginCompanionActivity(friend, activity);
            friend.BeginCompanionActivity(this, activity);
            if (activity == NativeGame.CompanionActivity.Walk)
            {
                Vector3 side = (friend.Position - Position).Normalized();
                Vector3 ahead = new(-side.Z, 0, side.X);
                if (ahead.LengthSquared() < .1f) ahead = Vector3.Forward;
                _navigation ??= new FarmNavigation(Town);
                friend._navigation ??= new FarmNavigation(Town);
                _path.AddRange(_navigation.Route(Position, Position + ahead * 9, 1.6f));
                friend._path.AddRange(friend._navigation.Route(friend.Position, friend.Position + ahead * 9, 1.6f));
                if (_path.Count == 0 || friend._path.Count == 0)
                {
                    activity = NativeGame.CompanionActivity.Play;
                    BeginCompanionActivity(friend, activity);
                    friend.BeginCompanionActivity(this, activity);
                }
            }
            if (activity != NativeGame.CompanionActivity.Walk)
                Town.Game.RecordCompanionActivity(Ident, friend.Ident, activity);
        }
        if (_companionTime <= 0) return false;
        var partner = Town.FarmPet(_activeCompanion);
        if (partner == null || partner._reaction > 0 || partner._returningHome || partner._companionTime <= 0)
        {
            EndCompanionship();
            return false;
        }
        if (_companionTime <= delta)
        {
            EndCompanionship();
            return false;
        }
        _companionTime -= delta;
        if (_companionActivity == NativeGame.CompanionActivity.Walk)
        {
            if (!_companionWalkRecorded && Position.DistanceSquaredTo(_companionStart) >= 2.25f
                && partner.Position.DistanceSquaredTo(partner._companionStart) >= 2.25f
                && Position.DistanceSquaredTo(partner.Position) <= 25)
            {
                Town.Game.RecordCompanionActivity(Ident, partner.Ident, _companionActivity);
                _companionWalkRecorded = partner._companionWalkRecorded = true;
            }
            if (_index < _path.Count) return false;
            Velocity = Vector3.Zero;
            Activity = "在散步路上等伙伴";
            ShowPose(state, "idle");
            return true;
        }
        Velocity = Vector3.Zero;
        Activity = NativeGame.CompanionActivityName(_companionActivity);
        ShowPose(state, _companionActivity == NativeGame.CompanionActivity.Rest ? "sleep" : "pet");
        return true;
    }
}
