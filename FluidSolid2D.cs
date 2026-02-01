using Godot;
using System;
using System.Collections.Generic;

public partial class FluidSolid2D : Node2D
{
    [Export] public NodePath FluidPath { get; set; }
    [Export] public NodePath ShapePath { get; set; }
    [Export] public bool UsePhysicsBody { get; set; } = true;

    [Export] public float SampleSpacing { get; set; } = 14.0f;
    [Export] public float SampleRadius { get; set; } = 18.0f;
    [Export] public float SampleInset { get; set; } = 2.0f;

    [Export] public float InteractionStiffness { get; set; } = 260.0f;
    [Export] public float InteractionDamping { get; set; } = 12.0f;
    [Export] public float ForceScale { get; set; } = 2.0f;
    [Export] public float ParticlePushStrength { get; set; } = 0.65f;
    [Export] public float MaxForcePerSample { get; set; } = 800.0f;
    [Export] public float MaxTotalForce { get; set; } = 6000.0f;
    [Export] public float MaxTotalTorque { get; set; } = 15000.0f;
    [Export] public float BuoyancyPerPenetration { get; set; } = 55.0f;
    [Export] public float WaterDragPerSample { get; set; } = 8.0f;
    [Export] public float MaxDownwardSpeed { get; set; } = 350.0f;

    [Export] public float ManualGravity { get; set; } = 600.0f;
    [Export] public float ManualMass { get; set; } = 8.0f;
    [Export] public float ManualInertia { get; set; } = 36000.0f;
    [Export] public float ManualLinearDamping { get; set; } = 1.25f;
    [Export] public float ManualAngularDamping { get; set; } = 2.0f;
    [Export] public float StartDelay { get; set; } = 1.25f;
    [Export] public bool ConstrainToFluidBounds { get; set; } = true;
    [Export] public float BoundsMargin { get; set; } = 6.0f;

    private FluidSim _fluid;
    private CollisionPolygon2D _collisionPolygon;
    private Polygon2D _polygonVisual;
    private readonly List<Vector2> _localSamples = new();
    private readonly List<Vector2> _localNormals = new();
    private Vector2[] _samplePoints = Array.Empty<Vector2>();
    private Vector2[] _sampleNormals = Array.Empty<Vector2>();
    private Vector2[] _sampleVelocities = Array.Empty<Vector2>();
    private Vector2[] _sampleOffsets = Array.Empty<Vector2>();
    private Vector2 _manualVelocity = Vector2.Zero;
    private float _manualAngularVelocity = 0.0f;
    private float _elapsed = 0.0f;
    private bool _activated = false;
    private bool _freezeInitialized = false;

