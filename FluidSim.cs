using Godot;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

public partial class FluidSim : Node2D
{
    [Export] public int ParticleCountX { get; set; } = 100;
    [Export] public int ParticleCountY { get; set; } = 20;
    [Export] public float ParticleSpacing { get; set; } = 10.0f;
    [Export] public float ParticleRadius { get; set; } = 4.0f;
    [Export] public Color ParticleColor { get; set; } = new Color(0.2f, 0.6f, 0.95f, 0.9f);

    [Export] public float SmoothingRadius { get; set; } = 18.0f;
    [Export] public float ParticleMass { get; set; } = 1.0f;
    [Export] public float RestDensity { get; set; } = 18.0f;
    [Export] public float GasConstant { get; set; } = 220.0f;
    [Export] public float Viscosity { get; set; } = 40.0f;
    [Export] public Vector2 Gravity { get; set; } = new Vector2(0.0f, 450.0f);
    [Export] public float BoundaryDamping { get; set; } = 0.35f;
    [Export] public int Substeps { get; set; } = 2;
    [Export] public bool EnableParticleCollisions { get; set; } = true;
    [Export] public float CollisionRestitution { get; set; } = 0.15f;
    [Export] public int CollisionEveryNSubsteps { get; set; } = 1;
    [Export] public bool EnableParallel { get; set; } = true;
    [Export] public int ParallelMinParticles { get; set; } = 256;

    [Export] public Vector2 BoundsCenter { get; set; } = Vector2.Zero;
    [Export] public Vector2 BoundsSize { get; set; } = Vector2.Zero;
    [Export] public float BoundsMargin { get; set; } = 40.0f;
    [Export] public bool DrawBounds { get; set; } = true;

    private Vector2[] _positions = Array.Empty<Vector2>();
    private Vector2[] _velocities = Array.Empty<Vector2>();
    private float[] _densities = Array.Empty<float>();
    private float[] _pressures = Array.Empty<float>();
    private int _particleCount = 0;
    private readonly Dictionary<long, List<int>> _grid = new();
    private readonly List<List<int>> _gridLists = new();
    private int _gridListsUsed = 0;
    private Rect2 _bounds;
    private float _cachedSmoothingRadius = -1.0f;
    private float _cachedH2;
    private float _cachedPoly6;
    private float _cachedSpikyGrad;
    private float _cachedViscLap;
    private MultiMeshInstance2D _particleMeshInstance;
    private MultiMesh _particleMultiMesh;
    private float _cachedRenderRadius = -1.0f;
    private Color _cachedParticleColor = new Color(-1, -1, -1, -1);

    public override void _Ready()
    {
        UpdateBounds();
        SetupParticleRenderer();
        SpawnParticles();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_particleCount == 0)
        {
            return;
        }

        float step = (float)delta / Math.Max(1, Substeps);
        for (int i = 0; i < Substeps; i++)
        {
            StepSimulation(step, i);
        }

        UpdateParticleMeshTransforms();
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (!DrawBounds)
        {
            return;
        }

