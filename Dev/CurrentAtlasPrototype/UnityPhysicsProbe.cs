using System;
using System.Globalization;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UBOAT.Mods.RadioNavigation;

// Executed only in a disposable Unity project. No game assemblies or saves.
public static class UnityPhysicsProbe
{
    private const float Step = 0.02f;
    private const int Steps = 150;
    private const float QuadraticPerMeter = 0.5f;

    public static void Run()
    {
        bool automaticSimulation = Physics.autoSimulation;
        try
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Physics.autoSimulation = false;
            Rigidbody baseline = MakeBody(false);
            Rigidbody still = MakeBody(false);
            Rigidbody east = MakeBody(false);
            Rigidbody west = MakeBody(false);
            Rigidbody held = MakeBody(true);
            CurrentVelocity calm = default(CurrentVelocity);
            CurrentVelocity eastward = new ConstantCurrentField(2, 0).Sample(0, 0, 5, DateTime.UnixEpoch);
            CurrentVelocity westward = new ConstantCurrentField(-2, 0).Sample(0, 0, 5, DateTime.UnixEpoch);

            for (int i = 0; i < Steps; i++)
            {
                Advance(still, calm);
                Advance(east, eastward);
                Advance(west, westward);
                Advance(held, eastward);
                Physics.Simulate(Step);
            }

            // Analytic terminal velocity and bounds for right-endpoint position integration.
            Check(still.position == baseline.position && still.velocity == baseline.velocity, "still baseline");
            Check(Mathf.Abs(east.velocity.x - 1.5f) < 1e-4f, "east terminal speed");
            Check(east.position.x >= 3.2274f && east.position.x <= 3.2575f, "east trajectory");
            Check(Mathf.Abs(west.position.x + east.position.x) < 1e-4f, "west sign");
            Check(east.position.y == 0f && east.position.z == 0f, "no unintended axes");
            Check(held.position == Vector3.zero, "held body");
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "CURRENT_PHYSX_PASS steps={0} east_m={1:F6} east_mps={2:F6} west_m={3:F6} held_m={4:F6}",
                Steps, east.position.x, east.velocity.x, west.position.x, held.position.x));
        }
        catch (Exception error)
        {
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
        finally
        {
            Physics.autoSimulation = automaticSimulation;
        }
    }

    private static Rigidbody MakeBody(bool held)
    {
        var obj = new GameObject();
        var body = obj.AddComponent<Rigidbody>();
        body.useGravity = false;
        body.isKinematic = held;
        return body;
    }

    private static void Advance(Rigidbody body, CurrentVelocity water)
    {
        if (!body.isKinematic)
            body.velocity = CurrentWaterDrag.Advance(body.velocity, water, 0, QuadraticPerMeter, Step);
    }

    private static void Check(bool passed, string scenario)
    {
        if (!passed)
            throw new InvalidOperationException("Current PhysX check failed: " + scenario);
    }
}