    public override void _Ready()
    {
        ProcessPriority = 1;

        if (FluidPath != null && !FluidPath.IsEmpty)
        {
            _fluid = GetNodeOrNull<FluidSim>(FluidPath);
        }

        _fluid ??= GetTree().CurrentScene?.GetNodeOrNull<FluidSim>("FluidSim");

        if (ShapePath != null && !ShapePath.IsEmpty)
        {
            Node shapeNode = GetNodeOrNull(ShapePath);
            _collisionPolygon = shapeNode as CollisionPolygon2D;
            _polygonVisual = shapeNode as Polygon2D;
        }

        if (_collisionPolygon == null)
        {
            _collisionPolygon = FindChildOfType<CollisionPolygon2D>(this);
        }

        if (_polygonVisual == null)
        {
            _polygonVisual = FindChildOfType<Polygon2D>(this);
        }

        RebuildSamples();
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;
        _elapsed += dt;
        if (!_activated && StartDelay > 0.0f && _elapsed < StartDelay)
        {
            if (UsePhysicsBody && GetPhysicsBody(out RigidBody2D waitBody))
            {
                if (!_freezeInitialized)
                {
                    waitBody.FreezeMode = RigidBody2D.FreezeModeEnum.Kinematic;
                    _freezeInitialized = true;
                }
                waitBody.Freeze = true;
                waitBody.LinearVelocity = Vector2.Zero;
                waitBody.AngularVelocity = 0.0f;
            }
            return;
        }
        if (!_activated)
        {
            _activated = true;
            if (UsePhysicsBody && GetPhysicsBody(out RigidBody2D wakeBody))
            {
                wakeBody.Freeze = false;
            }
        }

        if (_localSamples.Count == 0)
        {
            RebuildSamples();
        }
        if (_fluid == null || _localSamples.Count == 0)
        {
            if (UsePhysicsBody && GetPhysicsBody(out RigidBody2D clampBody))
            {
                ConstrainToBounds(clampBody);
            }
            else
            {
                ConstrainToBounds(null);
            }
            return;
        }
        EnsureSampleBuffers(_localSamples.Count);

        Vector2 linearVelocity;
        float angularVelocity;
        Vector2 bodyPosition;
        if (UsePhysicsBody && GetPhysicsBody(out RigidBody2D body))
        {
            linearVelocity = body.LinearVelocity;
            angularVelocity = body.AngularVelocity;
            bodyPosition = body.GlobalPosition;
        }
        else
        {
            linearVelocity = _manualVelocity;
            angularVelocity = _manualAngularVelocity;
            bodyPosition = GlobalPosition;
        }

        for (int i = 0; i < _localSamples.Count; i++)
        {
            Vector2 localPoint = _localSamples[i];
            Vector2 localNormal = _localNormals[i];
            Vector2 worldPoint = GlobalTransform * localPoint;
            Vector2 worldNormal = GlobalTransform.BasisXform(localNormal).Normalized();
            Vector2 offset = worldPoint - bodyPosition;
            Vector2 angularVel = new Vector2(-offset.Y, offset.X) * angularVelocity;

            _samplePoints[i] = worldPoint;
            _sampleNormals[i] = worldNormal;
            _sampleVelocities[i] = linearVelocity + angularVel;
            _sampleOffsets[i] = offset;
        }

        _fluid.ApplySolidSamples(
            _samplePoints,
            _sampleNormals,
            _sampleVelocities,
            _sampleOffsets,
            SampleRadius,
            InteractionStiffness,
            InteractionDamping,
            ForceScale,
            ParticlePushStrength,
            MaxForcePerSample,
            dt,
            out Vector2 fluidForce,
            out float fluidTorque,
            out int hitSamples,
            out float totalPenetration
        );

        if (BuoyancyPerPenetration > 0.0f && totalPenetration > 0.0f)
        {
            fluidForce += new Vector2(0.0f, -totalPenetration * BuoyancyPerPenetration);
        }
        if (WaterDragPerSample > 0.0f && hitSamples > 0)
        {
            fluidForce += -linearVelocity * (WaterDragPerSample * hitSamples);
        }

        if (MaxTotalForce > 0.0f && fluidForce.LengthSquared() > MaxTotalForce * MaxTotalForce)
        {
            fluidForce = fluidForce.Normalized() * MaxTotalForce;
        }
        if (MaxTotalTorque > 0.0f)
        {
            fluidTorque = Mathf.Clamp(fluidTorque, -MaxTotalTorque, MaxTotalTorque);
        }

        if (UsePhysicsBody && GetPhysicsBody(out RigidBody2D rigidBody))
        {
            rigidBody.ApplyForce(fluidForce);
            rigidBody.ApplyTorque(fluidTorque);
            if (MaxDownwardSpeed > 0.0f && hitSamples > 0)
            {
                Vector2 currentVelocity = rigidBody.LinearVelocity;
                if (currentVelocity.Y > MaxDownwardSpeed)
                {
                    currentVelocity.Y = MaxDownwardSpeed;
                    rigidBody.LinearVelocity = currentVelocity;
                }
            }
            ConstrainToBounds(rigidBody);
        }
        else
        {
            Vector2 totalForce = fluidForce + new Vector2(0.0f, ManualGravity * ManualMass);
            Vector2 acceleration = totalForce / Math.Max(0.001f, ManualMass);
            _manualVelocity += acceleration * dt;
            _manualVelocity *= MathF.Exp(-ManualLinearDamping * dt);
            if (MaxDownwardSpeed > 0.0f && hitSamples > 0 && _manualVelocity.Y > MaxDownwardSpeed)
            {
                _manualVelocity.Y = MaxDownwardSpeed;
            }
            GlobalPosition += _manualVelocity * dt;
            ConstrainToBounds(null);

            float inertia = Math.Max(0.001f, ManualInertia);
            _manualAngularVelocity += (fluidTorque / inertia) * dt;
            _manualAngularVelocity *= MathF.Exp(-ManualAngularDamping * dt);
            Rotation += _manualAngularVelocity * dt;
        }
    }