        DrawRect(_bounds, new Color(0.12f, 0.2f, 0.26f, 0.35f), false, 2.0f);
    }

    private void UpdateBounds()
    {
        Vector2 size = BoundsSize;
        if (size == Vector2.Zero)
        {
            Vector2 viewportSize = GetViewportRect().Size;
            size = new Vector2(
                Math.Max(100.0f, viewportSize.X - BoundsMargin * 2.0f),
                Math.Max(100.0f, viewportSize.Y - BoundsMargin * 2.0f)
            );
        }

        Vector2 center = BoundsCenter;
        if (center == Vector2.Zero)
        {
            center = GetViewportRect().Size * 0.5f;
        }

        _bounds = new Rect2(center - size * 0.5f, size);
    }

    private void SpawnParticles()
    {
        float padding = SmoothingRadius * 0.6f;
        Vector2 start = _bounds.Position + new Vector2(padding, padding);
        _particleCount = Math.Max(0, ParticleCountX * ParticleCountY);
        EnsureParticleCapacity(_particleCount);

        int index = 0;
        for (int y = 0; y < ParticleCountY; y++)
        {
            for (int x = 0; x < ParticleCountX; x++)
            {
                _positions[index] = start + new Vector2(x * ParticleSpacing, y * ParticleSpacing);
                _velocities[index] = Vector2.Zero;
                _densities[index] = RestDensity;
                _pressures[index] = 0.0f;
                index++;
            }
        }

        SyncParticleMeshCapacity();
    }

    private void StepSimulation(float delta, int substepIndex)
    {
        float h = SmoothingRadius;
        if (!Mathf.IsEqualApprox(h, _cachedSmoothingRadius))
        {
            _cachedSmoothingRadius = h;
            _cachedH2 = h * h;
            _cachedPoly6 = 4.0f / (Mathf.Pi * Mathf.Pow(h, 8));
            _cachedSpikyGrad = -30.0f / (Mathf.Pi * Mathf.Pow(h, 5));
            _cachedViscLap = 20.0f / (3.0f * Mathf.Pi * Mathf.Pow(h, 5));
        }

        float h2 = _cachedH2;
        float poly6 = _cachedPoly6;
        float spikyGrad = _cachedSpikyGrad;
        float viscLap = _cachedViscLap;

        BuildGrid(h);
        bool useParallel = EnableParallel && _particleCount >= ParallelMinParticles;
        if (useParallel)
        {
            Parallel.For(0, _particleCount, i =>
            {
                float density = 0.0f;
                Vector2 piPosition = _positions[i];

                Vector2I cell = GetCell(piPosition, h);
                for (int y = -1; y <= 1; y++)
                {
                    for (int x = -1; x <= 1; x++)
                    {
                        long key = EncodeCell(cell.X + x, cell.Y + y);
                        if (!_grid.TryGetValue(key, out List<int> list))
                        {
                            continue;
                        }

                        for (int n = 0; n < list.Count; n++)
                        {
                            int j = list[n];
                            Vector2 rij = piPosition - _positions[j];
                            float r2 = rij.LengthSquared();
                            if (r2 < h2)
                            {
                                float diff = h2 - r2;
                                density += ParticleMass * poly6 * diff * diff * diff;
                            }
                        }
                    }
                }

                float safeDensity = Math.Max(density, RestDensity * 0.5f);
                _densities[i] = safeDensity;
                _pressures[i] = GasConstant * (safeDensity - RestDensity);
            });

            Parallel.For(0, _particleCount, i =>
            {
                Vector2 pressureForce = Vector2.Zero;
                Vector2 viscosityForce = Vector2.Zero;
                Vector2 piPosition = _positions[i];
                Vector2 piVelocity = _velocities[i];
                float piPressure = _pressures[i];
                float piDensity = _densities[i];

                Vector2I cell = GetCell(piPosition, h);
                for (int y = -1; y <= 1; y++)
                {
                    for (int x = -1; x <= 1; x++)
                    {
                        long key = EncodeCell(cell.X + x, cell.Y + y);
                        if (!_grid.TryGetValue(key, out List<int> list))
                        {
                            continue;
                        }

                        for (int n = 0; n < list.Count; n++)
                        {
                            int j = list[n];
                            if (i == j)
                            {
                                continue;
                            }

                            Vector2 pjPosition = _positions[j];
                            Vector2 rij = piPosition - pjPosition;
                            float r2 = rij.LengthSquared();
                            if (r2 < h2 && r2 > 0.000001f)
                            {
                                float r = Mathf.Sqrt(r2);
                                float diff = h - r;
                                Vector2 grad = spikyGrad * diff * diff * (rij / r);
                                float pressureTerm = (piPressure + _pressures[j]) / (2.0f * _densities[j]);
                                pressureForce += -ParticleMass * pressureTerm * grad;

                                Vector2 velDiff = _velocities[j] - piVelocity;
                                float lap = viscLap * diff;
                                viscosityForce += Viscosity * ParticleMass * velDiff / _densities[j] * lap;
                            }
                        }
                    }
                }

                Vector2 gravityForce = Gravity * piDensity;
                Vector2 totalForce = pressureForce + viscosityForce + gravityForce;
                Vector2 acceleration = totalForce / piDensity;
                piVelocity += acceleration * delta;
                piPosition += piVelocity * delta;
                ResolveBounds(ref piPosition, ref piVelocity);
                _velocities[i] = piVelocity;
                _positions[i] = piPosition;
            });
        }
        else
        {
            for (int i = 0; i < _particleCount; i++)
            {
                float density = 0.0f;
                Vector2 piPosition = _positions[i];

                Vector2I cell = GetCell(piPosition, h);
                for (int y = -1; y <= 1; y++)
                {
                    for (int x = -1; x <= 1; x++)
                    {
                        long key = EncodeCell(cell.X + x, cell.Y + y);
                        if (!_grid.TryGetValue(key, out List<int> list))
                        {
                            continue;
                        }

                        for (int n = 0; n < list.Count; n++)
                        {
                            int j = list[n];
                            Vector2 rij = piPosition - _positions[j];
                            float r2 = rij.LengthSquared();
                            if (r2 < h2)
                            {
                                float diff = h2 - r2;
                                density += ParticleMass * poly6 * diff * diff * diff;
                            }
                        }
                    }
                }

                float safeDensity = Math.Max(density, RestDensity * 0.5f);
                _densities[i] = safeDensity;
                _pressures[i] = GasConstant * (safeDensity - RestDensity);
            }

            for (int i = 0; i < _particleCount; i++)
            {
                Vector2 pressureForce = Vector2.Zero;
                Vector2 viscosityForce = Vector2.Zero;
                Vector2 piPosition = _positions[i];
                Vector2 piVelocity = _velocities[i];
                float piPressure = _pressures[i];
                float piDensity = _densities[i];

                Vector2I cell = GetCell(piPosition, h);
                for (int y = -1; y <= 1; y++)
                {
                    for (int x = -1; x <= 1; x++)
                    {
                        long key = EncodeCell(cell.X + x, cell.Y + y);
                        if (!_grid.TryGetValue(key, out List<int> list))
                        {
                            continue;
                        }

                        for (int n = 0; n < list.Count; n++)
                        {
                            int j = list[n];
                            if (i == j)
                            {
                                continue;
                            }

                            Vector2 pjPosition = _positions[j];
                            Vector2 rij = piPosition - pjPosition;
                            float r2 = rij.LengthSquared();
                            if (r2 < h2 && r2 > 0.000001f)
                            {
                                float r = Mathf.Sqrt(r2);
                                float diff = h - r;
                                Vector2 grad = spikyGrad * diff * diff * (rij / r);
                                float pressureTerm = (piPressure + _pressures[j]) / (2.0f * _densities[j]);
                                pressureForce += -ParticleMass * pressureTerm * grad;

                                Vector2 velDiff = _velocities[j] - piVelocity;
                                float lap = viscLap * diff;
                                viscosityForce += Viscosity * ParticleMass * velDiff / _densities[j] * lap;
                            }
                        }
                    }
                }

                Vector2 gravityForce = Gravity * piDensity;
                Vector2 totalForce = pressureForce + viscosityForce + gravityForce;
                Vector2 acceleration = totalForce / piDensity;
                piVelocity += acceleration * delta;
                piPosition += piVelocity * delta;
                ResolveBounds(ref piPosition, ref piVelocity);
                _velocities[i] = piVelocity;
                _positions[i] = piPosition;
            }
        }

        if (EnableParticleCollisions)
        {
            int interval = Math.Max(1, CollisionEveryNSubsteps);
            if (substepIndex % interval == 0)
            {
                ResolveParticleCollisions();
            }
        }

        UpdateParticleMeshTransforms();
    }

    private void ResolveBounds(ref Vector2 position, ref Vector2 velocity)
    {
        float left = _bounds.Position.X + ParticleRadius;
        float right = _bounds.Position.X + _bounds.Size.X - ParticleRadius;
        float top = _bounds.Position.Y + ParticleRadius;
        float bottom = _bounds.Position.Y + _bounds.Size.Y - ParticleRadius;

        if (position.X < left)
        {
            position.X = left;
            velocity.X *= -BoundaryDamping;
        }
        else if (position.X > right)
        {
            position.X = right;
            velocity.X *= -BoundaryDamping;
        }

        if (position.Y < top)
        {
            position.Y = top;
            velocity.Y *= -BoundaryDamping;
        }
        else if (position.Y > bottom)
        {
            position.Y = bottom;
            velocity.Y *= -BoundaryDamping;
        }

    }

    public bool TryGetSurfaceY(float worldX, float maxDistance, out float surfaceY)
    {
        float best = float.MaxValue;
        bool found = false;
        for (int i = 0; i < _particleCount; i++)
        {
            Vector2 position = _positions[i];
            float dx = MathF.Abs(position.X - worldX);
            if (dx > maxDistance)
            {
                continue;
            }

            float candidate = position.Y - ParticleRadius;
            if (candidate < best)
            {
                best = candidate;
                found = true;
            }
        }

        surfaceY = best;
        return found;
    }

    public bool TryGetParticleMinY(out float minY)
    {
        if (_particleCount == 0)
        {
            minY = 0.0f;
            return false;
        }

        minY = _positions[0].Y - ParticleRadius;
        for (int i = 1; i < _particleCount; i++)
        {
            float candidate = _positions[i].Y - ParticleRadius;
            if (candidate < minY)
            {
                minY = candidate;
            }
        }

        return true;
    }

    public void ApplyBoatSamples(
        Vector2[] samplePoints,
        Vector2[] sampleVelocities,
        Vector2[] sampleOffsets,
        float radius,
        float stiffness,
        float damping,
        float forceScale,
        float positionPush,
        float delta,
        out Vector2 totalForce,
        out float totalTorque
    )
    {
        totalForce = Vector2.Zero;
        totalTorque = 0.0f;

        if (_particleCount == 0 || radius <= 0.0f || samplePoints == null || samplePoints.Length == 0)
        {
            return;
        }

        BuildGrid(radius);
        float radius2 = radius * radius;
        float clampedForceScale = Math.Max(0.0f, forceScale);
        float clampedPush = Mathf.Clamp(positionPush, 0.0f, 1.0f);

        for (int s = 0; s < samplePoints.Length; s++)
        {
            Vector2 point = samplePoints[s];
            Vector2 pointVelocity = sampleVelocities[s];
            Vector2 offset = sampleOffsets[s];
            Vector2 sampleForce = Vector2.Zero;

            Vector2I cell = GetCell(point, radius);
            for (int y = -1; y <= 1; y++)
            {
                for (int x = -1; x <= 1; x++)
                {
                    long key = EncodeCell(cell.X + x, cell.Y + y);
                    if (!_grid.TryGetValue(key, out List<int> list))
                    {
                        continue;
                    }

                    for (int n = 0; n < list.Count; n++)
                    {
                        int i = list[n];
                        Vector2 deltaPos = _positions[i] - point;
                        float dist2 = deltaPos.LengthSquared();
                        if (dist2 > radius2 || dist2 < 0.000001f)
                        {
                            continue;
                        }

                        float dist = Mathf.Sqrt(dist2);
                        Vector2 normal = deltaPos / dist;
                        float penetration = radius - dist;
                        float relVel = (_velocities[i] - pointVelocity).Dot(normal);
                        float forceMag = (penetration * stiffness - relVel * damping) * clampedForceScale;
                        if (forceMag <= 0.0f)
                        {
                            continue;
                        }

                        Vector2 force = normal * forceMag;
                        Vector2 particleVelocity = _velocities[i];
                        Vector2 particlePosition = _positions[i];
                        particleVelocity += force / ParticleMass * delta;
                        if (clampedPush > 0.0f)
                        {
                            particlePosition += normal * (penetration * clampedPush);
                        }
                        ResolveBounds(ref particlePosition, ref particleVelocity);
                        _positions[i] = particlePosition;
                        _velocities[i] = particleVelocity;
                        sampleForce -= force;
                    }
                }
            }

            totalForce += sampleForce;
            totalTorque += offset.X * sampleForce.Y - offset.Y * sampleForce.X;
        }
    }

    public void ApplySolidSamples(
        Vector2[] samplePoints,
        Vector2[] sampleNormals,
        Vector2[] sampleVelocities,
        Vector2[] sampleOffsets,
        float radius,
        float stiffness,
        float damping,
        float forceScale,
        float positionPush,
        float maxForcePerSample,
        float delta,
        out Vector2 totalForce,
        out float totalTorque,
        out int hitSamples,
        out float totalPenetration
    )
    {
        totalForce = Vector2.Zero;
        totalTorque = 0.0f;
        hitSamples = 0;
        totalPenetration = 0.0f;

        if (_particleCount == 0 || radius <= 0.0f || samplePoints == null || samplePoints.Length == 0)
        {
            return;
        }

        BuildGrid(radius);
        float radius2 = radius * radius;
        float clampedForceScale = Math.Max(0.0f, forceScale);
        float clampedPush = Mathf.Clamp(positionPush, 0.0f, 1.0f);
        float clampedMaxForce = Math.Max(0.0f, maxForcePerSample);

        for (int s = 0; s < samplePoints.Length; s++)
        {
            Vector2 point = samplePoints[s];
            Vector2 pointVelocity = sampleVelocities[s];
            Vector2 offset = sampleOffsets[s];
            Vector2 sampleForce = Vector2.Zero;
            float samplePenetrationMax = 0.0f;

            Vector2I cell = GetCell(point, radius);
            for (int y = -1; y <= 1; y++)
            {
                for (int x = -1; x <= 1; x++)
                {
                    long key = EncodeCell(cell.X + x, cell.Y + y);
                    if (!_grid.TryGetValue(key, out List<int> list))
                    {
                        continue;
                    }

                    for (int n = 0; n < list.Count; n++)
                    {
                        int i = list[n];
                        Vector2 deltaPos = _positions[i] - point;
                        float dist2 = deltaPos.LengthSquared();
                        if (dist2 >= radius2 || dist2 < 0.000001f)
                        {
                            continue;
                        }

                        float dist = Mathf.Sqrt(dist2);
                        Vector2 normal = deltaPos / dist;
                        float penetration = radius - dist;
                        float relVel = (_velocities[i] - pointVelocity).Dot(normal);
                        float forceMag = (penetration * stiffness - relVel * damping) * clampedForceScale;
                        if (forceMag <= 0.0f)
                        {
                            continue;
                        }
                        if (clampedMaxForce > 0.0f && forceMag > clampedMaxForce)
                        {
                            forceMag = clampedMaxForce;
                        }

                        Vector2 force = normal * forceMag;
                        Vector2 particleVelocity = _velocities[i];
                        Vector2 particlePosition = _positions[i];
                        particleVelocity += force / ParticleMass * delta;
                        if (clampedPush > 0.0f)
                        {
                            particlePosition += normal * (penetration * clampedPush);
                        }

                        ResolveBounds(ref particlePosition, ref particleVelocity);
                        _positions[i] = particlePosition;
                        _velocities[i] = particleVelocity;
                        sampleForce -= force;
                        if (penetration > samplePenetrationMax)
                        {
                            samplePenetrationMax = penetration;
                        }
                    }
                }
            }

            totalForce += sampleForce;
            totalTorque += offset.X * sampleForce.Y - offset.Y * sampleForce.X;
            if (samplePenetrationMax > 0.0f)
            {
                hitSamples++;
                totalPenetration += samplePenetrationMax;
            }
        }
    }

    private void ResolveParticleCollisions()
    {
        float diameter = ParticleRadius * 2.0f;
        if (diameter <= 0.0f)
        {
            return;
        }

        float diameter2 = diameter * diameter;
        BuildGrid(diameter);
        for (int i = 0; i < _particleCount; i++)
        {
            Vector2 piPosition = _positions[i];
            Vector2 piVelocity = _velocities[i];
            Vector2I cell = GetCell(piPosition, diameter);
            for (int y = -1; y <= 1; y++)
            {
                for (int x = -1; x <= 1; x++)
                {
                    long key = EncodeCell(cell.X + x, cell.Y + y);
                    if (!_grid.TryGetValue(key, out List<int> list))
                    {
                        continue;
                    }

                    for (int n = 0; n < list.Count; n++)
                    {
                        int j = list[n];
                        if (j <= i)
                        {
                            continue;
                        }

                        Vector2 pjPosition = _positions[j];
                        Vector2 pjVelocity = _velocities[j];
                        Vector2 delta = piPosition - pjPosition;
                        float r2 = delta.LengthSquared();
                        if (r2 < diameter2 && r2 > 0.000001f)
                        {
                            float r = Mathf.Sqrt(r2);
                            float penetration = diameter - r;
                            Vector2 normal = delta / r;
                            Vector2 correction = normal * (penetration * 0.5f);
                            piPosition += correction;
                            pjPosition -= correction;

                            float relVel = (piVelocity - pjVelocity).Dot(normal);
                            if (relVel < 0.0f)
                            {
                                float impulse = -(1.0f + CollisionRestitution) * relVel * 0.5f;
                                Vector2 impulseVec = normal * impulse;
                                piVelocity += impulseVec;
                                pjVelocity -= impulseVec;
                            }

                            ResolveBounds(ref piPosition, ref piVelocity);
                            ResolveBounds(ref pjPosition, ref pjVelocity);
                            _positions[i] = piPosition;
                            _velocities[i] = piVelocity;
                            _positions[j] = pjPosition;
                            _velocities[j] = pjVelocity;
                        }
                    }
                }
            }
        }
    }

    private void BuildGrid(float cellSize)
    {
        for (int i = 0; i < _gridListsUsed; i++)
        {
            _gridLists[i].Clear();
        }
        _grid.Clear();
        _gridListsUsed = 0;

        for (int i = 0; i < _particleCount; i++)
        {
            Vector2 position = _positions[i];
            Vector2I cell = GetCell(position, cellSize);
            long key = EncodeCell(cell.X, cell.Y);
            if (!_grid.TryGetValue(key, out List<int> list))
            {
                if (_gridListsUsed < _gridLists.Count)
                {
                    list = _gridLists[_gridListsUsed];
                    list.Clear();
                }
                else
                {
                    list = new List<int>(8);
                    _gridLists.Add(list);
                }
                _gridListsUsed++;
                _grid[key] = list;
            }
            list.Add(i);
        }
    }

    private static Vector2I GetCell(Vector2 position, float cellSize)
    {
        float inv = 1.0f / cellSize;
        int x = (int)MathF.Floor(position.X * inv);
        int y = (int)MathF.Floor(position.Y * inv);
        return new Vector2I(x, y);
    }

    private static long EncodeCell(int x, int y)
    {
        return ((long)x << 32) ^ (uint)y;
    }

    public Rect2 GetBounds()
    {
        return _bounds;
    }

    private void EnsureParticleCapacity(int count)
    {
        if (_positions.Length >= count)
        {
            return;
        }

        int newSize = Math.Max(count, _positions.Length == 0 ? 256 : _positions.Length * 2);
        Array.Resize(ref _positions, newSize);
        Array.Resize(ref _velocities, newSize);
        Array.Resize(ref _densities, newSize);
        Array.Resize(ref _pressures, newSize);
    }

    private void SetupParticleRenderer()
    {
        if (_particleMeshInstance != null)
        {
            return;
        }

        _particleMeshInstance = new MultiMeshInstance2D();
        _particleMultiMesh = new MultiMesh();
        _particleMultiMesh.TransformFormat = MultiMesh.TransformFormatEnum.Transform2D;
        _particleMultiMesh.UseColors = false;
        _particleMeshInstance.Multimesh = _particleMultiMesh;

        var quad = new QuadMesh();
        quad.Size = new Vector2(ParticleRadius * 2.0f, ParticleRadius * 2.0f);
        _particleMultiMesh.Mesh = quad;

        var shader = new Shader();
        shader.Code = @"shader_type canvas_item;

uniform vec4 tint : source_color = vec4(0.2, 0.6, 0.95, 0.9);

void fragment() {
    vec2 uv = UV * 2.0 - 1.0;
    if (length(uv) > 1.0) {
        discard;
    }
    COLOR = tint;
}";

        var material = new ShaderMaterial();
        material.Shader = shader;
        material.SetShaderParameter("tint", ParticleColor);
        _particleMeshInstance.Material = material;

        AddChild(_particleMeshInstance);
    }

    private void SyncParticleMeshCapacity()
    {
        if (_particleMultiMesh == null)
        {
            return;
        }

        if (_particleMultiMesh.InstanceCount != _particleCount)
        {
            _particleMultiMesh.InstanceCount = _particleCount;
        }
    }

    private void UpdateParticleMeshTransforms()
    {
        if (_particleMultiMesh == null)
        {
            return;
        }

        bool radiusChanged = !Mathf.IsEqualApprox(_cachedRenderRadius, ParticleRadius);
        bool colorChanged = _cachedParticleColor != ParticleColor;
        if (radiusChanged)
        {
            _cachedRenderRadius = ParticleRadius;
            if (_particleMultiMesh.Mesh is QuadMesh quad)
            {
                quad.Size = new Vector2(ParticleRadius * 2.0f, ParticleRadius * 2.0f);
            }
        }

        if (colorChanged && _particleMeshInstance.Material is ShaderMaterial material)
        {
            _cachedParticleColor = ParticleColor;
            material.SetShaderParameter("tint", ParticleColor);
        }

        for (int i = 0; i < _particleCount; i++)
        {
            _particleMultiMesh.SetInstanceTransform2D(i, new Transform2D(0.0f, _positions[i]));
        }
    }
}
