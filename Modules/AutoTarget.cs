using System;
using System.Linq;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Logging;
using Hypostasis.Game.Structures;
using GameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace ReAction.Modules;

public unsafe class AutoTarget : PluginModule
{
    public override bool ShouldEnable => ReAction.Config.EnableAutoTarget;

    protected override bool Validate() => Game.fpGetGameObjectFromObjectID != null && ActionManager.canUseActionOnGameObject.IsValid;
    protected override void Enable() => ActionStackManager.PostActionStack += PostActionStack;
    protected override void Disable() => ActionStackManager.PostActionStack -= PostActionStack;

    private static void TargetEnemy()
    {
        if (DalamudApi.ClientState.LocalPlayer is not { } p) return;

        var worldCamera = Common.CameraManager->worldCamera;
        if (worldCamera == null) return;

        var hRotation = worldCamera->currentHRotation + Math.PI * 1.5;
        if (worldCamera->IsHRotationOffset)
            hRotation -= Math.PI;

        const double doublePI = Math.PI * 2;
        const double halfCone = Math.PI * 0.35;
        var minRotation = (hRotation + doublePI - halfCone) % doublePI;
        var maxRotation = (hRotation + halfCone) % doublePI;

        static bool IsBetween(double val, double a, double b)
        {
            if (a > b)
                return val >= a || val <= b;
            return val >= a && val <= b;
        }

        Dalamud.Game.ClientState.Objects.Types.GameObject closest = null;
        foreach (var o in DalamudApi.ObjectTable.Where(o => o.YalmDistanceX < 30
            && o.ObjectKind is ObjectKind.Player or ObjectKind.BattleNpc
            && ((BattleChara)o).CurrentHp > 0
            && ActionManager.CanUseActionOnGameObject(7, (GameObject*)o.Address)))
        {
            var posDiff = o.Position - p.Position;
            var angle = Math.Atan2(-posDiff.Z, posDiff.X) + Math.PI;
            if (IsBetween(angle, minRotation, maxRotation) && (closest == null || closest.YalmDistanceX > o.YalmDistanceX))
                closest = o;
        }

        if (closest != null)
            DalamudApi.TargetManager.Target = closest;
    }

    private static void PostActionStack(ActionManager* actionManager, uint actionType, uint actionID, uint adjustedActionID, ref long targetObjectID, uint param, uint useType, int pvp)
    {
        if (actionType != 1) return;

        var targetObject = DalamudApi.TargetManager.Target is { } t ? (GameObject*)t.Address : null;
        if (!ReAction.Config.EnableAutoChangeTarget && targetObject != null
            || targetObjectID != Game.InvalidObjectID && Game.GetGameObjectFromObjectID(targetObjectID) != targetObject
            || ActionManager.CanUseActionOnGameObject(adjustedActionID, targetObject)
            || !ReAction.actionSheet.TryGetValue(adjustedActionID, out var a)
            || !a.CanTargetHostile)
            return;

        PluginLog.Debug($"Attempting to swap target {adjustedActionID}, {targetObjectID:X}");

        TargetEnemy();
        if (DalamudApi.TargetManager.Target is not { } target) return;

        var prevTargetObjectID = targetObjectID;
        targetObjectID = Game.GetObjectID((GameObject*)target.Address);

        PluginLog.Debug($"Target swapped {prevTargetObjectID:X} -> {targetObjectID:X}");
    }
}