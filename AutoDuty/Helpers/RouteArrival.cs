using System;
using System.Numerics;

namespace AutoDuty.Helpers;

internal static class RouteArrival
{
    internal const float WaypointTolerance = 0.25f;
    internal static bool HasArrived(string action, Vector3 target, Vector3 position)
    {
        Vector3 delta = target - position;
        return string.Equals(action, "Interactable", StringComparison.OrdinalIgnoreCase)
            ? delta.X * delta.X + delta.Z * delta.Z <= 4f && MathF.Abs(delta.Y) <= 4f
            : delta.LengthSquared() <= WaypointTolerance * WaypointTolerance;
    }
}
