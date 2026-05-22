using UnityEngine;
using UnityEngine.UIElements;

namespace CupkekGames.Graphs.Editor
{
    /// <summary>
    /// Paints the canvas background — a flat fill plus a line grid that
    /// translates and scales with the view. Cheap: a single
    /// <c>BeginPath/Stroke</c> pair, ~150 line segments at full HD.
    /// </summary>
    public class GridBackground : VisualElement
    {
        /// <summary>World-space spacing of minor grid lines. Snap-to-grid uses the same value.</summary>
        public const float MinorSpacing = 24f;

        /// <summary>Every Nth minor line is drawn thicker.</summary>
        const int MajorEvery = 4;

        /// <summary>Hide grid below this on-screen spacing (overcrowded otherwise).</summary>
        const float HideBelowScreenSpacing = 4f;

        static readonly Color BackgroundColor = GraphTheme.CanvasBackdrop;
        static readonly Color MinorLineColor  = GraphTheme.GridLineMinor;
        static readonly Color MajorLineColor  = GraphTheme.GridLineMajor;

        Vector2 _viewOffset = Vector2.zero;
        float _viewZoom = 1f;
        bool _visible = true;

        /// <summary>
        /// Toggle the grid lines on / off. The flat background fill stays
        /// either way — only the line overlay is hidden so users still see
        /// the dark canvas backdrop.
        /// </summary>
        public bool Visible
        {
            get => _visible;
            set
            {
                if (_visible == value) return;
                _visible = value;
                MarkDirtyRepaint();
            }
        }

        public GridBackground()
        {
            style.backgroundColor = BackgroundColor;
            generateVisualContent += OnPaint;
        }

        /// <summary>
        /// Called by <see cref="GraphCanvas"/> when the view changes; triggers
        /// a repaint so the grid follows the canvas pan/zoom.
        /// </summary>
        public void SetView(Vector2 offset, float zoom)
        {
            _viewOffset = offset;
            _viewZoom = zoom;
            MarkDirtyRepaint();
        }

        void OnPaint(MeshGenerationContext ctx)
        {
            if (!_visible) return;

            float screenSpacing = MinorSpacing * _viewZoom;
            if (screenSpacing < HideBelowScreenSpacing)
                return;

            Rect rect = contentRect;
            var painter = ctx.painter2D;
            painter.lineWidth = 1f;

            // Find the first grid line position (in screen-space) inside the
            // viewport. World-space line k is at screen
            //   x = k * MinorSpacing * zoom + offset.x
            // so the first k with screen-x >= 0 is:
            //   k0 = ceil(-offset.x / (MinorSpacing * zoom))
            float modX = Mod(_viewOffset.x, screenSpacing);
            float modY = Mod(_viewOffset.y, screenSpacing);

            // Walk world-line index along with screen position so we can flag
            // major lines.
            int startKx = Mathf.CeilToInt(-_viewOffset.x / screenSpacing);
            int startKy = Mathf.CeilToInt(-_viewOffset.y / screenSpacing);

            // ---- Minor lines ----
            painter.strokeColor = MinorLineColor;
            painter.BeginPath();
            int k = startKx;
            for (float x = modX; x < rect.width; x += screenSpacing, k++)
            {
                if (k % MajorEvery == 0) continue;
                painter.MoveTo(new Vector2(x, 0f));
                painter.LineTo(new Vector2(x, rect.height));
            }
            k = startKy;
            for (float y = modY; y < rect.height; y += screenSpacing, k++)
            {
                if (k % MajorEvery == 0) continue;
                painter.MoveTo(new Vector2(0f, y));
                painter.LineTo(new Vector2(rect.width, y));
            }
            painter.Stroke();

            // ---- Major lines ----
            painter.strokeColor = MajorLineColor;
            painter.BeginPath();
            k = startKx;
            for (float x = modX; x < rect.width; x += screenSpacing, k++)
            {
                if (k % MajorEvery != 0) continue;
                painter.MoveTo(new Vector2(x, 0f));
                painter.LineTo(new Vector2(x, rect.height));
            }
            k = startKy;
            for (float y = modY; y < rect.height; y += screenSpacing, k++)
            {
                if (k % MajorEvery != 0) continue;
                painter.MoveTo(new Vector2(0f, y));
                painter.LineTo(new Vector2(rect.width, y));
            }
            painter.Stroke();
        }

        // Positive-result modulo (C# % can return negatives for negative inputs).
        static float Mod(float a, float b)
        {
            float r = a % b;
            return r < 0f ? r + b : r;
        }
    }
}
