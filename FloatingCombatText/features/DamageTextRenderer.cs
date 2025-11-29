using System;
using System.Collections.Generic;
using System.Linq;
using Cairo;
using Vintagestory.API.Client;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Color = System.Drawing.Color;

namespace FloatingCombatText;

public class DamageTextRenderer : IRenderer
{
    private const float FloatSpeed = 0.5f;
    private const float FontSize = 28f;

    private readonly ICoreClientAPI _api;
    public static readonly int DefaultDamageColor = Color.Firebrick.ToArgb();
    public static readonly int DefaultHealingColor = Color.ForestGreen.ToArgb();

    public DamageTextRenderer(ICoreClientAPI api)
    {
        _api = api;
    }

    public double RenderOrder => 1.0;
    public int RenderRange => 100;

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        foreach (Entity entity in FloatingDamageText.CombatTexts.Keys)
        {
            foreach (FloatingDamageText.CombatTextEntry entry in FloatingDamageText.CombatTexts[entity])
            {
                // Calculate alpha (fade out)
                float age = entry.GetAge();
                float alpha = 1f - (age / 2 / FloatingDamageText.LifetimeSeconds);

                Vec3d screenPos = Vec3d.Zero;
                if (entity == _api.World.Player.Entity)
                {
                    double timePassed = age * FloatSpeed;
                    double screenSizeFactor = _api.Render.FrameHeight * 0.2;
                    Vec2d pos =
                        CubicBezier(timePassed, new(0, 0), new(.25, -1.2), new Vec2d(0.55, -2.0), new(1, 1)) *
                        screenSizeFactor;
                    screenPos = new Vec3d((_api.Render.FrameWidth / 2.0) - pos.X,
                        _api.Render.FrameHeight * 0.1 - pos.Y,
                        0);
                }
                else
                {
                    // Calculate position (float upward)
                    Vec3d renderPos = entry.Position.Clone();
                    renderPos.Y += age * FloatSpeed;

                    // Project 3D world position to screen (ViewMat handles camera transform)
                    screenPos = MatrixToolsd.Project(
                        renderPos,
                        _api.Render.PerspectiveProjectionMat,
                        _api.Render.PerspectiveViewMat,
                        _api.Render.FrameWidth,
                        _api.Render.FrameHeight
                    );
                }

                // Only render if in front of camera
                if (screenPos == null || screenPos.Z < 0) continue;
                string sign = "+";
                if (entry.Color == DefaultDamageColor)
                {
                    sign = "-";
                }

                string text = sign + entry.AbsDeltaHealth.ToString("0.#");

                // Extract color components and apply alpha for fade-out
                Color color = Color.FromArgb(entry.Color);
                double[] textColor = { color.R / 255.0, color.G / 255.0, color.B / 255.0, alpha };
                double[] strokeColor = { 0, 0, 0, alpha }; // Black stroke

                float screenX = (float)screenPos.X;
                float screenY = _api.Render.FrameHeight - (float)screenPos.Y;

                // Create font with stroke (border) built-in
                CairoFont font = CairoFont.WhiteMediumText()
                    .WithFontSize(FontSize)
                    .WithColor(textColor)
                    .WithWeight(FontWeight.Bold)
                    .WithStroke(strokeColor, 5.0);

                entry.Texture ??= _api.Gui.TextTexture.GenTextTexture(text, font);

                _api.Render.Render2DTexture(
                    entry.Texture.TextureId,
                    screenX - entry.Texture.Width / 2f,
                    screenY - entry.Texture.Height / 2f,
                    entry.Texture.Width,
                    entry.Texture.Height,
                    50
                );
            }
        }
    }

    private static Vec2d CubicBezier(double t, Vec2d p0, Vec2d p1, Vec2d p2, Vec2d p3)
    {
        double cx = 3 * (p1.X - p0.X);
        double cy = 3 * (p1.Y - p0.Y);
        double bx = 3 * (p2.X - p1.X) - cx;
        double by = 3 * (p2.Y - p1.Y) - cy;
        double ax = p3.X - p0.X - cx - bx;
        double ay = p3.Y - p0.Y - cy - by;
        double cube = Math.Pow(t, 3);
        double square = Math.Pow(t, 2);

        double resX = (ax * cube) + (bx * square) + (cx * t) + p0.X;
        double resY = (ay * cube) + (by * square) + (cy * t) + p0.Y;

        return new Vec2d(resX, resY);
    }

    public void Dispose()
    {
    }
}