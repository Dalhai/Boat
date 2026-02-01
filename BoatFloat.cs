using Godot;
using System;

public partial class BoatFloat : Node2D
{
    [Export] public NodePath FluidPath { get; set; }
    [Export] public int SampleCount { get; set; } = 5;
    [Export] public int SampleRows { get; set; } = 3;
    [Export] public float SampleWidth { get; set; } = 240.0f;
    [Export] public float SampleDepth { get; set; } = 20.0f;
    [Export] public float SampleRadius { get; set; } = 18.0f;

    [Export] public float Gravity { get; set; } = 600.0f;
    [Export] public float BoatMass { get; set; } = 8.0f;
    [Export] public float AngularInertia { get; set; } = 36000.0f;
    [Export] public float InteractionStiffness { get; set; } = 220.0f;
    [Export] public float InteractionDamping { get; set; } = 10.0f;
    [Export] public float BuoyancyScale { get; set; } = 1.6f;
    [Export] public float ParticlePushStrength { get; set; } = 0.55f;
    [Export] public float SurfaceSearchRadius { get; set; } = 120.0f;
    [Export] public float BuoyancyPerDepth { get; set; } = 280.0f;
    [Export] public float MaxBuoyancyPerSample { get; set; } = 2200.0f;
    [Export] public float LinearDamping { get; set; } = 1.25f;
    [Export] public float WaterDrag { get; set; } = 2.25f;
    [Export] public float AngularDamping { get; set; } = 2.0f;
    [Export] public float SafeSpawnMargin { get; set; } = 40.0f;

    private Vector2 _velocity = Vector2.Zero;
    private float _angularVelocity = 0.0f;
    private FluidSim _fluid;
    private Vector2[] _samplePoints = Array.Empty<Vector2>();
    private Vector2[] _sampleVelocities = Array.Empty<Vector2>();
    private Vector2[] _sampleOffsets = Array.Empty<Vector2>();

    public override void _Ready()
    {
        ProcessPriority = 1;

        if (FluidPath != null && !FluidPath.IsEmpty)
        {
            _fluid = GetNodeOrNull<FluidSim>(FluidPath);
        }

        _fluid ??= GetTree().CurrentScene?.GetNodeOrNull<FluidSim>("FluidSim");
        EnsureSafeSpawn();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_fluid == null || SampleCount <= 0)
        {
            return;
        }

        float dt = (float)delta;
        Vector2 totalForce = new Vector2(0.0f, Gravity * BoatMass);
        float totalTorque = 0.0f;

        int rowCount = Math.Max(1, SampleRows);
        int columnCount = Math.Max(1, SampleCount);
        int totalSamples = rowCount * columnCount;
        EnsureSampleBuffers(totalSamples);
        float halfWidth = SampleWidth * 0.5f;
        int sampleIndex = 0;
        for (int col = 0; col < columnCount; col++)
        {
            float t = columnCount == 1 ? 0.5f : col / (float)(columnCount - 1);
            float localX = Mathf.Lerp(-halfWidth, halfWidth, t);
            for (int row = 0; row < rowCount; row++)
            {
                float rowT = rowCount == 1 ? 1.0f : row / (float)(rowCount - 1);
                float localY = Mathf.Lerp(0.0f, SampleDepth, rowT);
                Vector2 localPoint = new Vector2(localX, localY);
                Vector2 worldPoint = GlobalTransform * localPoint;
                Vector2 localPerp = new Vector2(-localPoint.Y, localPoint.X);
                Vector2 angularVelocity = GlobalTransform.BasisXform(localPerp) * _angularVelocity;
                Vector2 pointVelocity = _velocity + angularVelocity;
                Vector2 worldOffset = worldPoint - GlobalPosition;

                _samplePoints[sampleIndex] = worldPoint;
                _sampleVelocities[sampleIndex] = pointVelocity;
                _sampleOffsets[sampleIndex] = worldOffset;

                if (_fluid.TryGetSurfaceY(worldPoint.X, SurfaceSearchRadius, out float surfaceY))
                {
                    float depth = worldPoint.Y - surfaceY + SampleRadius;
                    if (depth > 0.0f)
                    {
                        float buoyancy = Math.Min(depth * BuoyancyPerDepth, MaxBuoyancyPerSample);
                        totalForce += new Vector2(0.0f, -buoyancy);
                    }
                }

                sampleIndex++;
            }
        }

        _fluid.ApplyBoatSamples(
            _samplePoints,
            _sampleVelocities,
            _sampleOffsets,
            SampleRadius,
            InteractionStiffness,
            InteractionDamping,
            BuoyancyScale,
            ParticlePushStrength,
            dt,
            out Vector2 fluidForce,
            out float fluidTorque
        );

        totalForce += fluidForce;
        totalTorque += fluidTorque;

        Vector2 acceleration = totalForce / Math.Max(0.001f, BoatMass);
        _velocity += acceleration * dt;
        bool submerged = fluidForce.LengthSquared() > 0.0001f;
        float damping = submerged ? WaterDrag : LinearDamping;
        _velocity *= MathF.Exp(-damping * dt);
        GlobalPosition += _velocity * dt;

        float inertia = Math.Max(0.001f, AngularInertia);
        _angularVelocity += (totalTorque / inertia) * dt;
        _angularVelocity *= MathF.Exp(-AngularDamping * dt);
        Rotation += _angularVelocity * dt;
    }

    private void EnsureSampleBuffers(int count)
    {
        count = Math.Max(1, count);
        if (_samplePoints.Length == count)
        {
            return;
        }

        _samplePoints = new Vector2[count];
        _sampleVelocities = new Vector2[count];
        _sampleOffsets = new Vector2[count];
    }

    private void EnsureSafeSpawn()
    {
        if (_fluid == null)
        {
            return;
        }

        if (!_fluid.TryGetParticleMinY(out float minY))
        {
            return;
        }

        float safeY = minY - (SampleDepth + SampleRadius + SafeSpawnMargin);
        Vector2 pos = GlobalPosition;
        if (pos.Y > safeY)
        {
            pos.Y = safeY;
            GlobalPosition = pos;
            _velocity = Vector2.Zero;
            _angularVelocity = 0.0f;
        }
    }
}