    public void RebuildSamples()
    {
        _localSamples.Clear();
        _localNormals.Clear();

        Vector2[] polygon = GetSourcePolygon();
        if (polygon == null || polygon.Length < 3)
        {
            return;
        }

        float signedArea = 0.0f;
        for (int i = 0; i < polygon.Length; i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[(i + 1) % polygon.Length];
            signedArea += (a.X * b.Y) - (b.X * a.Y);
        }

        bool isCCW = signedArea > 0.0f;
        float spacing = Math.Max(2.0f, SampleSpacing);
        float inset = SampleInset;

        for (int i = 0; i < polygon.Length; i++)
        {
            Vector2 a = polygon[i];
            Vector2 b = polygon[(i + 1) % polygon.Length];
            Vector2 edge = b - a;
            float length = edge.Length();
            if (length < 0.001f)
            {
                continue;
            }

            Vector2 dir = edge / length;
            Vector2 normal = isCCW ? new Vector2(dir.Y, -dir.X) : new Vector2(-dir.Y, dir.X);
            int steps = Math.Max(1, (int)MathF.Floor(length / spacing));
            for (int s = 0; s <= steps; s++)
            {
                float t = steps == 0 ? 0.0f : s / (float)steps;
                Vector2 localPoint = a + dir * (length * t);
                _localSamples.Add(localPoint - normal * inset);
                _localNormals.Add(normal);
            }
        }
    }

    private Vector2[] GetSourcePolygon()
    {
        if (_collisionPolygon != null)
        {
            return _collisionPolygon.Polygon;
        }

        if (_polygonVisual != null)
        {
            return _polygonVisual.Polygon;
        }

        return null;
    }

    private void EnsureSampleBuffers(int count)
    {
        if (_samplePoints.Length == count)
        {
            return;
        }

        _samplePoints = new Vector2[count];
        _sampleNormals = new Vector2[count];
        _sampleVelocities = new Vector2[count];
        _sampleOffsets = new Vector2[count];
    }

    private bool GetPhysicsBody(out RigidBody2D body)
    {
        if (Owner is RigidBody2D ownerBody)
        {
            body = ownerBody;
            return true;
        }

        if (GetParent() is RigidBody2D parentBody)
        {
            body = parentBody;
            return true;
        }

        body = null;
        return false;
    }

    private static T FindChildOfType<T>(Node root) where T : Node
    {
        foreach (Node child in root.GetChildren())
        {
            if (child is T match)
            {
                return match;
            }

            T nested = FindChildOfType<T>(child);
            if (nested != null)
            {
                return nested;
            }
        }

        return null;
    }

    private void ConstrainToBounds(RigidBody2D rigidBody)
    {
        if (!ConstrainToFluidBounds || _fluid == null)
        {
            return;
        }

        Rect2 bounds = _fluid != null ? _fluid.GetBounds() : GetViewportRect();
        if (bounds.Size == Vector2.Zero)
        {
            return;
        }

        float margin = Math.Max(0.0f, BoundsMargin);
        float left = bounds.Position.X + margin;
        float right = bounds.Position.X + bounds.Size.X - margin;
        float top = bounds.Position.Y + margin;
        float bottom = bounds.Position.Y + bounds.Size.Y - margin;

        Vector2 pos = rigidBody != null ? rigidBody.GlobalPosition : GlobalPosition;
        bool clamped = false;
        if (pos.X < left)
        {
            pos.X = left;
            clamped = true;
        }
        else if (pos.X > right)
        {
            pos.X = right;
            clamped = true;
        }

        if (pos.Y < top)
        {
            pos.Y = top;
            clamped = true;
        }
        else if (pos.Y > bottom)
        {
            pos.Y = bottom;
            clamped = true;
        }

        if (!clamped)
        {
            return;
        }

        if (rigidBody != null)
        {
            rigidBody.GlobalPosition = pos;
            Vector2 velocity = rigidBody.LinearVelocity;
            if ((pos.X == left && velocity.X < 0.0f) || (pos.X == right && velocity.X > 0.0f))
            {
                velocity.X = 0.0f;
            }
            if ((pos.Y == top && velocity.Y < 0.0f) || (pos.Y == bottom && velocity.Y > 0.0f))
            {
                velocity.Y = 0.0f;
            }
            rigidBody.LinearVelocity = velocity;
            rigidBody.AngularVelocity *= 0.5f;
        }
        else
        {
            GlobalPosition = pos;
            if (pos.X == left || pos.X == right)
            {
                _manualVelocity.X = 0.0f;
            }
            if (pos.Y == top || pos.Y == bottom)
            {
                _manualVelocity.Y = 0.0f;
            }
            _manualAngularVelocity *= 0.5f;
        }
    }
}
