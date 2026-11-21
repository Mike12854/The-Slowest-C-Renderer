using System;
using System.Numerics;
using Silk.NET.Input;

namespace Sandbox;


public sealed class Camera
{
    // UE5 Standard: X = Vorne, Y = Rechts, Z = Höhe
    public Vector3 Position { get; set; } = new(-4.0f, 0.0f, 1.5f);
    
    // Rotation in Grad (wie in UE5: Pitch = Hoch/Runter, Yaw = Links/Rechts)
    public float YawDegrees { get; set; } = 0.0f;
    public float PitchDegrees { get; set; } = -15.0f;

    public float FieldOfViewDegrees { get; set; } = 60.0f;
    public float NearPlane { get; set; } = 0.1f;
    public float FarPlane { get; set; } = 1000.0f;
    
    // Flugsteuerungs-Parameter
    public float MoveSpeed { get; set; } = 25.0f;
    public float SprintMultiplier { get; set; } = 3.0f;
    public float MouseSensitivity { get; set; } = 0.15f;
    
    private Vector2? _lastMousePosition;
    
    
    public Vector3 Forward
    {
        get
        {
            float yaw = MathF.PI / 180f * YawDegrees;
            float pitch = MathF.PI / 180f * Math.Clamp(PitchDegrees, -89.9f, 89.9f);

            float cosPitch = MathF.Cos(pitch);
            return Vector3.Normalize(new Vector3(
                MathF.Cos(yaw) * cosPitch, // +X = Vorne
                MathF.Sin(yaw) * cosPitch, // +Y = Rechts
                MathF.Sin(pitch)           // +Z = Höhe
            ));
        }
    }
    
    public Vector3 Right => Vector3.Normalize(Vector3.Cross(Vector3.UnitZ, Forward));
    public Vector3 Up => Vector3.Cross(Forward, Right);
    
    public void Update(float deltaTime, IKeyboard keyboard, IMouse mouse)
    {
        bool rightMouseDown = mouse.IsButtonPressed(MouseButton.Right);

        if (rightMouseDown)
        {
            mouse.Cursor.CursorMode = CursorMode.Raw;

            if (_lastMousePosition.HasValue)
            {
                Vector2 delta = mouse.Position - _lastMousePosition.Value;
                YawDegrees += delta.X * MouseSensitivity;
                PitchDegrees = Math.Clamp(PitchDegrees - delta.Y * MouseSensitivity, -89.9f, 89.9f);
            }
            _lastMousePosition = mouse.Position;
        }
        else
        {
            mouse.Cursor.CursorMode = CursorMode.Normal;
            _lastMousePosition = null;
            return;
        }

        float speed = MoveSpeed * (keyboard.IsKeyPressed(Key.ShiftLeft) ? SprintMultiplier : 1.0f);
        Vector3 moveDir = Vector3.Zero;

        if (keyboard.IsKeyPressed(Key.W)) moveDir += Forward;
        if (keyboard.IsKeyPressed(Key.S)) moveDir -= Forward;
        if (keyboard.IsKeyPressed(Key.D)) moveDir += Right;
        if (keyboard.IsKeyPressed(Key.A)) moveDir -= Right;
        if (keyboard.IsKeyPressed(Key.E)) moveDir += Vector3.UnitZ;
        if (keyboard.IsKeyPressed(Key.Q)) moveDir -= Vector3.UnitZ;

        if (moveDir.LengthSquared() > 0.0001f)
        {
            Position += Vector3.Normalize(moveDir) * speed * deltaTime;
        }
    }
    
    public void OnMouseScroll(float scrollDeltaY)
    {
        MoveSpeed = Math.Clamp(MoveSpeed + scrollDeltaY, 1.0f, 300.0f);
    }
    
    public Matrix4x4 GetViewProjectionMatrix(float aspectRatio)
    {
        Vector3 f = Forward;
        Vector3 r = Right;
        Vector3 u = Up;

        // 1. View-Matrix: UE5 (X=Forward, Y=Right, Z=Up) -> Vulkan View-Space (X=Right, Y=Down, Z=Forward)
        var view = new Matrix4x4(
            r.X, -u.X, f.X, 0.0f,
            r.Y, -u.Y, f.Y, 0.0f,
            r.Z, -u.Z, f.Z, 0.0f,
            -Vector3.Dot(r, Position),
            Vector3.Dot(u, Position),
            -Vector3.Dot(f, Position),
            1.0f
        );

        // 2. Projektions-Matrix: Left-Handed, Reversed-Z Vulkan Depth [1, 0]
        float fovRad = MathF.PI / 180f * FieldOfViewDegrees;
        float yScale = 1.0f / MathF.Tan(fovRad * 0.5f);
        float xScale = yScale / Math.Max(0.0001f, aspectRatio);

        float zRange = NearPlane / (NearPlane - FarPlane);
        var proj = new Matrix4x4(
            xScale, 0.0f,   0.0f,               0.0f,
            0.0f,   yScale, 0.0f,               0.0f,
            0.0f,   0.0f,   zRange,             1.0f,
            0.0f,   0.0f,  -FarPlane * zRange,  0.0f
        );

        return view * proj;
    }
}