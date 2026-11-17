using System.Numerics;
using AutoDuty.Helpers;
Check(RouteArrival.HasArrived("Interactable", new(1.5f, 3, 0), Vector3.Zero), "sloped interaction accepted within horizontal range and height guard");
Check(!RouteArrival.HasArrived("Interactable", new(1, 6, 0), Vector3.Zero), "different floor rejected");
Check(!RouteArrival.HasArrived("Interactable", new(3, 0, 0), Vector3.Zero), "outside interaction range rejected");
Check(RouteArrival.HasArrived("MoveTo", new(.2f, 0, 0), Vector3.Zero), "precise movement remains accepted");
Check(!RouteArrival.HasArrived("MoveTo", new(1.5f, 0, 0), Vector3.Zero), "interaction range never broadens MoveTo");
Check(!RouteArrival.HasArrived("MoveTo", new(0, 1, 0), Vector3.Zero), "precise movement retains vertical distance");
static void Check(bool value, string label) { if (!value) throw new Exception(label); Console.WriteLine("PASS: " + label); }
